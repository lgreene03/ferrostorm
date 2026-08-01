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

    /// <summary>
    /// The directory that holds /data. Running from source that is the repo
    /// root, the parent of res://; in a PACKAGED build there is no repo, so it
    /// is the folder beside the executable.
    ///
    /// This has to be a search rather than one path because ADR-006 made /data
    /// the runtime source and the sim's loaders take REAL OS PATHS
    /// (CatalogueFiles.RegisterAll walks directories, MapData.Load opens a
    /// file). Files inside an exported .pck are not real OS files, so /data
    /// cannot simply be imported into res://: it ships as a loose folder
    /// beside the game, which is also what keeps it moddable and what lets one
    /// code path serve both layouts.
    ///
    /// Resolved once and cached. If no candidate holds a /data the first is
    /// returned unchanged, so the existing readable failure still fires (the
    /// catrefuse gate pins "the /data loader fails readably for a missing
    /// directory"), with a warning naming everywhere it looked, so the log says
    /// what actually went wrong rather than only that something did.
    /// </summary>
    public static string RepoRoot => _dataRoot ??= ResolveDataRoot();
    private static string? _dataRoot;

    private static IEnumerable<string> DataRootCandidates()
    {
        // 1 and 2. From source: res:// is game/, so its parent is the repo root.
        //
        // GUARDED, and the guard is not defensive padding: in a PACKAGED build
        // res:// lives inside the .pck and GlobalizePath returns an EMPTY
        // STRING, on which Path.GetFullPath throws ArgumentException. That
        // threw before the search ever reached the executable-directory
        // candidate below, which is the one a package needs, so the packaged
        // game died in MainMenu._Ready. It could only ever be caught by running
        // a real export, which is exactly what found it.
        string res = ProjectSettings.GlobalizePath("res://");
        if (!string.IsNullOrEmpty(res))
        {
            yield return Path.GetFullPath(Path.Combine(res, ".."));
            yield return Path.GetFullPath(res);
        }
        // 3. Packaged: the folder holding the executable, the shipped layout
        //    (the game binary with a data folder beside it).
        string? exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        if (!string.IsNullOrEmpty(exeDir))
        {
            yield return Path.GetFullPath(exeDir);
            // 4. macOS puts the binary at Game.app/Contents/MacOS/game, so the
            //    folder the user actually sees the .app sitting in is three up.
            yield return Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
        }
    }

    private static string ResolveDataRoot()
    {
        string? first = null;
        var searched = new List<string>();
        foreach (string c in DataRootCandidates())
        {
            first ??= c;
            searched.Add(c);
            if (Directory.Exists(Path.Combine(c, "data"))) return c;
        }
        GD.PushWarning("no data directory found beside the game. Searched: "
                       + string.Join(", ", searched)
                       + ". A packaged build must ship the data folder beside the executable.");
        return first ?? ".";
    }

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
    /// <summary>DR-14b: the rung on doc 28's ladder - 0 easy, 1 normal, 2 hard,
    /// 3 brutal. A SEPARATE axis from AiPreset above, which is the opponent's
    /// taste in wave size rather than its strength. Defaults to 1 and NOT to
    /// enum-zero, here and in the sidecar reader, for the reason the faction
    /// fields below spell out: every file written before this field existed
    /// describes a match played against a Normal commander, so a missing value
    /// must decode to Normal or every old save and replay resumes against a
    /// different opponent and reports DIVERGED.</summary>
    public int AiDifficulty = 1;
    public long StartCredits = 8000;
    public ulong Seed = 2026;
    // TICKET-P6-FACTION-01: the sides, applied by BuildStartingWorld before
    // tick 0 (skirmish only; a mission's map declares its own). TWO fields,
    // not one: every sidecar written before this ticket describes a match in
    // which both players were Directorate (the World constructor default),
    // so the missing-field default must decode to that legacy pairing or
    // every old replay re-simulates a different world and reports DIVERGED.
    // A fresh menu skirmish gives the opponent the other side (doc 24).
    public int Faction;               // player 0: 0 Directorate, 1 Sodality
    public int OppFaction;            // player 1
    /// <summary>How many seats this skirmish is played with, INCLUDING the
    /// local player. Zero means "as many as the map declares", which is what
    /// every match written before this field meant and what the menu offers by
    /// default. P7-8d derived the count from the map alone; GDD s9 asks for
    /// "1-7 opponents", which is a CHOICE, so it has to be carried.
    ///
    /// Optional in the sidecar for the same reason ai_difficulty is: absent
    /// means the old behaviour, so no format version and no migration.</summary>
    public int Seats;
    /// <summary>P7-8h: how the seats are DIVIDED, which is GDD s9's "up to 4v4"
    /// expressed as one field rather than as a per-seat picker no menu has room
    /// for. <see cref="TeamsFreeForAll"/> is every seat on its own team, which is
    /// exactly what the sim already does when nothing calls SetTeam, so it is
    /// today's behaviour by construction rather than by resemblance.
    /// <see cref="TeamsEvenSides"/> puts the even seats on one side and the odd
    /// seats on the other, so a four-start map is 2v2 and a two-start map is the
    /// 1v1 it always was.
    ///
    /// Optional in the sidecar for the same reason ai_difficulty and seats are:
    /// absent means zero means free-for-all, which is what every match written
    /// before this field was, so no format version and no migration.</summary>
    public int TeamMode;

    /// <summary>Every seat fights alone. The sim's own default, and the value a
    /// sidecar that has never heard of teams decodes to.</summary>
    public const int TeamsFreeForAll = 0;
    /// <summary>Seat p fights for team p % 2: seats 0 and 2 against seats 1 and
    /// 3.</summary>
    public const int TeamsEvenSides = 1;

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
            w.WriteNumber("ai_difficulty", Setup.AiDifficulty);
            w.WriteNumber("start_credits", Setup.StartCredits);
            w.WriteNumber("seed", Setup.Seed);
            // TICKET-P6-FACTION-01: the .frep format stays untouched (seed,
            // setup name, command stream); the sides ride here, in the
            // sidecar, exactly as every other setup field already does.
            w.WriteNumber("faction", Setup.Faction);
            w.WriteNumber("opp_faction", Setup.OppFaction);
            w.WriteNumber("seats", Setup.Seats);
            w.WriteNumber("team_mode", Setup.TeamMode);
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
                    // DR-14b: absent in every sidecar written before the ladder,
                    // and those matches were all played at Normal. Defaulting to
                    // 1 rather than 0 is what keeps them resuming against the
                    // opponent they actually faced.
                    AiDifficulty = Num(r, "ai_difficulty", 1),
                    StartCredits = Num(r, "start_credits", 8000),
                    Seed = (ulong)Num(r, "seed", 2026),
                    // Absent in every pre-P6 sidecar: default 0/0, the legacy
                    // both-Directorate pairing those matches actually played.
                    Faction = Num(r, "faction"),
                    OppFaction = Num(r, "opp_faction"),
                    // Absent in every sidecar written before the opponent
                    // count existed. Zero means "fill the map", which is
                    // exactly what those matches did, so they resume against
                    // the same opposition rather than a different one.
                    Seats = Num(r, "seats"),
                    // Absent in every sidecar written before the team mode
                    // existed. Zero is FREE FOR ALL, which is what those
                    // matches were, so they resume as the free-for-all they
                    // were played as rather than as somebody's alliance.
                    TeamMode = Num(r, "team_mode"),
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
