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
        int harvesters = 0, army = 0, supply = 0, draw = 0;
        int cyCount = 0, refineryCount = 0, ownMcv = -1, scouts = 0, ownEngineer = -1;
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
        }
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
            // No yard, no MCV: beaten. Sell everything still standing and send
            // every unit in one last wave - the classic Fire Sale, the ending
            // that goes out with a bang instead of a mop-up. Once.
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
        int wanted = !hasPlant ? 1
                   : refinery < 0 ? 3
                   : barracks < 0 ? 11
                   : factory < 0 ? 2
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
                   : supply < draw + 40 ? 1
                   : refineryCount < cyCount ? 3 // one refinery per base (TICKET-AI-03)
                   : !hasTurret ? 5
                   // ADR-008 clause 4: the radar before the superweapon, at
                   // BD-09's affordability threshold. Without this rung the AI
                   // never lights its own minimap surrogate today, and the day
                   // ADR-009's prerequisites land it queues a superweapon it
                   // can never build and stalls forever.
                   : !hasRadar && w.Credits(_player) >= 1500 ? 12
                   : !hasSuper && w.Credits(_player) >= 4500 ? 6 // war chest banked: reach for the sky (TICKET-AI-04)
                   : 0;
        int ready = w.Entities[cy].ReadyStructure;
        if (ready != 0)
        {
            if (TryFindPlacement(w, out int ax, out int ay))
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
        // ADR-009 clause 7's subtlest failure, addressed by keeping this gate
        // exactly equal to the MCV's authored prerequisite. com_mcv.yaml still
        // reads `prerequisites: [com_factory]` this wave (Q006 owns moving it
        // behind the radar), so gating on the factory is gating on the data:
        // the AI never saves 3500 credits for an MCV it cannot buy. The day
        // Q006 moves that prerequisite, this line moves with it in the same
        // change, or the saving-for-nothing failure appears here first.
        bool expansionDesired = homeThin && cyCount < 2 && ownMcv < 0 && factory >= 0;
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
        if (factory >= 0 && refinery >= 0 && harvesters < refineryCount * _harvestersPerRefinery
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
            if (producer >= 0 && w.QueueLength(producer) < 2
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
            int strike = enemyRefinery >= 0 ? enemyRefinery : enemyStructure;
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
                && !(w.FactionOf(_player) == World.FactionSodality && e.UnitType == 9))
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
                if (!own.Alive || !World.IsOwnedBy(in own, _player)) continue;       // P7-8g: ownership
                bool isEconomy = own.Kind == EntityKind.Harvester;
                if (!isEconomy && own.Kind is not (EntityKind.ConstructionYard or EntityKind.PowerPlant
                    or EntityKind.Refinery or EntityKind.Factory or EntityKind.Turret)) continue;
                Fix64 guard = isEconomy ? Fix64.FromInt(64) : Fix64.FromInt(196);
                Fix64 d = Fix64.DistSq(hostile.X - own.X, hostile.Y - own.Y);
                if (d <= guard && d < intruderD) { intruderD = d; intruder = i; }
            }
        }
        if (intruder >= 0 && garrisonCount > 0 && w.Tick - _lastDefendTick >= 60)
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
                    && Fix64.DistSq(e.X - homeX, e.Y - homeY) > Fix64.FromInt(144))
                    output.Add(new Command(w.Tick, _player, CommandType.PathMove, garrison[g], homeX, homeY));
            }
        }

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
                if (ward >= 0 && wardD > Fix64.FromInt(9))
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
                    && !InGarrison(i))
                    output.Add(new Command(w.Tick, _player, CommandType.AttackMove, i, target.X, target.Y));
            }
            _lastWaveTick = w.Tick;
        }
    }

    /// <summary>Richest live field beyond 20 cells of home; ties break to lower id.</summary>
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

    /// <summary>Deterministic outward ring scan around own structures for a
    /// legal anchor, NEWEST structure first - so support buildings gravitate
    /// to the frontier (a fresh expansion CY gets its refinery, not the
    /// already-crowded home base).</summary>
    private bool TryFindPlacement(World w, out int ax, out int ay)
    {
        for (int i = w.Entities.Count - 1; i >= 0; i--)
        {
            var s = w.Entities[i];
            if (!s.Alive || !World.IsOwnedBy(in s, _player)) continue;
            if (s.Kind is not (EntityKind.ConstructionYard or EntityKind.PowerPlant or EntityKind.Factory or EntityKind.Refinery)) continue;
            int oax = w.AnchorOf(s.X, s.StructType), oay = w.AnchorOf(s.Y, s.StructType);
            for (int ring = 3; ring <= World.BuildRadius; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue; // ring shell only
                        int cx = oax + dx, cyy = oay + dy;
                        if (w.ValidPlacement(_player, cx, cyy)) { ax = cx; ay = cyy; return true; }
                    }
        }
        ax = ay = -1;
        return false;
    }
}
