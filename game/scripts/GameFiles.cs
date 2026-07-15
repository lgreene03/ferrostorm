using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SAVE-01: where the client keeps player data. Everything the
/// player creates lives under user:// (saves, replays); everything the game
/// ships with lives under the repo root beside res://. The sim's own APIs
/// (World.Save, World.Load, Replay.Load, ReplayWriter.Finish) take Streams
/// and real OS paths, not Godot virtual paths, so every path handed to them
/// is globalized here exactly once and never anywhere else.
/// </summary>
public static class GameFiles
{
    /// <summary>Four slots. Enough to keep a campaign and an experiment apart
    /// without turning the overlay into a file browser.</summary>
    public const int SlotCount = 4;

    /// <summary>A replay shorter than three seconds is a mis-click, not a
    /// match; recording one would only litter the browser.</summary>
    public const int MinRecordedTicks = 45;

    private static string Dir(string name)
    {
        string d = Path.Combine(ProjectSettings.GlobalizePath("user://"), name);
        Directory.CreateDirectory(d);
        return d;
    }

    public static string SavesDir => Dir("saves");
    public static string ReplaysDir => Dir("replays");

    /// <summary>The repo root: the parent of res://, which is where /data sits.
    /// MainMenu already resolves maps this way; this is that idiom named once.</summary>
    public static string RepoRoot =>
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    /// <summary>Saves and replays store map paths RELATIVE to the repo root, not
    /// absolute: an absolute path bakes one machine's home directory into a
    /// file that is meant to outlive it.</summary>
    public static string Rel(string absolute) =>
        Path.GetRelativePath(RepoRoot, absolute).Replace('\\', '/');

    public static string Abs(string repoRelative) =>
        Path.GetFullPath(Path.Combine(RepoRoot, repoRelative));

    public static string SlotSave(int slot) => Path.Combine(SavesDir, $"slot-{slot}.fsav");
    public static string SlotMeta(int slot) => Path.Combine(SavesDir, $"slot-{slot}.json");

    /// <summary>Replays newest first. Orphan .frep files (no sidecar) are
    /// listed with a null meta rather than hidden: the recording is real even
    /// when its metadata is missing, and saying so is more honest than
    /// pretending the file is not there.</summary>
    public static List<(string Path, MatchMeta? Meta)> Replays()
    {
        var list = new List<(string, MatchMeta?)>();
        var files = new List<string>(Directory.GetFiles(ReplaysDir, "*.frep"));
        files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        foreach (string f in files)
            list.Add((f, MatchMeta.Read(Path.ChangeExtension(f, ".json"))));
        return list;
    }
}

/// <summary>
/// Everything needed to rebuild a match's starting world bit-for-bit. The
/// .frep format carries only a seed, a setup name and the command stream, so
/// the rest of the setup rides in a sidecar beside it (doc 18 Phase D: "sidecar
/// match-setup metadata per replay so recordings survive the new setup options
/// without touching the .frep format").
/// </summary>
public sealed class MatchSetup
{
    public string MapPath = "data/maps/skirmish-01.fmap";  // repo-relative
    public int MissionIndex;          // 0 = skirmish, else the campaign index
    public int AiPreset;              // 0 standard, 1 rusher, 2 turtle
    public long StartCredits = 8000;
    public ulong Seed = 2026;

    public string MapName => Path.GetFileNameWithoutExtension(MapPath);
    public bool IsMission => MissionIndex > 0;

    public string Describe() => IsMission
        ? $"MISSION {MissionIndex:00}  {MapName.ToUpperInvariant()}"
        : $"SKIRMISH  {MapName.ToUpperInvariant()}";
}

/// <summary>
/// The sidecar written beside every save slot and every replay: map name,
/// mission index, tick and a timestamp per the ticket, plus the setup fields
/// needed to rebuild the world the file resumes into. Hand-rolled with
/// Utf8JsonWriter and JsonDocument rather than reflection serialization, so
/// the on-disk shape is exactly what is written here and nothing else.
/// </summary>
public sealed class MatchMeta
{
    public MatchSetup Setup = new();
    public int Tick;              // save point, or a replay's recorded length
    public long Credits;          // display only
    public string Stamp = "";     // wall clock, UTC, ISO-ish - client-side only
    public string FinalHash = ""; // replays: the hash the stream must reproduce

    public static MatchMeta For(MatchSetup s, int tick, long credits) => new()
    {
        Setup = s,
        Tick = tick,
        Credits = credits,
        // The determinism rules bind /sim, not the client: a save file's
        // timestamp is presentation, never an input to the simulation.
        Stamp = Time.GetDatetimeStringFromSystem(utc: true, useSpace: true),
    };

    public void Write(string path)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("map", Setup.MapName);
            w.WriteString("map_path", Setup.MapPath);
            w.WriteNumber("mission", Setup.MissionIndex);
            w.WriteNumber("tick", Tick);
            w.WriteString("saved_at", Stamp);
            w.WriteNumber("credits", Credits);
            w.WriteNumber("ai_preset", Setup.AiPreset);
            w.WriteNumber("start_credits", Setup.StartCredits);
            w.WriteNumber("seed", Setup.Seed);
            if (FinalHash.Length > 0) w.WriteString("final_hash", FinalHash);
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>Null for a missing or unreadable sidecar. A corrupt sidecar
    /// must not take the menu down with it.</summary>
    public static MatchMeta? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            var m = new MatchMeta
            {
                Setup = new MatchSetup
                {
                    MapPath = Str(r, "map_path", "data/maps/skirmish-01.fmap"),
                    MissionIndex = Num(r, "mission"),
                    AiPreset = Num(r, "ai_preset"),
                    StartCredits = Num(r, "start_credits", 8000),
                    Seed = (ulong)Num(r, "seed", 2026),
                },
                Tick = Num(r, "tick"),
                Credits = Num(r, "credits"),
                Stamp = Str(r, "saved_at", ""),
                FinalHash = Str(r, "final_hash", ""),
            };
            return m;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"unreadable sidecar {path}: {e.Message}");
            return null;
        }
    }

    private static string Str(JsonElement r, string k, string dflt) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? dflt : dflt;
    private static int Num(JsonElement r, string k, int dflt = 0) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;
    private static long Num(JsonElement r, string k, long dflt) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : dflt;

    /// <summary>One line for a slot button or a replay row.</summary>
    public string Line() => $"{Setup.Describe()}   TICK {Tick}   {Stamp}";
}

/// <summary>
/// The campaign manifest, parsed once and shared. MainMenu owned this inline;
/// loading a campaign save needs the same allow-lists, and two parsers for one
/// file is how the two drift apart.
/// </summary>
public static class Campaign
{
    public readonly record struct Entry(
        string Path, string Title, int Index,
        HashSet<int>? Structs, HashSet<int>? Units);

    public static List<Entry> Load()
    {
        var missions = new List<Entry>();
        string manifest = Path.Combine(GameFiles.RepoRoot, "data", "campaign", "campaign.txt");
        if (!File.Exists(manifest)) return missions;
        int idx = 0;
        foreach (var line in File.ReadAllLines(manifest))
        {
            if (line.StartsWith('#') || line.Trim().Length == 0) continue;
            var parts = line.Split('|');
            missions.Add(new Entry(
                Path.Combine(GameFiles.RepoRoot, parts[0].Trim()), parts[2].Trim(), ++idx,
                parts.Length > 3 ? ParseAllow(parts[3]) : null,
                parts.Length > 4 ? ParseAllow(parts[4]) : null));
        }
        return missions;
    }

    public static Entry? ByIndex(int index)
    {
        foreach (var e in Load()) if (e.Index == index) return e;
        return null;
    }

    /// <summary>Allow column: "-" means nothing, a comma list means those ids,
    /// an absent column (caller passes nothing) means everything.</summary>
    private static HashSet<int> ParseAllow(string col)
    {
        var set = new HashSet<int>();
        foreach (var tok in col.Split(','))
            if (int.TryParse(tok.Trim(), out int id)) set.Add(id);
        return set;   // "-" parses to an empty set: nothing buildable
    }
}
