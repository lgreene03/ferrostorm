using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-35: a unit's STRONG AGAINST and WEAK AGAINST lines, for its sidebar
/// tooltip, DERIVED from the live world and never written down.
///
/// Three things decide them, all read from the catalogue the match plays:
/// the damage matrix (World.DamageOf, which honours /data's
/// damage_matrix.yaml), the armour class of every unit type in the catalogue
/// (and Structure, which the sim gives every building at spawn), and the
/// weapon each unit and building carries, its warhead and its anti-air flag.
/// Who can shoot whom is the sim's own predicate, World.WeaponCanEngage, asked
/// of a probe entity, so a flak gun is never said to be strong against tanks
/// it cannot fire on, and a ground gun is never listed as a threat to a plane.
///
/// STRONG AGAINST is the target class this unit's warhead hurts most, among
/// the targets its weapon can engage. WEAK AGAINST is the warhead that hurts
/// this unit's armour most, among the weapons in the catalogue that can
/// engage it. The percentage is the matrix cell. A tie names every class in
/// it, and a row that is flat across everything it can reach says so rather
/// than picking one. An edited matrix changes the lines with no edit here.
/// The words for each armour class and warhead are names, not judgements:
/// which of them a line picks is the matrix's decision.
/// </summary>
public static class Matchups
{
    public static string StrongLine(World w, int unitType)
    {
        var def = w.GetUnitType(unitType);
        if (def.WeaponId == 0) return "STRONG AGAINST: NOTHING, IT CARRIES NO WEAPON";
        var weapon = w.GetWeaponType(def.WeaponId);
        var ground = new SortedSet<ArmourClass>();
        var air = new SortedSet<ArmourClass>();
        foreach (var (armour, airborne) in Targets(w, weapon.AntiAir))
            (airborne ? air : ground).Add(armour);
        if (ground.Count + air.Count == 0) return "STRONG AGAINST: NOTHING IN THE FIELD IT CAN REACH";
        int best = int.MinValue;
        foreach (var a in ground) best = System.Math.Max(best, Pct(w, weapon.Warhead, a));
        foreach (var a in air) best = System.Math.Max(best, Pct(w, weapon.Warhead, a));
        var names = new List<string>();
        foreach (var a in ground) if (Pct(w, weapon.Warhead, a) == best) names.Add(ArmourWords(a));
        bool airBest = false;
        foreach (var a in air) airBest |= Pct(w, weapon.Warhead, a) == best;
        if (airBest) names.Add("AIRCRAFT");
        int groups = ground.Count + (air.Count > 0 ? 1 : 0);
        string what = groups > 1 && names.Count == groups ? "EVERY TARGET ALIKE" : Join(names);
        string line = $"STRONG AGAINST: {what} ({best}%)";
        if (ground.Count == 0) line += ", AND IT CANNOT FIRE ON THE GROUND";
        return line;
    }

    public static string WeakLine(World w, int unitType)
    {
        var def = w.GetUnitType(unitType);
        var probe = new Entity { Alive = true, Kind = def.Kind, UnitType = unitType, Armour = def.Armour };
        bool airborne = w.IsAirborne(in probe);
        var warheads = new SortedSet<Warhead>();
        foreach (int weaponId in CarriedWeapons(w))
        {
            var weapon = w.GetWeaponType(weaponId);
            if (w.WeaponCanEngage(weapon.AntiAir, in probe)) warheads.Add(weapon.Warhead);
        }
        if (warheads.Count == 0) return "WEAK AGAINST: NOTHING IN THE FIELD CAN REACH IT";
        int best = int.MinValue;
        foreach (var h in warheads) best = System.Math.Max(best, Pct(w, h, def.Armour));
        // An aircraft is hurt only by anti-air weapons, so that is the name a
        // player needs, whatever warhead those weapons carry.
        if (airborne) return $"WEAK AGAINST: ANTI-AIR FIRE ({best}%)";
        var names = new List<string>();
        foreach (var h in warheads) if (Pct(w, h, def.Armour) == best) names.Add($"{WarheadWords(h)} FIRE");
        string what = warheads.Count > 1 && names.Count == warheads.Count ? "EVERY WEAPON ALIKE" : Join(names);
        return $"WEAK AGAINST: {what} ({best}%)";
    }

    /// <summary>The armour class and airborne flag of every target in the
    /// catalogue a weapon with this anti-air flag can engage: each unit type,
    /// and each building (Structure armour, never airborne).</summary>
    private static IEnumerable<(ArmourClass Armour, bool Airborne)> Targets(World w, bool antiAir)
    {
        foreach (int id in w.UnitTypeIds())
        {
            var d = w.GetUnitType(id);
            var probe = new Entity { Alive = true, Kind = d.Kind, UnitType = id, Armour = d.Armour };
            if (w.WeaponCanEngage(antiAir, in probe)) yield return (d.Armour, w.IsAirborne(in probe));
        }
        foreach (int id in w.StructureTypeIds())
        {
            var s = w.GetStructureType(id);
            var probe = new Entity { Alive = true, Kind = s.Kind, StructType = id, Armour = ArmourClass.Structure };
            if (w.WeaponCanEngage(antiAir, in probe)) yield return (ArmourClass.Structure, false);
        }
    }

    /// <summary>Every weapon a unit type or a building in the catalogue
    /// carries, each once.</summary>
    private static SortedSet<int> CarriedWeapons(World w)
    {
        var ids = new SortedSet<int>();
        foreach (int id in w.UnitTypeIds()) if (w.GetUnitType(id).WeaponId != 0) ids.Add(w.GetUnitType(id).WeaponId);
        foreach (int id in w.StructureTypeIds()) if (w.GetStructureType(id).WeaponId != 0) ids.Add(w.GetStructureType(id).WeaponId);
        return ids;
    }

    /// <summary>One matrix cell as a percentage, through the live table.</summary>
    private static int Pct(World w, Warhead h, ArmourClass a) => w.DamageOf(100, h, a);

    /// <summary>A fingerprint of the live matrix, so a panel can tell when
    /// its lines need deriving again without copying the table every frame.</summary>
    public static int MatrixFingerprint(World w)
    {
        int fp = 17;
        for (int h = 0; h < DamageMatrix.Warheads; h++)
            for (int a = 0; a < DamageMatrix.ArmourClasses; a++)
                fp = unchecked(fp * 31 + Pct(w, (Warhead)h, (ArmourClass)a));
        return fp;
    }

    private static string ArmourWords(ArmourClass a) => a switch
    {
        ArmourClass.None => "UNARMOURED",
        ArmourClass.Light => "LIGHT ARMOUR",
        ArmourClass.Heavy => "HEAVY ARMOUR",
        ArmourClass.Structure => "STRUCTURES",
        _ => a.ToString().ToUpperInvariant(),
    };

    private static string WarheadWords(Warhead h) => h switch
    {
        Warhead.AntiInfantry => "ANTI-INFANTRY",
        Warhead.AntiArmour => "ANTI-ARMOUR",
        Warhead.AntiBuilding => "ANTI-BUILDING",
        Warhead.Omni => "ALL-PURPOSE",
        _ => h.ToString().ToUpperInvariant(),
    };

    private static string Join(List<string> names) => names.Count switch
    {
        0 => "NOTHING",
        1 => names[0],
        _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + " AND " + names[^1],
    };
}
