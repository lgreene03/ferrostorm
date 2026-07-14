namespace Ferrostorm.Sim;

public enum ArmourClass : byte { None = 0, Light = 1, Heavy = 2, Structure = 3 }
public enum Warhead : byte { AntiInfantry = 0, AntiArmour = 1, AntiBuilding = 2, Omni = 3 }

/// <summary>
/// Warhead vs armour percentage matrix (GDD s6). Phase 1: compiled-in table;
/// wiring to /data YAML is a Phase 2 ticket (needs the data loader).
/// Values are percentages applied to base damage, integer maths only.
/// </summary>
public static class DamageMatrix
{
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
}

public readonly struct WeaponDef
{
    public readonly Fix64 Range;        // in cell units
    public readonly int Damage;
    public readonly Warhead Warhead;
    public readonly int CooldownTicks;
    public readonly Fix64 MinRange;     // targets closer than this cannot be engaged (artillery dead zone)
    public readonly Fix64 SplashRadius; // 0 = single target; else half damage to everything else in radius (friend or foe)

    public WeaponDef(Fix64 range, int damage, Warhead warhead, int cooldownTicks,
                     Fix64 minRange = default, Fix64 splashRadius = default)
    {
        Range = range; Damage = damage; Warhead = warhead; CooldownTicks = cooldownTicks;
        MinRange = minRange; SplashRadius = splashRadius;
    }
}

public static class Weapons
{
    public static readonly WeaponDef None = new(Fix64.Zero, 0, Warhead.Omni, int.MaxValue);
    public static readonly WeaponDef TestCannon = new(Fix64.FromInt(4), 30, Warhead.AntiArmour, 15);
    public static readonly WeaponDef TestRifle = new(Fix64.FromInt(3), 12, Warhead.AntiInfantry, 8);
    public static readonly WeaponDef TestRocket = new(Fix64.FromInt(4), 40, Warhead.AntiArmour, 20);
    public static readonly WeaponDef TurretGun = new(Fix64.FromInt(5), 35, Warhead.AntiArmour, 12);
    // Howitzer: enormous reach, a 3-cell dead zone, slow, and splash that
    // does not care whose uniform you wear (TICKET-P2-SIM-14).
    public static readonly WeaponDef Howitzer = new(Fix64.FromInt(9), 60, Warhead.AntiBuilding, 45,
        minRange: Fix64.FromInt(3), splashRadius: Fix64.FromFraction(3, 2));
    // Bulwark cannon: the Directorate's argument-ender. Heavy single-target.
    public static readonly WeaponDef BulwarkCannon = new(Fix64.FromInt(5), 50, Warhead.AntiArmour, 18);
    // Vanguard autocannon (TICKET-P4-SLICE-01): a fast-cycling anti-infantry
    // gun - the per-cost edge over massed rifles that justifies the car.
    public static readonly WeaponDef VanguardGun = new(Fix64.FromInt(3), 18, Warhead.AntiInfantry, 6);

    public static WeaponDef Get(int id) => id switch
    {
        1 => TestCannon,
        2 => TestRifle,
        3 => TestRocket,
        4 => TurretGun,
        5 => Howitzer,
        6 => BulwarkCannon,
        7 => VanguardGun,
        _ => None,
    };
}
