namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-AI-01: a deterministic rule-based skirmish commander. It plays
/// through exactly the same command interface as a human or the network
/// layer - it holds no privileged access and mutates nothing, so an AI match
/// is as replayable and desync-safe as any other. All state it keeps is its
/// own and updates deterministically from world state.
///
/// Doctrine (deliberately simple; the point is a full closed loop, not
/// brilliance): establish power -> refinery -> factory; keep one harvester
/// working; alternate rifle/cannon production; add a defensive turret; when
/// six combat units stand ready, attack-move the wave at the nearest enemy
/// structure, and keep the waves coming.
/// </summary>
public sealed class SkirmishAI
{
    private readonly int _player;
    private readonly int _actEvery;   // decision beat; larger = slower commander (difficulty knob)
    private readonly int _waveSize;   // units per attack wave (difficulty knob)
    private int _produced;
    private int _lastWaveTick = -10_000;
    private int _lastDefendTick = -10_000;

    public SkirmishAI(int player, int actEvery = 15, int waveSize = 6)
    { _player = player; _actEvery = actEvery; _waveSize = waveSize; }

    // Personality presets (TICKET-AI-03): the knobs make the tiers.
    public static SkirmishAI Standard(int player) => new(player);
    public static SkirmishAI Rusher(int player) => new(player, actEvery: 15, waveSize: 4);
    public static SkirmishAI Turtle(int player) => new(player, actEvery: 15, waveSize: 10);

    public void Act(World w, List<Command> output)
    {
        if (w.Tick % _actEvery != 0) return;

        int cy = -1, factory = -1, refinery = -1;
        bool hasPlant = false, hasTurret = false;
        int harvesters = 0, army = 0, supply = 0, draw = 0;
        int cyCount = 0, refineryCount = 0, ownMcv = -1, scouts = 0;
        bool hasSuper = false;
        int readySuper = -1, enemyRefinery = -1;
        int enemyStructure = -1;
        Fix64 bestEnemyD = Fix64.MaxValue;
        Fix64 homeX = Fix64.Zero, homeY = Fix64.Zero;

        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive) continue;
            if (e.PlayerId == _player)
            {
                switch (e.Kind)
                {
                    case EntityKind.ConstructionYard:
                        cyCount++;
                        if (cy < 0) { cy = i; homeX = e.X; homeY = e.Y; }
                        break;
                    case EntityKind.Factory: factory = i; break;
                    case EntityKind.Refinery: refinery = i; refineryCount++; break;
                    case EntityKind.PowerPlant: hasPlant = true; break;
                    case EntityKind.Turret: hasTurret = true; break;
                    case EntityKind.Superweapon:
                        hasSuper = true;
                        if (e.ChargeTicks == 0 && e.StrikeTicks < 0) readySuper = i;
                        break;
                    case EntityKind.Harvester: harvesters++; break;
                    case EntityKind.Unit:
                        if (e.UnitType == 7) ownMcv = i;
                        else if (e.UnitType == 6) scouts++; // support, not line strength
                        else army++;
                        break;
                }
                supply += e.PowerSupply;
                draw += e.PowerDraw;
            }
            else if (e.PlayerId >= 0 && e.PlayerId != _player
                     && e.Kind is EntityKind.ConstructionYard or EntityKind.Factory or EntityKind.Refinery or EntityKind.PowerPlant)
            {
                if (e.Kind == EntityKind.Refinery && enemyRefinery < 0) enemyRefinery = i;
                // Nearest enemy production structure is the wave target.
                if (cy >= 0)
                {
                    Fix64 d = Fix64.DistSq(e.X - homeX, e.Y - homeY);
                    if (d < bestEnemyD) { bestEnemyD = d; enemyStructure = i; }
                }
                else if (enemyStructure < 0) enemyStructure = i;
            }
        }
        if (cy < 0) return; // decapitated: nothing to command with

        // --- Construction via the sidebar flow (TICKET-P2-SIM-05): place
        // whatever the yard has finished; otherwise queue the next need. ---
        int wanted = !hasPlant ? 1
                   : refinery < 0 ? 3
                   : factory < 0 ? 2
                   : supply < draw + 40 ? 1
                   : refineryCount < cyCount ? 3 // one refinery per base (TICKET-AI-03)
                   : !hasTurret ? 5
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
                 && w.Credits(_player) >= World.GetStructureType(wanted).Cost)
        {
            output.Add(new Command(w.Tick, _player, CommandType.BuildStructure, cy,
                Fix64.Zero, Fix64.Zero, wanted));
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
        // purchase fires the moment the war chest covers it ---
        if (factory >= 0 && refinery >= 0 && harvesters < refineryCount && w.QueueLength(factory) == 0)
            output.Add(new Command(w.Tick, _player, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 4));
        else if (expansionDesired && w.Credits(_player) >= 3500 && w.QueueLength(factory) == 0)
            output.Add(new Command(w.Tick, _player, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 7));

        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive || e.PlayerId != _player || e.Kind != EntityKind.Harvester) continue;
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
            && wanted == 0 && w.QueueLength(factory) < 2)
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
            if (w.Credits(_player) >= w.GetUnitType(unitType).Cost)
            {
                output.Add(new Command(w.Tick, _player, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, unitType));
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
            if (e.Alive && e.PlayerId == _player && e.Kind == EntityKind.Unit
                && e.UnitType != 6 && e.UnitType != 7 && e.UnitType != 11
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
            if (!hostile.Alive || hostile.PlayerId < 0 || hostile.PlayerId == _player) continue;
            if (hostile.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            for (int j = 0; j < w.Entities.Count; j++)
            {
                var own = w.Entities[j];
                if (!own.Alive || own.PlayerId != _player) continue;
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
                if (!sc.Alive || sc.PlayerId != _player || sc.UnitType != 6) continue;
                if (sc.Moving) continue;
                int ward = -1; Fix64 wardD = Fix64.MaxValue;
                for (int j = 0; j < w.Entities.Count; j++)
                {
                    var h = w.Entities[j];
                    if (!h.Alive || h.PlayerId != _player || h.Kind != EntityKind.Harvester) continue;
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
                if (!ph.Alive || ph.PlayerId != _player || ph.UnitType != 9) continue;
                if (ph.ExplicitTarget >= 0 || ph.Moving) continue;
                int prey = -1; Fix64 preyD = Fix64.MaxValue;
                for (int j = 0; j < w.Entities.Count; j++)
                {
                    var h = w.Entities[j];
                    if (!h.Alive || h.PlayerId < 0 || h.PlayerId == _player || h.Kind != EntityKind.Harvester) continue;
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
                if (e.Alive && e.PlayerId == _player && e.Kind == EntityKind.Unit
                    && e.UnitType != 7 && e.UnitType != 6 && e.UnitType != 11
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

    /// <summary>Deterministic outward ring scan around own structures for a
    /// legal anchor, NEWEST structure first - so support buildings gravitate
    /// to the frontier (a fresh expansion CY gets its refinery, not the
    /// already-crowded home base).</summary>
    private bool TryFindPlacement(World w, out int ax, out int ay)
    {
        for (int i = w.Entities.Count - 1; i >= 0; i--)
        {
            var s = w.Entities[i];
            if (!s.Alive || s.PlayerId != _player) continue;
            if (s.Kind is not (EntityKind.ConstructionYard or EntityKind.PowerPlant or EntityKind.Factory or EntityKind.Refinery)) continue;
            int oax = World.AnchorOf(s.X, s.StructType), oay = World.AnchorOf(s.Y, s.StructType);
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
