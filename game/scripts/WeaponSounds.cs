using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-44 (FEEL-14): the report each weapon makes. The effects layer had two
/// sounds for ten weapons, `shot_rifle` for the rifle and the autocannon and
/// `shot_cannon` for everything else, so a rocket, a flak burst, a howitzer
/// and a tank's gun all sounded alike and `shot_rocket` shipped unplayed.
///
/// The table is keyed by the /data weapon NAME and resolved through
/// UnitCatalogue.WeaponIdOf, the one name-to-number map the sim already owns,
/// so a renumbering cannot point a sound at the wrong gun and a misspelt name
/// fails loudly at first use rather than falling silent. Pitch varies a shared
/// report where two weapons are the same class at a different weight (the
/// bulwark's cannon is the tank's, lower). A weapon the table does not name
/// takes a report from its LIVE def: anti-air is flak, a splash weapon is
/// artillery, an anti-infantry warhead is a rifle, and anything else is a
/// cannon, which is also what an unarmed or unknown id gets.
/// </summary>
public static class WeaponSounds
{
    private static readonly (string Weapon, string Sound, float Pitch)[] Table =
    {
        ("wpn_service_rifle", "shot_rifle", 1.00f),
        ("wpn_commando_rifle", "shot_rifle", 0.85f),
        ("wpn_vanguard_autocannon", "shot_heavy_mg", 1.00f),
        ("wpn_emplacement_gun", "shot_heavy_mg", 0.90f),
        ("wpn_tank_cannon", "shot_cannon", 1.00f),
        ("wpn_turret_gun", "shot_cannon", 0.94f),
        ("wpn_bulwark_cannon", "shot_cannon", 0.82f),
        ("wpn_rocket_tube", "shot_rocket", 1.00f),
        ("wpn_howitzer", "shot_howitzer", 1.00f),
        ("wpn_flak_gun", "shot_flak", 1.00f),
    };

    private static readonly Dictionary<int, (string Sound, float Pitch)> ById = Build();

    private static Dictionary<int, (string Sound, float Pitch)> Build()
    {
        var d = new Dictionary<int, (string Sound, float Pitch)>();
        foreach (var (weapon, sound, pitch) in Table) d[UnitCatalogue.WeaponIdOf(weapon)] = (sound, pitch);
        return d;
    }

    /// <summary>The report for weapon `weaponId`, and its pitch: the table's
    /// row, or for a weapon it does not name, a class read off `def`.</summary>
    public static (string Sound, float Pitch) FireSoundOf(int weaponId, WeaponDef? def = null)
    {
        if (ById.TryGetValue(weaponId, out var row)) return row;
        if (def is { Damage: > 0 } d)
        {
            if (d.AntiAir) return ("shot_flak", 1f);
            if (d.SplashRadius > Fix64.Zero) return ("shot_howitzer", 1f);
            if (d.Warhead == Warhead.AntiInfantry) return ("shot_rifle", 1f);
        }
        return ("shot_cannon", 1f);
    }

    /// <summary>Every report the table names, once each, for the harness's
    /// asset check.</summary>
    public static List<string> Sounds()
    {
        var l = new List<string>();
        foreach (var (_, sound, _) in Table) if (!l.Contains(sound)) l.Add(sound);
        return l;
    }
}
