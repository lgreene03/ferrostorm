using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ferrostorm.Client;

/// <summary>
/// P8-34: which campaign missions the player has WON, kept in user:// so a won
/// mission unlocks the next one across restarts. Nothing recorded it before:
/// every mission was open from the first launch, a win was forgotten the
/// moment the scene changed, and the campaign had no shape a player could
/// progress through.
///
/// The rule is the classic one and is stated once, in <see cref="IsUnlocked"/>:
/// mission 1 is always open, and mission N opens when mission N-1 has been won.
/// A set of won missions rather than a single high-water mark, so a later
/// manifest that reorders or inserts a mission cannot silently unlock or lock
/// anything the player did not actually win.
///
/// Written ATOMICALLY through <see cref="GameFiles.WriteAtomically"/>, as
/// P8-11's saves are, so a crash during the write leaves the previous file
/// whole rather than a truncated one that would read as no progress at all.
/// A missing or unreadable file reads as no wins, never as an exception: a
/// corrupt progress file must not take the menu down with it (the sidecar
/// reader's posture). Presentation state only; the sim never reads it.
/// </summary>
public static class CampaignProgress
{
    public const string FileName = "campaign-progress.json";

    /// <summary>Verification seam: the harness points this at a file of its
    /// own so a run never touches the player's real progress. Null in every
    /// played game; nothing in the client ever sets it.</summary>
    public static string? PathOverrideForTest;

    private static HashSet<int>? _won;

    public static string FilePath =>
        PathOverrideForTest ?? Path.Combine(ProjectSettings.GlobalizePath("user://"), FileName);

    public static bool HasWon(int mission)
    {
        Load();
        return _won!.Contains(mission);
    }

    /// <summary>THE unlock rule: mission 1 is always open, and every later
    /// mission opens once the one before it has been won.</summary>
    public static bool IsUnlocked(int mission) => mission <= 1 || HasWon(mission - 1);

    /// <summary>Remember a win. A mission already won is not written again, so
    /// replaying an old mission costs no disk work and cannot disturb the file.</summary>
    public static void RecordWin(int mission)
    {
        if (mission <= 0) return;
        Load();
        if (!_won!.Add(mission)) return;
        GameFiles.WriteAtomically(FilePath, ToJson(_won));
    }

    /// <summary>The won missions, ascending, as read or last written.</summary>
    public static List<int> WonMissions()
    {
        Load();
        var l = new List<int>(_won!);
        l.Sort();
        return l;
    }

    /// <summary>Verification seam: drop what is held in memory, so the next
    /// read comes from the file on disk exactly as it would after a restart.</summary>
    public static void ForgetForTest() => _won = null;

    private static void Load()
    {
        if (_won != null) return;
        _won = new HashSet<int>();
        string path = FilePath;
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("won", out var won) && won.ValueKind == JsonValueKind.Array)
                foreach (var m in won.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int idx) && idx > 0) _won.Add(idx);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"unreadable campaign progress {path}: {e.Message}");
        }
    }

    private static byte[] ToJson(HashSet<int> won)
    {
        var sorted = new List<int>(won);
        sorted.Sort();
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteStartArray("won");
            foreach (int m in sorted) w.WriteNumberValue(m);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}

/// <summary>
/// P8-34: the main menu remembers the last match it started, so a player who
/// set up a four-seat Brutal match on their favourite theatre does not set all
/// seven rows again after every restart. Stored as a MatchMeta sidecar, the
/// one shape saves and replays already store a setup in, so there is no second
/// encoding of a MatchSetup to drift from the first; written atomically by
/// MatchMeta.Write. Only a skirmish is remembered: a mission chooses its own
/// map and sides.
/// </summary>
public static class LastMatchMemory
{
    public const string FileName = "last-match.json";

    /// <summary>Verification seam, as <see cref="CampaignProgress.PathOverrideForTest"/>.</summary>
    public static string? PathOverrideForTest;

    public static string FilePath =>
        PathOverrideForTest ?? Path.Combine(ProjectSettings.GlobalizePath("user://"), FileName);

    public static void Remember(MatchSetup setup)
    {
        if (setup.IsMission) return;
        MatchMeta.For(setup, 0, 0).Write(FilePath);
    }

    /// <summary>The remembered skirmish, or null when there is none or the
    /// file cannot be read.</summary>
    public static MatchSetup? Recall()
    {
        var meta = MatchMeta.Read(FilePath);
        return meta is { Setup.IsMission: false } ? meta.Setup : null;
    }
}
