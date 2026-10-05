namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-AI-01: a deterministic rule-based skirmish commander. It plays
/// through exactly the same command interface as a human or the network
/// layer - it holds no privileged access and mutates nothing, so an AI match
/// is as replayable and desync-safe as any other. All state it keeps is its
/// own and updates deterministically from world state.
///
/// Doctrine (deliberately simple; the point is a full closed loop, not
/// brilliance): establish power -> refinery -> barracks -> factory; keep one
/// harvester working; alternate rifle/cannon production, each ROUTED TO ITS
/// OWN PRODUCER (ADR-009); add a defensive turret, then the radar uplink
/// (ADR-008); when six combat units stand ready, attack-move the wave at the
/// nearest enemy structure, and keep the waves coming.
///
/// ADR-009 clause 7, the finding that inverts the intuitive risk: an AI that
/// does not know about produced_at does not "still build tanks and look
/// almost normal", it builds NOTHING. `lineHolds` opens both faction branches
/// on rifles and rockets, which are barracks units; gate Produce on
/// produced_at without teaching the ladder and the routing, and every one of
/// them is refused at the factory, `army` stays 0 forever, lineHolds is never
/// true, and the AI produces only refused infantry in perpetuity and never
/// attacks. That is why the ladder, the routing and the per-producer queue
/// guards below are not optional polish.
///
/// The TUNING is not in this file. Every number that describes the commander -
/// its decision beat, its wave size (the "six" above), the beat ratio and the
/// mining of each rung, and Brutal's declared handicap - is authored in /data/ai
/// and registered on the World, per CLAUDE.md's rule that gameplay numbers live
/// in /data. AiTuning holds the compiled reference copy those files must
/// reproduce, and aituninggate proves both halves. The tuning rides
/// World.CatalogueChecksum because it MUST: the commander's numbers were
/// compiled, so two LAN peers agreed on them by construction, and authoring them
/// would otherwise let peers holding different files issue different AI commands
/// and desync a match in which every unit, building and gun still matched.
/// </summary>
/// <summary>DR-14 / doc 28: the difficulty ladder GDD line 76 promised, kept
/// ORTHOGONAL to personality. Personality (Standard, Rusher, Turtle) is a
/// commander's taste in wave size; difficulty is how good it is, and the review
/// found the two had been conflated - the only knob a player could reach varied
/// wave size, which changes an opponent's shape rather than its strength.
/// Difficulty owns the decision beat and the economy, which is the genre's
/// honest ladder: a worse commander thinks more slowly and mines less, and only
/// the top rung is allowed a handicap, declared rather than hidden.
///
/// The NUMBERS behind each rung are no longer here. They are authored in
/// data/ai (ai_easy, ai_normal, ai_hard, ai_brutal), which is where the design
/// reasoning for each now lives too, beside the values a designer can actually
/// edit; AiTuning holds the compiled reference copy those files must reproduce.
/// This enum is only the player-facing pick, and AiTuning.RungIdOf maps it to
/// the row.</summary>
public enum AiDifficulty
{
    /// <summary>GDD line 76 "no cheats, slow". See data/ai/ai_easy.yaml.</summary>
    Easy = 0,
    /// <summary>GDD line 76 "competent build orders", and the IDENTITY rung that
    /// keeps the goldens byte-identical. See data/ai/ai_normal.yaml.</summary>
    Normal = 1,
    /// <summary>GDD line 76 "strong macro, honest information". See
    /// data/ai/ai_hard.yaml.</summary>
    Hard = 2,
    /// <summary>GDD line 76 "resource handicap, clearly labelled as cheating".
    /// See data/ai/ai_brutal.yaml, and StartingCreditHandicap below for why the
    /// AI never applies it to itself.</summary>
    Brutal = 3,
}

public sealed class SkirmishAI
{
    private readonly int _player;
    // The three resolved tuning numbers, composed once in the constructor from
    // the personality and rung rows in data/ai. Nothing below re-reads the
    // catalogue, so the commander holds tuning rather than a world.
    private readonly int _actEvery;   // decision beat; larger = slower commander (the ladder's honest knob)
    private readonly int _waveSize;   // units per attack wave (PERSONALITY, not difficulty - DR-14)
    private readonly int _harvestersPerRefinery;  // DR-14: the ladder's economy knob
    // P8-17 (ADR-072): the air answer's two numbers, from the personality row
    // beside the wave size, because a home guard's size is shape, not strength.
    private readonly int _antiAirCap;       // most anti-air units it builds once it has seen air
    private readonly int _antiAirGarrison;  // how many of those it holds at home

    /// <summary>P8-17 (ADR-072): the MOST enemy aircraft this commander has seen
    /// at once, with its own eyes, so far this match. Zero until the first
    /// sighting, and every air-answer branch below is gated on it, which is what
    /// keeps a ground-only match (every golden) byte-identical.
    ///
    /// A high-water mark rather than this beat's count, and that is a decision:
    /// a commander that has watched three flyers burn its harvesters does not
    /// forget the enemy flies the moment they leave its sight. It is memory of
    /// what it SAW, never a read through fog, so D9's spirit holds. AI-internal
    /// like every field here: never hashed, never saved, and a loaded match's
    /// commander learns it again from the next sighting.</summary>
    private int _airSeen;
    /// <summary>P8-17: has the anti-air answer reached its target since the
    /// last time the high-water mark rose? See orderAntiAir in Act.</summary>
    private bool _antiAirStood;
    private int _lastAirDefendTick = -10_000;

    /// <summary>The garrison's re-order cadence in ticks, and the three guard
    /// distances (squared cells) the garrison and the escorts work to. Named
    /// because P8-17's anti-air garrison and escort reuse each of them rather
    /// than inventing its own: a second literal would be a copy that could
    /// drift. The values are the ones these sites always used.</summary>
    private const int DefendCadenceTicks = 60;
    private const int EconomyGuardSq = 64;    // 8 cells round a harvester (the intruder census)
    private const int BaseGuardSq = 196;      // 14 cells round a structure (the intruder census)
    private const int HomeLeashSq = 144;      // 12 cells: an idle garrison further out drifts home
    private const int EscortLeashSq = 9;      // 3 cells: an escort further from its ward closes up

    /// <summary>
    /// P7-7a: how many refineries a commander runs per base, from GDD s4's
    /// stated equilibrium ("a player floats at 2 refineries / 3 harvesters on
    /// one base"). A compiled constant rather than a /data rung knob, and that
    /// is deliberate: it is the GAME's designed economy, the same for every
    /// difficulty, and the ladder's knobs are what make one commander better
    /// than another. Turning this into a rung value would make "plays the
    /// economy the game is designed around" a difficulty setting.
    /// </summary>
    public const int RefineriesPerBase = 2;
    private int _produced;
    /// <summary>DR-10: the Fire Sale fires ONCE. AI-internal state, like every
    /// field here: never hashed, never saved - the AI is sim-adjacent and its
    /// commands are what enter the record, not its mind.</summary>
    private bool _lastStandMade;
    private int _lastWaveTick = -10_000;
    private int _lastDefendTick = -10_000;

    /// <summary>The Standard personality at the given rung, from a world's
    /// registered tuning or, with no world in hand, from the compiled reference.
    /// The numbers themselves live in /data/ai; only the composition rule lives
    /// here.</summary>
    public SkirmishAI(int player, AiDifficulty difficulty = AiDifficulty.Normal, World? tuning = null)
        : this(player, AiTuning.StandardId, difficulty, tuning) { }

    /// <summary>
    /// The one place a commander is composed from its two authored rows. The
    /// personality supplies the shape (its beat and its wave size) and the rung
    /// supplies the strength (how that beat is scaled and how hard it mines),
    /// which is DR-14's orthogonality expressed as an operation rather than as a
    /// comment.
    ///
    /// The tuning is READ from the world rather than from a static, because it
    /// is catalogue data: it rides the checksum the LAN hello and every save and
    /// replay compare, and it is frozen after tick 0. A null world means "no
    /// /data in hand" and takes the compiled reference, exactly as a bare World
    /// plays the compiled weapon table; the AI keeps no reference to the world
    /// beyond this constructor, so it still holds no world state and no
    /// privileged access.
    /// </summary>
    private SkirmishAI(int player, int personalityId, AiDifficulty difficulty, World? tuning)
    {
        _player = player;
        var shape = Resolve(personalityId, tuning);
        var rung = Resolve(AiTuning.RungIdOf(difficulty), tuning);
        _waveSize = shape.WaveSize;
        // DR-14: the ladder SCALES the personality's beat rather than replacing
        // it, so personality keeps whatever beat it asked for and difficulty
        // says how fast that commander thinks. Normal's ratio is 1/1, which is
        // why every existing caller - and therefore every golden - is unmoved.
        // Integer arithmetic and integer truncation, both load-bearing: the
        // ratio is authored as a numerator and a denominator so that Brutal's
        // two-thirds is exact with no floating-point type anywhere in /sim, and
        // 15 * 2 / 3 truncates to 10 exactly as the compiled expression did.
        // The floor matters at a beat of 1: a zero would make Tick % 0 throw.
        int beat = shape.ActEvery * rung.BeatNumerator / rung.BeatDenominator;
        _actEvery = beat < 1 ? 1 : beat;
        // Income headroom is the ladder's other honest knob: a strong commander
        // runs a second harvester per refinery, which is macro rather than a
        // gift.
        _harvestersPerRefinery = rung.HarvestersPerRefinery;
        // P8-17: shape, so from the personality, exactly as the wave size is.
        _antiAirCap = shape.AntiAirCap;
        _antiAirGarrison = shape.AntiAirGarrison;
    }

    /// <summary>The tuning row for an id: the world's registered one where a
    /// world is in hand, the compiled reference otherwise.</summary>
    private static AiTuningDef Resolve(int tuningId, World? tuning)
        => tuning is null ? AiTuning.Get(tuningId) : tuning.GetAiTuning(tuningId);

    /// <summary>DR-14b: the resolved decision beat, in ticks. Read-only and
    /// purely informational - nothing in Act consults it through this property,
    /// so exposing it changes no behaviour and moves no hash. It exists because
    /// the alternative way to check that a picked rung reached the commander is
    /// to infer the beat from how many commands it issues, and that inference
    /// is only valid in a world rich enough for every beat to produce one. The
    /// client harness first tried it in a world where the AI could afford
    /// nothing, measured zero beats at every rung, and failed on 0 &lt; 0. The
    /// beat itself is the thing being asserted, so the beat itself is what the
    /// check should read.</summary>
    public int DecisionBeat => _actEvery;

    /// <summary>P7-8f: which seat this commander plays. Read-only and purely
    /// informational, exactly as DecisionBeat is, so exposing it changes no
    /// behaviour and moves no hash. It exists because the lockstep client orders
    /// its attached commanders by seat, and an ORDER is only load-bearing if the
    /// thing it sorts on can be read: sorting by list position instead would make
    /// the determinism of a LAN match depend on the order a caller happened to
    /// build its list in, which is precisely the kind of unstated convention that
    /// desyncs one peer against the other.</summary>
    public int Seat => _player;

    /// <summary>DR-14: Brutal's declared handicap, in starting credits, applied
    /// by whatever builds the match and NEVER by the AI itself. This is not
    /// squeamishness: SkirmishAI holds no privileged access and mutates nothing,
    /// which is what makes an AI match replayable - a replay re-runs the bare
    /// command stream with NO AI attached (ReplayCheck), so an AI that granted
    /// itself credits would desync every replay of its own match. A handicap
    /// that lives in setup is ordinary starting state and stays replay-safe.
    /// GDD line 76 requires it be labelled as cheating wherever it is offered.
    /// The number itself is authored in data/ai/ai_brutal.yaml, so a world in
    /// hand is read in preference to the compiled reference.</summary>
    public static long StartingCreditHandicap(AiDifficulty difficulty, World? tuning = null)
        => Resolve(AiTuning.RungIdOf(difficulty), tuning).StartingCreditHandicap;

    // Personality presets (TICKET-AI-03): the knobs make the SHAPE of the
    // opponent, and they are now authored in data/ai rather than spelled here.
    // Each takes a rung as well, which is what lets a player pick taste and
    // strength independently (DR-14b), and an optional world, which is what lets
    // a real match play the AUTHORED numbers while the harness scenarios that
    // build a bare World keep the compiled ones. Both parameters default, so
    // every caller written before either existed builds the identical commander
    // it always did.
    public static SkirmishAI Standard(int player, AiDifficulty difficulty = AiDifficulty.Normal, World? tuning = null)
        => new(player, AiTuning.StandardId, difficulty, tuning);
    public static SkirmishAI Rusher(int player, AiDifficulty difficulty = AiDifficulty.Normal, World? tuning = null)
        => new(player, AiTuning.RusherId, difficulty, tuning);
    public static SkirmishAI Turtle(int player, AiDifficulty difficulty = AiDifficulty.Normal, World? tuning = null)
        => new(player, AiTuning.TurtleId, difficulty, tuning);

    // Difficulty presets (DR-14): the ladder GDD line 76 named. Each keeps the
    // standard personality, so a player picks strength here and taste above.
    public static SkirmishAI Easy(int player, World? tuning = null) => new(player, AiDifficulty.Easy, tuning);
    public static SkirmishAI Hard(int player, World? tuning = null) => new(player, AiDifficulty.Hard, tuning);
    public static SkirmishAI Brutal(int player, World? tuning = null) => new(player, AiDifficulty.Brutal, tuning);

    public void Act(World w, List<Command> output)
    {
        if (w.Tick % _actEvery != 0) return;

        int cy = -1, factory = -1, refinery = -1, barracks = -1;
        bool hasPlant = false, hasTurret = false, hasRadar = false;
        // P7-18: tracked by TYPE rather than by kind, because a side's own
        // defence and the common turret can share a kind (the Sodality's Shroud
        // Nest and the common emplacement are both EntityKind.Emplacement), so
        // a kind flag would report the shared one as the faction one.
        bool hasFactionDefence = false;
        // P7-18: its OWN side's armed defence, 0 for a side that has none.
        // Asked BEFORE the scan, because the scan needs it to recognise one.
        int factionDefence = w.BuildableFactionDefence(_player);
        // P7-5d: does anything of mine reveal cloak? Asked of the ENTITY FLAG
        // rather than of a building kind, so it counts the Directorate's Sentinel
        // Scout and the Sodality's Watch Post alike without naming either.
        bool hasDetector = false;
        int harvesters = 0, army = 0, supply = 0, draw = 0;
        int cyCount = 0, refineryCount = 0, ownMcv = -1, scouts = 0, ownEngineer = -1;
        // P8-17 (ADR-072): my anti-air units, and the enemy aircraft I can see
        // this beat. Both stay 0 in a ground-only match.
        int antiAir = 0, airSeenNow = 0;
        bool hasSuper = false;
        int readySuper = -1, enemyRefinery = -1;
        int enemyStructure = -1;
        Fix64 bestEnemyD = Fix64.MaxValue;
        Fix64 bestRefineryD = Fix64.MaxValue;
        Fix64 homeX = Fix64.Zero, homeY = Fix64.Zero;

        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive) continue;
            // P7-8g: the AI's census splits on exactly the two questions the sim
            // now names. The first arm is OWNERSHIP (what have I got), the second
            // is HOSTILITY (what should I hit), and they are not each other's
            // negation the moment teams exist - which is why the else-if below
            // restates the question rather than relying on falling through.
            if (World.IsOwnedBy(in e, _player))
            {
                if (e.Detector) hasDetector = true;
                // P7-18: compared against the type the QUERY returned, so the
                // flag and the rung can never disagree about what "my side's
                // defence" means.
                if (World.IsStructure(e.Kind) && e.StructType == factionDefence) hasFactionDefence = true;
                switch (e.Kind)
                {
                    case EntityKind.ConstructionYard:
                        cyCount++;
                        if (cy < 0) { cy = i; homeX = e.X; homeY = e.Y; }
                        break;
                    case EntityKind.Factory: factory = i; break;
                    // ADR-009 clause 7: the barracks is tracked exactly as the
                    // factory is, because it is now a producer the AI routes
                    // to, not scenery.
                    case EntityKind.Barracks: barracks = i; break;
                    case EntityKind.Refinery: refinery = i; refineryCount++; break;
                    case EntityKind.PowerPlant: hasPlant = true; break;
                    case EntityKind.Turret: hasTurret = true; break;
                    case EntityKind.RadarUplink: hasRadar = true; break;
                    case EntityKind.Superweapon:
                        hasSuper = true;
                        if (e.ChargeTicks == 0 && e.StrikeTicks < 0) readySuper = i;
                        break;
                    case EntityKind.Harvester: harvesters++; break;
                    case EntityKind.Unit:
                        if (e.UnitType == World.McvUnitType) ownMcv = i;
                        else if (e.UnitType == 6) scouts++; // support, not line strength
                        // P8-17: anti-air is not line strength either. It
                        // cannot shoot the ground (ADR-028 clause 3), so a
                        // flak track counted as army would launch waves that
                        // arrive one gun short. Asked of the WEAPON rather than
                        // the type id, so it means "can shoot aircraft".
                        else if (IsAntiAir(w, in e)) antiAir++;
                        else
                        {
                            // ADR-021: note an engineer so the outpost logic can
                            // send it. Deliberately INSIDE the existing else, so
                            // an engineer still counts toward army exactly as it
                            // did before: changing that would shift wave timing
                            // and move every golden.
                            if (e.UnitType == EngineerType && ownEngineer < 0) ownEngineer = i;
                            army++;
                        }
                        break;
                }
                supply += e.PowerSupply;
                draw += e.PowerDraw;
            }
            // ADR-009 clause 7: Barracks, RadarUplink and Airfield join the
            // wave-target kinds, or the AI walks straight past the building
            // this wave exists to add - an enemy barracks pumping infantry
            // would never be picked as the nearest production structure.
            else if (w.IsEnemyOf(in e, _player)
                     && e.Kind is EntityKind.ConstructionYard or EntityKind.Factory or EntityKind.Refinery
                        or EntityKind.PowerPlant or EntityKind.Barracks or EntityKind.RadarUplink or EntityKind.Airfield)
            {
                // The enemy REFINERY, nearest by the same measure the structure
                // pick uses. This was "the first refinery in entity order",
                // which with ONE opponent is the same thing and with three is
                // an artefact of spawn order: the wave and the superweapon
                // would both go for whichever player happens to sit earliest in
                // the array, every time, for the whole match. It reads as the
                // commander inexplicably focusing one player, and it is
                // perfectly deterministic and reproducible, which is exactly
                // why it would never be reported as a bug.
                //
                // The refinery is preferred over other production at both use
                // sites, so this line is what decides where a wave GOES.
                if (e.Kind == EntityKind.Refinery)
                {
                    if (cy >= 0)
                    {
                        Fix64 rd = Fix64.DistSq(e.X - homeX, e.Y - homeY);
                        if (rd < bestRefineryD) { bestRefineryD = rd; enemyRefinery = i; }
                    }
                    else if (enemyRefinery < 0) enemyRefinery = i;
                }
                // Nearest enemy production structure is the wave target.
                if (cy >= 0)
                {
                    Fix64 d = Fix64.DistSq(e.X - homeX, e.Y - homeY);
                    if (d < bestEnemyD) { bestEnemyD = d; enemyStructure = i; }
                }
                else if (enemyStructure < 0) enemyStructure = i;
            }
            // P8-17 (ADR-072): the AIR-THREAT CENSUS. An enemy aircraft counts
            // only if this commander can see it: its cell lit in MY fog this
            // tick, and not an undetected cloaked unit (D9). The commander
            // never reads through fog to learn the enemy flies.
            else if (w.IsEnemyOf(in e, _player) && SeesAircraft(w, in e))
            {
                airSeenNow++;
            }
        }
        // A larger raid than any before reopens the build-up (see orderAntiAir).
        if (airSeenNow > _airSeen) { _airSeen = airSeenNow; _antiAirStood = false; }
        if (cy < 0)
        {
            // DR-10: the last stand. This return used to be plain silence - a
            // decapitated AI issued nothing, forever, and the endgame petered
            // out while the player hunted its leftovers. The whole block lives
            // inside that formerly-silent state, which is the neutrality
            // argument: in any match where the AI keeps its yard (every
            // golden), not one command changes.
            //
            // With a rebuild MCV in hand, the honest move is to USE it - the
            // comeback rule exists and the AI never exercised it. Deploy is
            // re-issued each beat until it lands (the sim refuses an invalid
            // cell; the MCV keeps trying as it moves).
            if (ownMcv >= 0)
            {
                output.Add(new Command(w.Tick, _player, CommandType.Deploy, ownMcv, Fix64.Zero, Fix64.Zero));
                return;
            }
            // P7-17: and being able to BUY one counts as having one. DR-10's
            // comeback rule keyed on OWNING an MCV and never asked whether it
            // could get one, so a commander holding a Factory, the tier gate
            // the MCV waits behind, and twenty thousand credits against a three
            // thousand credit unit sold its whole base - radar included - and
            // called itself beaten. MEASURED: five structures sold for a 2850
            // credit consolation, one production order from a full rebuild.
            //
            // The seventeenth time P7 has found this defect: a rule keyed on an
            // INSTANCE where it means a CAPABILITY. "Do I have an MCV" should
            // always have read "can I get an MCV".
            //
            // ADR-058 widened the gap it hid in. While the MCV's prerequisite
            // was the factory it is produced at, having one and being able to
            // buy one nearly coincided; putting it behind the Radar Uplink made
            // them genuinely different states.
            var mcvDef = w.GetUnitType(World.McvUnitType);
            if (w.HasPrereqs(_player, mcvDef.Prereqs))
            {
                // The producer is the MCV's OWN produced_at (ADR-009 clause 2)
                // rather than the factory by name, so this keeps working if
                // Q020 ever moves it.
                int producer = -1;
                bool building = false;
                for (int i = 0; i < w.Entities.Count && !building; i++)
                {
                    var p = w.Entities[i];
                    if (!p.Alive || !World.IsOwnedBy(in p, _player) || !World.IsStructure(p.Kind)) continue;
                    if (p.StructType != mcvDef.ProducedAt) continue;
                    if (producer < 0) producer = i;
                    // AN MCV ALREADY ON ORDER COUNTS. Without this the fix
                    // failed its own first measurement: the order went out,
                    // and on the NEXT beat the producer's queue was no longer
                    // empty, so the search skipped it and fell straight through
                    // to the fire sale - selling the very factory that was
                    // building the comeback. Measured, not reasoned about: the
                    // sale count did not move at all.
                    foreach (int q in w.QueueContents(i))
                        if (q == World.McvUnitType) { building = true; break; }
                }
                // Waiting for one to finish is not being beaten.
                if (building) return;
                if (producer >= 0 && w.Credits(_player) >= mcvDef.Cost)
                {
                    output.Add(new Command(w.Tick, _player, CommandType.Produce, producer,
                                           Fix64.Zero, Fix64.Zero, World.McvUnitType));
                    return;
                }
            }
            // No yard, no MCV and no way to get one: beaten. Sell everything
            // still standing and send every unit in one last wave - the classic
            // Fire Sale, the ending that goes out with a bang, not a mop-up. Once.
            if (_lastStandMade || enemyStructure < 0) return;
            _lastStandMade = true;
            for (int i = 0; i < w.Entities.Count; i++)
            {
                var e = w.Entities[i];
                if (!e.Alive || !World.IsOwnedBy(in e, _player)) continue;
                if (World.IsStructure(e.Kind))
                    output.Add(new Command(w.Tick, _player, CommandType.SellStructure, i, Fix64.Zero, Fix64.Zero));
                else if (e.Kind is EntityKind.Unit or EntityKind.Harvester)
                    output.Add(new Command(w.Tick, _player, CommandType.AttackMove, i,
                        w.Entities[enemyStructure].X, w.Entities[enemyStructure].Y));
            }
            return;
        }

        // --- ADR-021: take the free income. A neutral Outpost pays whoever
        // walks an engineer into it, and until now only a human ever did, so
        // on any map carrying one the AI simply conceded the economy.
        //
        // ENTIRELY INERT WITHOUT AN OUTPOST. NearestNeutralOutpost returns -1
        // on a map with none, which is every golden scenario (skirmish-01 and
        // the mission maps carry none by deliberate choice, C4b), so this block
        // adds no command and changes no existing decision there. That is what
        // keeps the AI change hash-neutral.
        int outpost = NearestNeutralOutpost(w, homeX, homeY);
        if (outpost >= 0)
        {
            if (ownEngineer >= 0)
            {
                // An Attack order on a structure IS the capture order: the
                // engineer walks in and CaptureSystem consumes it on contact.
                // Re-issued each beat, which is harmless (the same target) and
                // self-healing if the walk is interrupted.
                output.Add(new Command(w.Tick, _player, CommandType.Attack, ownEngineer,
                    Fix64.Zero, Fix64.Zero, outpost));
            }
            else if (barracks >= 0 && w.Credits(_player) >= w.GetUnitType(EngineerType).Cost
                     && !AlreadyQueued(w, barracks, EngineerType))
            {
                // No engineer standing and none on the line: buy one. Gated on
                // the queue so this cannot spam an engineer every beat.
                output.Add(new Command(w.Tick, _player, CommandType.Produce, barracks,
                    Fix64.Zero, Fix64.Zero, EngineerType));
            }
        }

        // --- Construction via the sidebar flow (TICKET-P2-SIM-05): place
        // whatever the yard has finished; otherwise queue the next need. ---
        // ADR-009 clause 7 and doc 23 s4.3's ladder order. The barracks rung
        // sits between the refinery and the factory: 500 credits of infantry
        // production is the cheap early opening its price was signed for, and
        // - load-bearing under the produced_at gate - rifles and rockets are
        // the ONLY units both faction branches produce until a wave's worth of
        // army stands, so an AI that reaches the factory first would have
        // nothing it could legally build there.
        // P7-5 (DR-02): its OWN side's plant. hasPlant above already asked the
        // right question - it keys on EntityKind.PowerPlant, a property - so
        // only the two sites that name a type id needed teaching. A Directorate
        // commander picks type 1 and its behaviour is unchanged to the byte,
        // which is why the goldens do not move.
        int plant = World.PlantTypeForFaction(w.FactionOf(_player));
        // P7-5d: the other two buildings whose type the ladder used to name as a
        // literal, asked of the catalogue instead. Both return 0 for a side with
        // no answer, and every rung that uses one checks for 0 first, so a
        // commander can never queue a building it will be refused - which stalls
        // the yard forever rather than failing loudly.
        int superStruct = w.BuildableStructOfKind(_player, EntityKind.Superweapon);
        int detectorStruct = w.BuildableDetectorStruct(_player);
        // P8-17 (ADR-072): once air has been seen, the anti-air unit my side
        // can build (0 for none, or a cap of 0), and the building that unlocks
        // it if I lack one. Both asked of the catalogue: the unit is "whatever
        // of mine shoots aircraft" and the tier is "whatever it waits behind",
        // which today are the Flak Track and the Radar Uplink for both sides.
        int antiAirType = _airSeen > 0 && _antiAirCap > 0 ? AntiAirUnitType(w) : 0;
        int airTier = antiAirType != 0 ? MissingTierStruct(w, antiAirType) : 0;
        // The target: one more than the most flyers seen at once, capped.
        int antiAirTarget = _airSeen + 1 < _antiAirCap ? _airSeen + 1 : _antiAirCap;
        if (antiAirType != 0 && antiAir >= antiAirTarget) _antiAirStood = true;
        // WHEN to order, which memory alone gets wrong in both directions.
        // The first build-up runs from memory until the answer STANDS, because
        // a commander may glimpse the flyers once and not again until they are
        // over its harvesters, and every beat of waiting is a harvester lost.
        // After that, losses are replaced only while an aircraft is in sight.
        // MEASURED both ways: keyed on memory alone, a Normal Sodality
        // commander whose flak had died to ground fire and an orbital strike
        // spent 2200 credits rebuilding it against an empty sky while its
        // harvester line starved; keyed on sight alone, a Normal Directorate
        // commander placed its first order 195 ticks later (t=4845 against
        // t=4650) and lost all three harvesters before the last flyer fell.
        bool orderAntiAir = antiAirType != 0 && (!_antiAirStood || airSeenNow > 0);
        int wanted = !hasPlant ? plant
                   : refinery < 0 ? 3
                   : barracks < 0 ? 11
                   : factory < 0 ? 2
                   // P8-17: the anti-air tier, PULLED FORWARD once enemy air
                   // has been seen. Ahead of the harvester rung below on
                   // purpose: under air, a harvester bought before the answer
                   // exists is 1400 credits handed to the flyers (AI-01 lost
                   // five to nine of them that way). The ordinary radar rung
                   // further down waits behind defences and 1500 credits; this
                   // one waits only for the price. Inert until air is seen.
                   : airTier != 0 ? airTier
                   // ECONOMY BEFORE EVERYTHING ELSE, and this rung is not
                   // decoration: the barracks above costs 500 credits the
                   // opening did not previously spend, and MEASURED on
                   // mission-01 that was enough to deadlock the commander
                   // permanently. With the barracks inserted it bought plant,
                   // refinery, barracks, factory, a plant top-up and a turret,
                   // arrived at 16 credits with ZERO harvesters, and could
                   // never afford the 1400-credit harvester that pays for all
                   // of it: no income, no army, no wave, forever. The yard
                   // ladder outbids the harvester because it queues cheaper
                   // items first, so the rule is stated rather than left to
                   // budget luck - build nothing more until something is
                   // mining. It also covers the mid-game case honestly: lose
                   // every harvester and the commander rebuys the economy
                   // before it rebuys anything else.
                   : harvesters == 0 ? 0
                   : supply < draw + 40 ? plant
                   // P7-7a: TWO refineries per base, not one. GDD s4 states the
                   // designed equilibrium outright - "a player FLOATS AT 2
                   // REFINERIES / 3 HARVESTERS on one base" - and TICKET-AI-03's
                   // one-per-base rule has held the commander at half of it
                   // since the ladder existed. ADR-041 measured the consequence
                   // and drew the right conclusion from it: the economy is
                   // UNDERSIZED, not overflowing, which is why a credit ceiling
                   // was refused. This is the row that refusal pointed at.
                   : refineryCount < cyCount * RefineriesPerBase ? 3
                   : !hasTurret ? 5
                   // P7-18: and then its OWN side's defence, which no commander
                   // has ever built. Both sides put up one common turret and
                   // stopped, so a Sodality base and a Directorate base were
                   // defensively identical - the Bastion and the Shroud Nest
                   // existed in the catalogue and in the sidebar and were dead
                   // hardware, exactly as the superweapon and the Watch Post
                   // were before P7-5d asked the same question of them.
                   //
                   // This is deliberately the LAST word on defence rather than a
                   // replacement for the turret above. The common turret is
                   // anti-armour and cheap; a side's own defence is neither of
                   // those things for either faction, and swapping them would be
                   // a balance change to the opening dressed as a fix.
                   //
                   // Gated on the prerequisites because the rung above it is
                   // not: the Directorate's Bastion waits behind the radar, and
                   // queueing a building the yard will refuse stalls it forever
                   // rather than failing loudly. So the rung self-sequences -
                   // the Sodality builds its Shroud Nest as soon as it has a
                   // plant, the Directorate its Bastion once the radar is up.
                   : factionDefence != 0 && !hasFactionDefence
                     && w.Credits(_player) >= w.GetStructureType(factionDefence).Cost + 1500
                     && w.HasPrereqs(_player, w.GetStructureType(factionDefence).Prereqs) ? factionDefence
                   // P7-5d: EYES. The Directorate's unit cycle below has built a
                   // Sentinel Scout every sixth unit since TICKET-P3-FAC-04,
                   // described there as "eyes for the wall" - so that commander
                   // has always had an answer to cloak and the Sodality has
                   // never had one, which mirrored the sim's own hole exactly
                   // until ADR-043 closed it.
                   //
                   // For the Sodality the answer is a BUILDING, so it belongs
                   // here rather than in the unit cycle, and the rung is written
                   // as a capability: a side with no detector building gets 0
                   // from the query and skips the rung entirely. That is what
                   // keeps this hash-neutral for the Directorate, which is every
                   // commander in every golden.
                   : !hasDetector && detectorStruct != 0 ? detectorStruct
                   // ADR-008 clause 4: the radar before the superweapon, at
                   // BD-09's affordability threshold. Without this rung the AI
                   // never lights its own minimap surrogate today, and the day
                   // ADR-009's prerequisites land it queues a superweapon it
                   // can never build and stalls forever.
                   : !hasRadar && w.Credits(_player) >= 1500 ? 12
                   // P7-5d: ITS OWN SIDE'S superweapon. This named type 6 as a
                   // literal, and P7-5c made type 6 Directorate-only - so a
                   // Sodality commander asked for a building it is refused and
                   // simply never got a superweapon at all. The comment above
                   // predicted this exact failure for prerequisites and it
                   // arrived through faction instead.
                   //
                   // P8-18 (ADR-073, D1): and only once the commander holds
                   // TWO REFINERIES (GDD s4's floating economy, the count the
                   // refinery rung above already builds to) and AN ARMY OF AT
                   // LEAST ONE WAVE. Behind the radar and 4500 credits alone,
                   // every measured commander bought the weapon at about
                   // t=3000, before it had fought anyone, so its first strike
                   // landed before first contact. A weapon bought from a
                   // standing economy and a standing army is a climax rather
                   // than an opening. The prerequisite stays the radar.
                   //
                   // P8-18 (ADR-073, D33): AND NOT BEFORE ONE FULL CHARGE HAS
                   // ELAPSED, asked of the weapon's own def. MEASURED: with the
                   // economy and army gate alone every commander met it by about
                   // t=3170, so the median first launch was t=8701, short of
                   // F8's 10800. The floor is derived from the charge itself,
                   // which makes the first launch at least twice the charge plus
                   // the build (11536 measured). It times the COMMANDER's
                   // purchase only: a human may build the weapon whenever the
                   // radar stands and is bounded by the charge alone.
                   : !hasSuper && superStruct != 0 && w.Credits(_player) >= 4500
                     && refineryCount >= RefineriesPerBase && army >= _waveSize
                     && w.Tick >= w.GetStructureType(superStruct).ChargeTicks ? superStruct
                   : 0;
        int ready = w.Entities[cy].ReadyStructure;
        if (ready != 0)
        {
            if (TryFindPlacement(w, ready, out int ax, out int ay))
                output.Add(new Command(w.Tick, _player, CommandType.PlaceStructure, cy,
                    Fix64.FromInt(ax), Fix64.FromInt(ay), ready));
        }
        else if (wanted != 0 && w.QueueLength(cy) == 0
                 && w.Credits(_player) >= w.GetStructureType(wanted).Cost)
        {
            output.Add(new Command(w.Tick, _player, CommandType.BuildStructure, cy,
                Fix64.Zero, Fix64.Zero, wanted));
        }

        // --- DR-13: keep the base standing. A structure the AI could mend but
        // never does is the endgame's most-noticed absent behaviour (doc 27
        // tier 4): the command exists and, past the opening, the credits do
        // too. The Repair command TOGGLES, and once flipped on the sim runs it
        // to completion unaided (2 hp / 1 credit per tick, switching off at
        // full health, stalling while broke), so the commander's whole job is
        // to flip it ON ONCE per damage episode. The !Repairing guard is what
        // stops this re-toggling a live repair back OFF the next beat, and the
        // outer credit floor keeps it from issuing a mend it cannot pay a
        // single tick of. Like the outpost and fire-sale blocks, this adds no
        // command wherever no own structure is hurt; that it holds on the
        // goldens is a MEASURED claim, proven by the byte-compare, not assumed.
        if (w.Credits(_player) >= World.RepairCreditsPerTick)
        {
            for (int i = 0; i < w.Entities.Count; i++)
            {
                var s = w.Entities[i];
                if (!s.Alive || !World.IsOwnedBy(in s, _player) || !World.IsStructure(s.Kind)) continue;
                if (s.Hp < s.MaxHp && !s.Repairing)
                    output.Add(new Command(w.Tick, _player, CommandType.Repair, i, Fix64.Zero, Fix64.Zero));
            }
        }

        // --- Expansion (TICKET-AI-03): when no meaningful deposit remains
        // NEAR home (nearest-field is the wrong test - once home is dry the
        // nearest field IS the rich distant one), stop army spending, save
        // for an MCV, drive it to the richest distant field, deploy. ---
        bool homeThin = true;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var f = w.Entities[i];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount < 2000) continue;
            if (Fix64.DistSq(f.X - homeX, f.Y - homeY) <= Fix64.FromInt(400)) { homeThin = false; break; }
        }
        // ADR-009 clause 7's subtlest failure: a commander that saves 3500
        // credits for an MCV it cannot buy saves forever and never expands.
        //
        // This used to be a hand-kept COPY of the MCV's prerequisite - it read
        // `factory >= 0` because com_mcv.yaml read `[com_factory]` - with a
        // comment binding whoever answered Q006 to move both in the same change.
        // P7-16 answered Q006 and did something better than moving it: the gate
        // now ASKS THE DATA. Whatever the MCV waits on, this waits on the same
        // thing, and the two cannot drift apart again because there is no longer
        // a second copy to fall behind.
        //
        // The same correction P7 has made about fifteen times: read the rule as
        // the property it means rather than the instance it names.
        bool canBuyMcv = w.HasPrereqs(_player, w.GetUnitType(World.McvUnitType).Prereqs);
        bool expansionDesired = homeThin && cyCount < 2 && ownMcv < 0 && factory >= 0 && canBuyMcv;
        if (ownMcv >= 0)
        {
            var mcv = w.Entities[ownMcv];
            int site = RichestDistantField(w, homeX, homeY);
            if (site >= 0)
            {
                var f = w.Entities[site];
                // Tolerance matches movement's own arrival slack (crowd and
                // stall arrival can stop the vehicle several cells short).
                if (Fix64.DistSq(mcv.X - f.X, mcv.Y - f.Y) <= Fix64.FromInt(144))
                    output.Add(new Command(w.Tick, _player, CommandType.Deploy, ownMcv, Fix64.Zero, Fix64.Zero));
                else if (!mcv.Moving)
                    output.Add(new Command(w.Tick, _player, CommandType.PathMove, ownMcv, f.X - Fix64.FromInt(5), f.Y));
            }
        }
        // --- Economy: one harvester per refinery, kept working; the MCV
        // purchase fires the moment the war chest covers it. Both are FACTORY
        // units (produced_at com_factory), so these two guards already read
        // the right producer's queue and need no generalisation; the army
        // block below is the one that routes. ---
        // DR-14: the harvester target is the ladder's economy knob. At Normal
        // the multiplier is 1, so this reads exactly as it always did (one
        // harvester per refinery); Hard and Brutal run a second per refinery,
        // which is the honest way to be stronger - more mining, not free money.
        // --- P8-17 (ADR-072): ANTI-AIR INTO THE CYCLE. Once enemy air has been
        // seen, keep one anti-air unit more than the most flyers seen at once,
        // capped by the personality's anti_air_cap, ordered when orderAntiAir
        // says (above). Units already on the line count, or the commander
        // would queue one every beat while the first was still being built.
        // While the answer is short it takes its producer's turn ahead of the
        // harvester and the army: a harvester or a tank bought under
        // unanswered air is fed to the flyers.
        int antiAirProducer = -1;
        bool antiAirShort = false, antiAirOrdered = false;
        if (orderAntiAir)
        {
            var aa = w.GetUnitType(antiAirType);
            antiAirProducer = aa.ProducedAt == World.BarracksStructType ? barracks
                            : aa.ProducedAt == World.FactoryStructType ? factory : -1;
            int have = antiAir + (antiAirProducer >= 0 ? QueuedCount(w, antiAirProducer, antiAirType) : 0);
            antiAirShort = have < antiAirTarget && antiAirProducer >= 0 && w.HasPrereqs(_player, aa.Prereqs);
            if (antiAirShort && w.Credits(_player) >= aa.Cost)
            {
                // THE ANSWER CLEARS ITS LINE. Anything else on the producer's
                // queue is cancelled first, back to front so each index is
                // still the one meant (commands apply in list order), and the
                // head's refund is exact under pay-as-you-build, so the only
                // cost is build progress. MEASURED: a Normal Sodality commander
                // saw the flyers at t=4522 and ordered flak at t=4545, behind a
                // phantom tank that had just started; 210 ticks of phantom
                // stood between it and its first gun, and all three harvesters
                // died before the last flyer fell.
                var line = w.QueueContents(antiAirProducer);
                for (int q = line.Count - 1; q >= 0; q--)
                    if (line[q] != antiAirType)
                        output.Add(new Command(w.Tick, _player, CommandType.CancelProduce, antiAirProducer,
                                               Fix64.Zero, Fix64.Zero, q));
                output.Add(new Command(w.Tick, _player, CommandType.Produce, antiAirProducer,
                                       Fix64.Zero, Fix64.Zero, antiAirType));
                antiAirOrdered = true;
            }
        }
        if (antiAirOrdered)
        {
            // The factory's turn this beat is spent on the answer: the
            // harvester and MCV guards below read the queue as it stood before
            // this beat's orders, so without this they would stack behind it.
        }
        else if (factory >= 0 && refinery >= 0 && harvesters < refineryCount * _harvestersPerRefinery
            && w.QueueLength(factory) == 0)
            output.Add(new Command(w.Tick, _player, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 4));
        else if (expansionDesired && w.Credits(_player) >= 3500 && w.QueueLength(factory) == 0)
            output.Add(new Command(w.Tick, _player, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 7));

        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive || !World.IsOwnedBy(in e, _player) || e.Kind != EntityKind.Harvester) continue;
            if (e.HState != HarvestState.Idle) continue;
            int field = NearestField(w, e.X, e.Y);
            if (field >= 0)
                output.Add(new Command(w.Tick, _player, CommandType.Harvest, i, Fix64.Zero, Fix64.Zero, field));
        }

        // --- Production: alternate the counter pair once the economy stands;
        // stand aside (and save) while an expansion is desired or in transit,
        // or while the yard still wants a structure it cannot yet afford -
        // infrastructure before army, always ---
        if (factory >= 0 && harvesters >= 1 && !expansionDesired && ownMcv < 0
            && wanted == 0)
        {
            // Faction doctrine (TICKET-P3-FAC-04). Directorate: rifles and
            // cannons with a sentinel every fourth unit - eyes for the wall.
            // Sodality: rifles and phantoms - the war you cannot see coming.
            // Directorate cycle: rifle, cannon, rifle, BULWARK, cannon,
            // scout - the wall that walks soaks the war chest into combat
            // power and crushes rifle screens under its treads.
            // Opening book (one principle, both factions): hold the line
            // with cheap units until a wave's worth of army stands - nobody
            // opens with their Mammoth. Specials enter once the line exists.
            int unitType;
            bool lineHolds = army >= _waveSize;
            if (w.FactionOf(_player) == World.FactionSodality)
                unitType = !lineHolds
                    ? ((_produced % 2 == 0) ? 2 : 3)
                    : (_produced % 3) switch { 0 => 2, 1 => 9, _ => 3 };
            else
                unitType = !lineHolds
                    ? ((_produced % 2 == 0) ? 2 : 3)
                    : (_produced % 6) switch
                    {
                        0 => 2,
                        1 => 1,
                        2 => 2,
                        3 => 10,
                        4 => 8, // howitzer: splash shells are the answer to squad blobs
                        _ => scouts < 2 ? 6 : 2,
                    };
            // ADR-009 clause 7: route by the chosen type's OWN produced_at,
            // here at the command site rather than in the selection switch
            // above - the switch expresses doctrine, this expresses which
            // building can legally take the order. Infantry go to the
            // barracks, vehicles to the factory.
            int producer = w.GetUnitType(unitType).ProducedAt == World.BarracksStructType ? barracks : factory;
            // The queue-depth guard is PER PRODUCER, and that is load-bearing:
            // routing rifles to a barracks while the guard still read the
            // factory queue would leave the barracks queue unbounded and drain
            // the treasury pay-as-you-build, which is the runaway ADR-009
            // clause 7 names. A producer that does not stand yet takes no
            // order at all.
            // P8-17: and a producer whose anti-air answer is short builds
            // that first (above), so the army waits its turn there. Inert
            // until air is seen, when antiAirShort is false everywhere.
            if (producer >= 0 && w.QueueLength(producer) < 2
                && !(antiAirShort && producer == antiAirProducer)
                && w.Credits(_player) >= w.GetUnitType(unitType).Cost)
            {
                output.Add(new Command(w.Tick, _player, CommandType.Produce, producer, Fix64.Zero, Fix64.Zero, unitType));
                _produced++;
            }
        }

        // --- Superweapon doctrine (TICKET-AI-04): a charged weapon fires at
        // the enemy economy - the refinery first, any production structure
        // as fallback. No hesitation, no saving it for a rainy day. ---
        if (readySuper >= 0)
        {
            // P7-5e: WHAT the weapon is decides WHERE it goes, and the question
            // is asked of the def rather than of the faction. A commander whose
            // superweapon destroys resource fields is holding an economic-denial
            // weapon, and aiming it at a building throws away the only thing it
            // does that the other side's cannot.
            int strike = -1;
            if (w.GetStructureType(w.Entities[readySuper].StructType).DestroysFields)
                strike = BestFieldDenialTarget(w);
            // The refinery aim is the fallback as well as the ordinary case, so
            // a denial weapon with no field worth hitting still fires rather
            // than banking a charged superweapon forever.
            if (strike < 0) strike = enemyRefinery >= 0 ? enemyRefinery : enemyStructure;
            if (strike >= 0)
            {
                var st = w.Entities[strike];
                output.Add(new Command(w.Tick, _player, CommandType.LaunchSuper, readySuper, st.X, st.Y));
            }
        }

        // --- Defence squads (TICKET-AI-05): the first waveSize/2 fighters
        // (lowest ids - deterministic) are the GARRISON. Only they answer
        // threats, so harassers can no longer puppet the field army; and
        // because chasing is now safe, harvesters under attack anywhere get
        // protection again (the counter the shadow war was missing).
        int garrisonSize = _waveSize / 2 < 2 ? 2 : _waveSize / 2;
        int[] garrison = new int[8];
        int garrisonCount = 0;
        for (int i = 0; i < w.Entities.Count && garrisonCount < garrisonSize && garrisonCount < 8; i++)
        {
            var e = w.Entities[i];
            if (e.Alive && World.IsOwnedBy(in e, _player) && e.Kind == EntityKind.Unit
                && e.UnitType != 6 && e.UnitType != World.McvUnitType && e.UnitType != EngineerType
                && !(w.FactionOf(_player) == World.FactionSodality && e.UnitType == 9)
                && !IsAntiAir(w, in e))   // P8-17: anti-air keeps its own garrison, below
                garrison[garrisonCount++] = i;
        }
        bool InGarrison(int id)
        {
            for (int g = 0; g < garrisonCount; g++) if (garrison[g] == id) return true;
            return false;
        }

        int intruder = -1;
        Fix64 intruderD = Fix64.MaxValue;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var hostile = w.Entities[i];
            if (!hostile.Alive || !w.IsEnemyOf(in hostile, _player)) continue;   // P7-8g: hostility
            if (hostile.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            for (int j = 0; j < w.Entities.Count; j++)
            {
                var own = w.Entities[j];
                // P7-8i: MY TEAM'S, not merely mine. This is the whole of what
                // "the AI knows it has allies" means here, and it is one
                // predicate because the P7-8g refactor had already separated
                // ownership from hostility - the question this site asks is
                // "whose ground is being walked on", and the answer widened
                // from a seat to a side.
                //
                // With the default team map every seat is its own team, so
                // IsAlliedTo reduces to PlayerId == _player and this is
                // byte-identical to what shipped. That is the mechanism for
                // hash neutrality rather than a resemblance to it.
                if (!own.Alive || !w.IsAlliedTo(in own, _player)) continue;
                bool isEconomy = own.Kind == EntityKind.Harvester;
                if (!isEconomy && own.Kind is not (EntityKind.ConstructionYard or EntityKind.PowerPlant
                    or EntityKind.Refinery or EntityKind.Factory or EntityKind.Turret)) continue;
                Fix64 guard = isEconomy ? Fix64.FromInt(EconomyGuardSq) : Fix64.FromInt(BaseGuardSq);
                Fix64 d = Fix64.DistSq(hostile.X - own.X, hostile.Y - own.Y);
                if (d <= guard && d < intruderD) { intruderD = d; intruder = i; }
            }
        }
        if (intruder >= 0 && garrisonCount > 0 && w.Tick - _lastDefendTick >= DefendCadenceTicks)
        {
            var threat = w.Entities[intruder];
            for (int g = 0; g < garrisonCount; g++)
                output.Add(new Command(w.Tick, _player, CommandType.AttackMove, garrison[g], threat.X, threat.Y));
            _lastDefendTick = w.Tick;
            // The field army carries on: defence no longer preempts offence.
        }
        else if (intruder < 0)
        {
            // Quiet watch: idle garrison drifts back to the yard.
            for (int g = 0; g < garrisonCount; g++)
            {
                var e = w.Entities[garrison[g]];
                if (!e.Moving && e.ExplicitTarget < 0
                    && Fix64.DistSq(e.X - homeX, e.Y - homeY) > Fix64.FromInt(HomeLeashSq))
                    output.Add(new Command(w.Tick, _player, CommandType.PathMove, garrison[g], homeX, homeY));
            }
        }

        // --- P8-17 (ADR-072): THE ANTI-AIR GARRISON AND THE HARVESTER ESCORT.
        // The first anti_air_garrison anti-air units (lowest ids, so the split
        // is deterministic and stable as units die) hold the base: they close
        // on any flyer seen within the structure guard radius the intruder
        // census uses, and drift home when the sky is quiet. Every anti-air
        // unit beyond them is an ESCORT: it shadows a harvester (the k-th
        // escort the k-th harvester, round robin in id order), and when a
        // flyer is seen within the economy guard radius of any harvester, all
        // escorts close on it. With no harvester alive an escort guards the
        // refinery, which is where the next one docks. Entirely inert while
        // the commander owns no anti-air unit, which it never builds until it
        // has seen air.
        if (antiAir > 0) AnswerAir(w, output, homeX, homeY);

        // --- Waves: a full wave stands ready, at most one order per 300 ticks ---
        // --- Directorate escort doctrine (TICKET-P3-FAC-07): sentinels
        // shadow the harvesters. Detection travels with the economy, so the
        // shadow war happens in the light. ---
        if (w.FactionOf(_player) == World.FactionDirectorate)
        {
            for (int i = 0; i < w.Entities.Count; i++)
            {
                var sc = w.Entities[i];
                if (!sc.Alive || !World.IsOwnedBy(in sc, _player) || sc.UnitType != 6) continue;
                if (sc.Moving) continue;
                int ward = -1; Fix64 wardD = Fix64.MaxValue;
                for (int j = 0; j < w.Entities.Count; j++)
                {
                    var h = w.Entities[j];
                    if (!h.Alive || !World.IsOwnedBy(in h, _player) || h.Kind != EntityKind.Harvester) continue;
                    Fix64 d = Fix64.DistSq(h.X - sc.X, h.Y - sc.Y);
                    if (d < wardD) { wardD = d; ward = j; }
                }
                if (ward >= 0 && wardD > Fix64.FromInt(EscortLeashSq))
                {
                    var h = w.Entities[ward];
                    output.Add(new Command(w.Tick, _player, CommandType.PathMove, i, h.X, h.Y));
                }
            }
        }

        // --- Sodality shadow war (TICKET-P3-FAC-06): phantoms are not line
        // units. They cross the map cloaked and strangle the enemy economy -
        // idle phantoms take standing orders against the nearest enemy
        // harvester. The Directorate wins stand-up fights; the Sodality
        // makes sure there is no one left to stand up against.
        if (w.FactionOf(_player) == World.FactionSodality)
        {
            for (int i = 0; i < w.Entities.Count; i++)
            {
                var ph = w.Entities[i];
                if (!ph.Alive || !World.IsOwnedBy(in ph, _player) || ph.UnitType != 9) continue;
                if (ph.ExplicitTarget >= 0 || ph.Moving) continue;
                int prey = -1; Fix64 preyD = Fix64.MaxValue;
                for (int j = 0; j < w.Entities.Count; j++)
                {
                    var h = w.Entities[j];
                    // P7-8g: hostility. The phantom picks PREY, so this is the
                    // question that changes under teams, unlike the ownership
                    // test that found the phantom itself two lines up.
                    if (!h.Alive || !w.IsEnemyOf(in h, _player) || h.Kind != EntityKind.Harvester) continue;
                    Fix64 d = Fix64.DistSq(h.X - ph.X, h.Y - ph.Y);
                    if (d < preyD) { preyD = d; prey = j; }
                }
                if (prey >= 0)
                    output.Add(new Command(w.Tick, _player, CommandType.Attack, i, Fix64.Zero, Fix64.Zero, prey));
            }
        }

        if (army >= _waveSize + garrisonCount && enemyStructure >= 0 && w.Tick - _lastWaveTick >= 300)
        {
            // Strike the economy first: a dead refinery ends wars that a
            // dead turret only delays. Fall back to the nearest structure.
            var target = w.Entities[enemyRefinery >= 0 ? enemyRefinery : enemyStructure];
            for (int i = 0; i < w.Entities.Count; i++)
            {
                var e = w.Entities[i];
                // MCVs found bases and sentinels are eyes, not spears: the
                // wave conscripts fighting units only.
                if (e.Alive && World.IsOwnedBy(in e, _player) && e.Kind == EntityKind.Unit
                    && e.UnitType != World.McvUnitType && e.UnitType != 6 && e.UnitType != EngineerType
                    && !(w.FactionOf(_player) == World.FactionSodality && e.UnitType == 9)
                    && !IsAntiAir(w, in e)   // P8-17: anti-air guards home and harvesters, never a wave
                    && !InGarrison(i))
                    output.Add(new Command(w.Tick, _player, CommandType.AttackMove, i, target.X, target.Y));
            }
            _lastWaveTick = w.Tick;
        }
    }

    /// <summary>Richest live field beyond 20 cells of home; ties break to lower id.</summary>
    /// <summary>
    /// P7-5e: where a FIELD-DESTROYING superweapon should land, which is a
    /// different question from where a damaging one should.
    ///
    /// The existing aim reuses the wave scan: hit the nearest enemy refinery.
    /// For the seismic charge that throws away the only thing it does that the
    /// orbital cannon cannot, so this scan asks the weapon's own question -
    /// which patch of ground, denied, costs the enemy the most ferrite.
    ///
    /// Three rules, and each is a decision rather than an obvious step:
    ///
    /// 1. SCORE BY CLUSTER, not by the single richest field. The blast kills
    ///    every field within 6 cells (World.ApplySeismicCharge), so a tight
    ///    group of three ordinary fields is worth more than one fat isolated
    ///    one. This is the fact the effect's radius makes true and a
    ///    single-field aim would ignore.
    /// 2. ONLY THEIR GROUND. A field is a candidate only if it is nearer to an
    ///    enemy structure than to my own base, so the commander cannot decide
    ///    to deny the patch its own harvesters are working. Fields are neutral
    ///    and there is no ownership to read, so proximity is the only honest
    ///    proxy for "theirs".
    /// 3. TIES BY LOWEST ENTITY INDEX, which is what keeps it deterministic.
    ///    A strictly-greater comparison over an ascending walk does that by
    ///    construction, and it is stated because it is load-bearing rather than
    ///    incidental.
    ///
    /// Returns -1 when no field qualifies, and the caller falls back to the
    /// ordinary refinery aim rather than banking a charged superweapon.
    /// </summary>
    private int BestFieldDenialTarget(World w)
    {
        // Where my own base is, and where theirs is, taken as the nearest
        // structure of each. Both are needed for rule 2.
        Fix64 myX = Fix64.Zero, myY = Fix64.Zero;
        bool haveMine = false;
        for (int i = 0; i < w.Entities.Count && !haveMine; i++)
        {
            var e = w.Entities[i];
            if (e.Alive && World.IsOwnedBy(in e, _player) && World.IsStructure(e.Kind))
            { myX = e.X; myY = e.Y; haveMine = true; }
        }
        if (!haveMine) return -1;

        Fix64 blastSq = Fix64.FromInt(36);   // 6^2, ApplySeismicCharge's outer radius
        int best = -1;
        long bestScore = 0;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var f = w.Entities[i];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;

            // Rule 2: nearer to something of theirs than to something of mine.
            Fix64 mine = Fix64.DistSq(f.X - myX, f.Y - myY);
            Fix64 theirs = Fix64.MaxValue;
            for (int j = 0; j < w.Entities.Count; j++)
            {
                var o = w.Entities[j];
                if (!o.Alive || !World.IsStructure(o.Kind) || !w.IsEnemyOf(in o, _player)) continue;
                Fix64 d = Fix64.DistSq(o.X - f.X, o.Y - f.Y);
                if (d < theirs) theirs = d;
            }
            if (theirs == Fix64.MaxValue || theirs >= mine) continue;

            // Rule 1: what the whole blast would take, not just this field.
            long score = 0;
            for (int j = 0; j < w.Entities.Count; j++)
            {
                var g = w.Entities[j];
                if (!g.Alive || g.Kind != EntityKind.FerriteField || g.FerriteAmount <= 0) continue;
                if (Fix64.DistSq(g.X - f.X, g.Y - f.Y) > blastSq) continue;
                score += g.FerriteAmount;
            }
            // Rule 3: strictly greater over an ascending walk, so the lowest
            // entity index wins a tie and the choice is reproducible.
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private static int RichestDistantField(World w, Fix64 x, Fix64 y)
    {
        int best = -1, bestAmount = 0;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var f = w.Entities[i];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;
            if (Fix64.DistSq(f.X - x, f.Y - y) <= Fix64.FromInt(400)) continue;
            if (f.FerriteAmount > bestAmount) { bestAmount = f.FerriteAmount; best = i; }
        }
        return best;
    }

    private static int NearestField(World w, Fix64 x, Fix64 y)
    {
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var f = w.Entities[i];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;
            Fix64 d = Fix64.DistSq(f.X - x, f.Y - y);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>ADR-021: the engineer's unit type. Named because the outpost
    /// logic tests it in three places and a bare 11 in each is how one of them
    /// drifts (the McvUnitType precedent on the client side). Unit type 11 is
    /// the engineer; STRUCT type 11 is the barracks, a different namespace.</summary>
    /// <summary>Aliased to the sim-wide name rather than restating 11.</summary>
    private const int EngineerType = World.EngineerUnitType;

    /// <summary>ADR-021: the nearest UNCAPTURED outpost to a point, ties to the
    /// lower entity id so the choice is stable, in the NearestField shape. An
    /// outpost owned by anyone (including this player) is not a target: capture
    /// only ever flips a neutral one, and re-taking an enemy's is a job for the
    /// army, not a lone engineer. Returns -1 on a map with no outposts, which
    /// is what makes the whole outpost block inert in every golden scenario.</summary>
    private static int NearestNeutralOutpost(World w, Fix64 x, Fix64 y)
    {
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var o = w.Entities[i];
            if (!o.Alive || o.Kind != EntityKind.Outpost || o.PlayerId >= 0) continue;
            Fix64 d = Fix64.DistSq(o.X - x, o.Y - y);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Is this unit type already on a producer's line? Keeps the
    /// outpost logic from queueing an engineer every decision beat.</summary>
    private static bool AlreadyQueued(World w, int producer, int unitType)
    {
        var q = w.QueueContents(producer);
        for (int i = 0; i < q.Count; i++) if (q[i] == unitType) return true;
        return false;
    }

    /// <summary>P8-17: how many of a unit type are on a producer's line, so the
    /// anti-air target counts units being built as well as units standing.</summary>
    private static int QueuedCount(World w, int producer, int unitType)
    {
        var q = w.QueueContents(producer);
        int n = 0;
        for (int i = 0; i < q.Count; i++) if (q[i] == unitType) n++;
        return n;
    }

    /// <summary>P8-17 (ADR-072): is this a unit that can shoot aircraft? Asked
    /// of its WEAPON, the ADR-028 clause 3 flag, so it means the capability
    /// rather than naming the Flak Track. Never true of anything the commander
    /// owned before P8-17, which never built anti-air.</summary>
    private static bool IsAntiAir(World w, in Entity e)
        => e.Kind == EntityKind.Unit && e.WeaponId != 0 && w.GetWeaponType(e.WeaponId).AntiAir;

    /// <summary>
    /// P8-17 (ADR-072): the census rule, stated once. An enemy aircraft counts
    /// only if this commander can actually see it, and that means two things:
    ///   1. its cell is lit in MY fog this tick (World.IsVisible, the bitset the
    ///      fog pass writes for each player from that player's own sight and
    ///      scans), so the commander never reads through fog;
    ///   2. it is not an undetected cloaked unit, the same rule the sim's own
    ///      targeting applies (World.CanTarget), which is D9's spirit. No flyer
    ///      in the catalogue cloaks today, so this half costs nothing and is
    ///      written so a cloaked one is not counted the day it exists.
    /// The caller has already asked hostility; this asks only "a plane I see".
    /// </summary>
    private bool SeesAircraft(World w, in Entity e)
    {
        if (!w.IsAirborne(in e)) return false;
        int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y);
        if (!w.Map.InBounds(cx, cy) || !w.IsVisible(_player, cx, cy)) return false;
        bool cloaked = e.Stealth || e.FieldCloaked;
        return !cloaked || e.RevealTicks > 0 || (e.DetectedMask & (1 << _player)) != 0;
    }

    /// <summary>
    /// P8-17 (ADR-072): the anti-air unit this commander's side can build, or 0
    /// for none. Asked of the catalogue rather than naming the Flak Track, in
    /// the shape of World.BuildableDetectorStruct: the lowest unit type id, in
    /// the ascending order UnitTypeIds returns, that is producible (a price),
    /// on the ground (D10: the AI does not fly), the commander's side or
    /// common, and armed with an anti-air weapon. The Flak Track is common, so
    /// it is the answer for both the Directorate and the Sodality today.
    /// </summary>
    private int AntiAirUnitType(World w)
    {
        int faction = w.FactionOf(_player);
        var ids = w.UnitTypeIds();
        for (int k = 0; k < ids.Count; k++)
        {
            var d = w.GetUnitType(ids[k]);
            if (d.Cost <= 0 || d.Air || d.WeaponId == 0) continue;
            if (d.Faction != World.FactionCommon && d.Faction != faction) continue;
            if (w.GetWeaponType(d.WeaponId).AntiAir) return ids[k];
        }
        return 0;
    }

    /// <summary>
    /// P8-17 (ADR-072): the building a unit waits behind that this commander
    /// lacks and can build now, or 0. "The anti-air tier" read as a property:
    /// for each prerequisite the unit names, in authored order, a missing KIND
    /// (World.HasPrereqs' own reading, ADR-009 as amended by P7-5) is answered
    /// with the buildable structure of that kind for this side, provided its
    /// own prerequisites stand, because queueing a building the yard will
    /// refuse stalls the yard forever. For the Flak Track that is the Radar
    /// Uplink, behind the factory the ladder has already built.
    /// </summary>
    private int MissingTierStruct(World w, int unitType)
    {
        var prereqs = w.GetUnitType(unitType).Prereqs;
        if (prereqs == null) return 0;
        for (int r = 0; r < prereqs.Length; r++)
        {
            EntityKind need = w.GetStructureType(prereqs[r]).Kind;
            bool owned = false;
            for (int i = 0; i < w.Entities.Count && !owned; i++)
            {
                var o = w.Entities[i];
                owned = o.Alive && World.IsOwnedBy(in o, _player) && World.IsStructure(o.Kind) && o.Kind == need;
            }
            if (owned) continue;
            int build = w.BuildableStructOfKind(_player, need);
            if (build != 0 && w.HasPrereqs(_player, w.GetStructureType(build).Prereqs)) return build;
        }
        return 0;
    }

    /// <summary>
    /// P8-17 (ADR-072): orders for the anti-air units, garrison first and
    /// escorts after; see the call site for the doctrine. Threats are the seen
    /// flyers (SeesAircraft) nearest to what they threaten, ties to the lower
    /// id: within BaseGuardSq of a structure on my side, and within
    /// EconomyGuardSq of a harvester on my side, the intruder census's own
    /// radii and its own IsAlliedTo reading of "my side". Orders go out at most
    /// once per DefendCadenceTicks, the ground garrison's own cadence; the
    /// shadowing and the drift home are idle-only, so they never fight a live
    /// order. Reads the world in entity index order and keeps one tick of
    /// state, the cadence stamp.
    /// </summary>
    private void AnswerAir(World w, List<Command> output, Fix64 homeX, Fix64 homeY)
    {
        int baseFlyer = -1, raidFlyer = -1;
        Fix64 baseD = Fix64.MaxValue, raidD = Fix64.MaxValue;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var f = w.Entities[i];
            if (!f.Alive || !w.IsEnemyOf(in f, _player) || !SeesAircraft(w, in f)) continue;
            for (int j = 0; j < w.Entities.Count; j++)
            {
                var own = w.Entities[j];
                if (!own.Alive || !w.IsAlliedTo(in own, _player)) continue;
                Fix64 d = Fix64.DistSq(f.X - own.X, f.Y - own.Y);
                if (own.Kind == EntityKind.Harvester)
                {
                    if (d <= Fix64.FromInt(EconomyGuardSq) && d < raidD) { raidD = d; raidFlyer = i; }
                }
                else if (World.IsStructure(own.Kind) && d <= Fix64.FromInt(BaseGuardSq) && d < baseD)
                {
                    baseD = d; baseFlyer = i;
                }
            }
        }

        // My own harvesters in id order, the escorts' wards, and my lowest-id
        // refinery for when none is left.
        var wards = new List<int>();
        int refinery = -1;
        for (int j = 0; j < w.Entities.Count; j++)
        {
            var own = w.Entities[j];
            if (!own.Alive || !World.IsOwnedBy(in own, _player)) continue;
            if (own.Kind == EntityKind.Harvester) wards.Add(j);
            else if (own.Kind == EntityKind.Refinery && refinery < 0) refinery = j;
        }

        bool answer = w.Tick - _lastAirDefendTick >= DefendCadenceTicks;
        bool ordered = false;
        int n = 0;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive || !World.IsOwnedBy(in e, _player) || !IsAntiAir(w, in e)) continue;
            int rank = n++;
            if (rank < _antiAirGarrison)
            {
                // GARRISON: hold the base.
                if (baseFlyer >= 0)
                {
                    if (answer)
                    {
                        var t = w.Entities[baseFlyer];
                        output.Add(new Command(w.Tick, _player, CommandType.AttackMove, i, t.X, t.Y));
                        ordered = true;
                    }
                }
                else if (!e.Moving && e.ExplicitTarget < 0
                         && Fix64.DistSq(e.X - homeX, e.Y - homeY) > Fix64.FromInt(HomeLeashSq))
                    output.Add(new Command(w.Tick, _player, CommandType.PathMove, i, homeX, homeY));
                continue;
            }
            // ESCORT: close on a raid, otherwise shadow a harvester.
            if (raidFlyer >= 0)
            {
                if (answer)
                {
                    var t = w.Entities[raidFlyer];
                    output.Add(new Command(w.Tick, _player, CommandType.AttackMove, i, t.X, t.Y));
                    ordered = true;
                }
                continue;
            }
            if (e.Moving || e.ExplicitTarget >= 0) continue;
            int ward = wards.Count > 0 ? wards[(rank - _antiAirGarrison) % wards.Count] : refinery;
            if (ward < 0) continue;
            var h = w.Entities[ward];
            if (Fix64.DistSq(h.X - e.X, h.Y - e.Y) > Fix64.FromInt(EscortLeashSq))
                output.Add(new Command(w.Tick, _player, CommandType.PathMove, i, h.X, h.Y));
        }
        if (ordered) _lastAirDefendTick = w.Tick;
    }

    /// <summary>Deterministic outward ring scan around own structures for a
    /// legal anchor, OLDEST structure first (P7-8, below). ADR-071 clause 3
    /// filters the candidates through KeepsApron. P8-21 (ADR-075, decision D5)
    /// orients the scan to the MAP CENTRE: each anchor's rings are walked in one
    /// canonical frame, the side facing the centre first, reflected along every
    /// axis on which that anchor stands short of the centre, so a base in one
    /// corner lays itself out as the rotation (or, on a mirrored map, the
    /// reflection) of a base in the opposite one.</summary>
    private bool TryFindPlacement(World w, int ready, out int ax, out int ay)
    {
        bool placingRefinery = w.GetStructureType(ready).Kind == EntityKind.Refinery;
        int size = w.FootprintOf(ready);
        // The footprint ValidPlacement's default actually tests (structure
        // type 0's, or the 2x2 fallback), read rather than written as a
        // literal, so the reflected box below stays the box it checks.
        int box = w.FootprintOf(0);
        // P7-8: OLDEST FIRST, and the direction is the whole fix.
        //
        // This walked backwards, so the anchor was the most recently built
        // structure and every new building ringed off the last one. A base
        // therefore WALKED: measured, a Sodality commander strung its twelve
        // generators from its yard to the map corner, ending 31 cells out. The
        // Directorate drifted too, to 11, and only looked acceptable because
        // 100-supply plants meant it built five where the Sodality builds
        // twelve - so DR-02 did not create this, it multiplied it.
        //
        // Forwards, the first eligible structure is the Construction Yard, so
        // buildings ring outward from the base and it stays a base. Founding a
        // SECOND base still works, because the rings around the first fill up
        // and the loop falls through to the next yard - measured, the expansion
        // scenario still migrates its economy.
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var s = w.Entities[i];
            if (!s.Alive || !World.IsOwnedBy(in s, _player)) continue;
            if (s.Kind is not (EntityKind.ConstructionYard or EntityKind.PowerPlant or EntityKind.Factory or EntityKind.Refinery)) continue;
            int oax = w.AnchorOf(s.X, s.StructType), oay = w.AnchorOf(s.Y, s.StructType);
            int anchorSize = w.FootprintOf(s.StructType);
            // P8-21 (ADR-075, D5): THE SCAN IS ORIENTED TO THE MAP CENTRE.
            //
            // It walked the same compass order from every base: rows top to
            // bottom, each left to right. A base in the top-left corner
            // therefore filled its sheltered corner side first, and the base
            // in the opposite corner, on a map that is the 180-degree rotation
            // of itself, filled its side facing the enemy first. Swapping the
            // starts relabelled the winner in 34 of 36 measured pairs (P8-13):
            // the bias was the START, not the seat.
            //
            // So the offsets below are written in ONE canonical frame and
            // reflected along each axis on which THIS anchor's centre stands
            // short of the map's. A base and its rotated twin then try
            // mirrored cells in the same order. The reflection is about the
            // anchor's own centre and carries with it the box ValidPlacement
            // checks, so the reflection itself is exact for any pair of
            // footprint sizes. The build radius is NOT: ValidPlacement measures
            // it as the Chebyshev distance between top-left anchors, which the
            // half turn preserves only when the anchor's footprint and the
            // candidate's are the same size. For a 1x1 candidate round a 2x2
            // anchor that is not a yard, and for a 2x2 box round a 1x1
            // generator, the reflected candidate can stand six cells from the
            // anchor where its unreflected twin stands five, so ValidPlacement
            // can refuse a cell whose twin it accepts (a yard's radius of 7
            // covers both). ADR-075 lists it in the residue. A centre exactly
            // on the map's centre line is not reflected, the one tie and a
            // recorded asymmetry.
            //
            // The canonical frame is the walk a base right of and below the
            // centre always had, which from there starts on the side FACING
            // the centre. Measured, not assumed: on seven of the eight two-seat
            // maps the home ferrite lies towards the centre from the yard on
            // both axes, and on skirmish-03 it is level in x (-0.6 cells) and
            // towards the centre in y (+12.4), so this frame builds towards the
            // base's own economy. The other frame, the sheltered side first,
            // was measured and refused, because it fails aiairgate: ADR-075
            // records both.
            bool flipX = 2 * oax + anchorSize < w.Map.Width;
            bool flipY = 2 * oay + anchorSize < w.Map.Height;
            for (int ring = 3; ring <= World.BuildRadius; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue; // ring shell only
                        // The box ValidPlacement reads (it is asked with the
                        // default footprint, as it always was) and the
                        // building's own anchor, each reflected whole.
                        int bx = flipX ? oax + anchorSize - box - dx : oax + dx;
                        int by = flipY ? oay + anchorSize - box - dy : oay + dy;
                        int cx = flipX ? oax + anchorSize - size - dx : oax + dx;
                        int cyy = flipY ? oay + anchorSize - size - dy : oay + dy;
                        if (!w.ValidPlacement(_player, bx, by)) continue;
                        // Reflected, a 1x1 building no longer shares the box's
                        // anchor, and the sim's own test on the PlaceStructure
                        // reads the building's anchor for the build radius, so
                        // it is asked too: a placement the sim then refuses
                        // would be retried every beat and stall the yard. Where
                        // the anchors coincide the box test already implies it.
                        if ((bx != cx || by != cyy) && !w.ValidPlacement(_player, cx, cyy, ready)) continue;
                        if (KeepsApron(w, cx, cyy, size, placingRefinery))
                        { ax = cx; ay = cyy; return true; }
                    }
        }
        ax = ay = -1;
        return false;
    }

    /// <summary>
    /// ADR-071 clause 3: the commander keeps a one-cell clear apron round every
    /// refinery it owns, so it can never seal its own dock. Two halves:
    ///   1. Nothing it places may cover a cell of an own living refinery's
    ///      apron (the one-cell ring round the footprint), whoever placed that
    ///      refinery.
    ///   2. A refinery it places must have its own apron clear: every in-bounds
    ///      cell of the ring unblocked. A ring cell off the map edge is not a
    ///      cell anything can stand in or seal, so it is not counted.
    /// A filter only: it reads the world, entity index order, and keeps no state.
    /// </summary>
    private bool KeepsApron(World w, int ax, int ay, int size, bool placingRefinery)
    {
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var r = w.Entities[i];
            if (!r.Alive || r.Kind != EntityKind.Refinery || !World.IsOwnedBy(in r, _player)) continue;
            int rs = w.FootprintOf(r.StructType), rax = w.AnchorOf(r.X, r.StructType), ray = w.AnchorOf(r.Y, r.StructType);
            // The candidate's footprint against the refinery's footprint grown by one cell on every side.
            if (ax <= rax + rs && ax + size - 1 >= rax - 1 && ay <= ray + rs && ay + size - 1 >= ray - 1) return false;
        }
        if (placingRefinery)
            for (int y = ay - 1; y <= ay + size; y++)
                for (int x = ax - 1; x <= ax + size; x++)
                {
                    bool inside = x >= ax && x < ax + size && y >= ay && y < ay + size;
                    if (!inside && w.Map.InBounds(x, y) && w.Map.IsBlocked(x, y)) return false;
                }
        return true;
    }
}
