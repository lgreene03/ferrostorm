namespace Ferrostorm.Sim;

public enum ArmourClass : byte { None = 0, Light = 1, Heavy = 2, Structure = 3 }
public enum Warhead : byte { AntiInfantry = 0, AntiArmour = 1, AntiBuilding = 2, Omni = 3 }

/// <summary>
/// Warhead vs armour percentage matrix (GDD s6), the COMPILED REFERENCE that
/// data/combat/damage_matrix.yaml must reproduce exactly.
///
/// P7-15 finished what this class's own comment had promised since Phase 1:
/// "wiring to /data YAML is a Phase 2 ticket". Until then this was the ONE
/// gameplay number left outside /data, in a project whose CLAUDE.md says every
/// gameplay number lives there and that "hand-editing stats in code is
/// forbidden".
///
/// It stays a compiled table for the reason every other catalogue keeps one: a
/// bare World with no /data must behave identically, which roughly 138 runner
/// scenarios depend on, and the round-trip proves the authored file equals it.
/// The LIVE table a match plays is World.DamageOf, which honours
/// RegisterDamageMatrix.
///
/// Values are percentages applied to base damage, integer maths only.
/// </summary>
public static class DamageMatrix
{
    public const int Warheads = 4;
    public const int ArmourClasses = 4;

    //                              None Light Heavy Structure
    private static readonly int[,] Pct =
    {
        /* AntiInfantry */ { 100,  60,  25,  25 },
        /* AntiArmour   */ {  40,  75, 100,  50 },
        /* AntiBuilding */ {  30,  40,  50, 100 },
        /* Omni         */ {  80,  80,  80,  80 },
    };

    public static int Apply(int baseDamage, Warhead w, ArmourClass a)
        => baseDamage * Pct[(int)w, (int)a] / 100;

    /// <summary>One compiled cell, so a loader comparing an authored file to
    /// this reference can name the cell that differs rather than reporting that
    /// "the matrix drifted".</summary>
    public static int PercentOf(Warhead w, ArmourClass a) => Pct[(int)w, (int)a];

    /// <summary>A flat copy in warhead-major order, the shape a registered
    /// override and the checksum fold both use.</summary>
    public static int[] ToArray()
    {
        var flat = new int[Warheads * ArmourClasses];
        for (int wi = 0; wi < Warheads; wi++)
            for (int ai = 0; ai < ArmourClasses; ai++)
                flat[wi * ArmourClasses + ai] = Pct[wi, ai];
        return flat;
    }
}

public readonly struct WeaponDef
{
    public readonly Fix64 Range;        // in cell units
    public readonly int Damage;
    public readonly Warhead Warhead;
    public readonly int CooldownTicks;
    public readonly Fix64 MinRange;     // targets closer than this cannot be engaged (artillery dead zone)
    public readonly Fix64 SplashRadius; // 0 = single target; else half damage to everything else in radius (friend or foe)

    /// <summary>ADR-028: can this weapon engage an aircraft? Defaults FALSE, so
    /// every weapon written before the air layer is ground-only from the moment
    /// it lands. That is the point rather than an omission: an aircraft
    /// everything can shoot is just a fast tank.</summary>
    public readonly bool AntiAir;

    public WeaponDef(Fix64 range, int damage, Warhead warhead, int cooldownTicks,
                     Fix64 minRange = default, Fix64 splashRadius = default, bool antiAir = false)
    {
        Range = range; Damage = damage; Warhead = warhead; CooldownTicks = cooldownTicks;
        MinRange = minRange; SplashRadius = splashRadius; AntiAir = antiAir;
    }
}

/// <summary>
/// The COMPILED REFERENCE weapon table. Since the /data weapons wave these nine
/// defs are no longer the runtime's source: every number here is authored in
/// data/weapons/*.yaml against data/schema.weapon.json, the loader registers
/// them into the world before tick 0, and a live match reads
/// World.GetWeaponType. What survives here is the reference a /data file must
/// reproduce exactly, which WeaponDataGate proves field by field.
///
/// The design reasoning that used to live in the comments below moved into the
/// files with the numbers, because a note explaining why the howitzer has a
/// dead zone belongs beside the dead zone a designer can edit. Read
/// data/weapons/ for it.
/// </summary>
public static class Weapons
{
    /// <summary>The highest compiled weapon id, and therefore the bound for
    /// every loop that enumerates the table (World's seed, the /data loader's
    /// completeness check). Ids are dense from 1; 0 is None and is not a
    /// weapon.</summary>
    public const int MaxWeaponId = 10;

    public static readonly WeaponDef None = new(Fix64.Zero, 0, Warhead.Omni, int.MaxValue);
    // Each def below is the reference copy of one file in data/weapons, named in
    // the trailing comment. The design reasoning that used to sit here (DR-17's
    // rename of the prototype names, the howitzer's dead zone, the emplacement
    // gun out-ranging the service rifle, the flak gun being the only anti-air
    // weapon in the game) now lives in those files, beside the numbers a
    // designer can actually edit. Ranges are Fix64 cells here and integer
    // hundredths of a cell in the files, the speed convention.
    public static readonly WeaponDef TankCannon = new(Fix64.FromInt(4), 30, Warhead.AntiArmour, 15);          // wpn_tank_cannon
    public static readonly WeaponDef ServiceRifle = new(Fix64.FromInt(3), 12, Warhead.AntiInfantry, 8);       // wpn_service_rifle
    public static readonly WeaponDef RocketTube = new(Fix64.FromInt(4), 40, Warhead.AntiArmour, 20);          // wpn_rocket_tube
    public static readonly WeaponDef TurretGun = new(Fix64.FromInt(5), 35, Warhead.AntiArmour, 12);           // wpn_turret_gun
    public static readonly WeaponDef Howitzer = new(Fix64.FromInt(9), 60, Warhead.AntiBuilding, 45,           // wpn_howitzer
        minRange: Fix64.FromInt(3), splashRadius: Fix64.FromFraction(3, 2));
    public static readonly WeaponDef BulwarkCannon = new(Fix64.FromInt(5), 50, Warhead.AntiArmour, 18);       // wpn_bulwark_cannon
    public static readonly WeaponDef VanguardGun = new(Fix64.FromInt(3), 18, Warhead.AntiInfantry, 6);        // wpn_vanguard_autocannon
    public static readonly WeaponDef EmplacementGun = new(Fix64.FromInt(4), 22, Warhead.AntiInfantry, 7);     // wpn_emplacement_gun
    public static readonly WeaponDef FlakGun = new(Fix64.FromInt(6), 40, Warhead.AntiArmour, 10, antiAir: true); // wpn_flak_gun
    // P7-11b: the hero's rifle, carried by both Commandos. A NEW def rather than
    // a second user of the service rifle, because the hero's whole claim over an
    // ordinary rifle squad is what it does to infantry.
    public static readonly WeaponDef CommandoRifle = new(Fix64.FromInt(4), 45, Warhead.AntiInfantry, 10); // wpn_commando_rifle

    /// <summary>The compiled reference table: the values a /data/weapons file
    /// must reproduce exactly. Static so that callers with no World (the
    /// balance tools, the client's own harnesses) can still read a weapon; a
    /// live match must read World.GetWeaponType instead, which honours
    /// RegisterWeaponType and is therefore what /data actually drives.</summary>
    public static WeaponDef Get(int id) => id switch
    {
        1 => TankCannon,
        2 => ServiceRifle,
        3 => RocketTube,
        4 => TurretGun,
        5 => Howitzer,
        6 => BulwarkCannon,
        7 => VanguardGun,
        8 => EmplacementGun,
        9 => FlakGun,
        10 => CommandoRifle,
        _ => None,
    };
}
