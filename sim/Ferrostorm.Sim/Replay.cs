using System.Globalization;

namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-SIM-07: replays. Determinism makes replays nearly free - a
/// replay is just the seed, the setup name, and the command stream; playback
/// re-simulates and must land on the identical final hash. This doubles as
/// the game's preservation promise (doc 07 differentiator) and as the
/// strongest possible determinism regression artefact: any sim change that
/// breaks old replays is caught by hash mismatch, never by drift.
/// Text format, line-based:
///   ferrostorm-replay v3
///   seed &lt;ulong&gt;
///   setup &lt;name&gt;
///   catalogue &lt;hex16&gt;      (v3, ADR-006: the recording world's catalogue checksum; optional)
///   c &lt;tick&gt; &lt;player&gt; &lt;type&gt; &lt;entity&gt; &lt;aux&gt; &lt;xraw&gt; &lt;yraw&gt;
///   hash &lt;hex16&gt;
/// v2 streams (no catalogue line) remain loadable; a missing catalogue line
/// means do not check, never refuse (ADR-006).
/// </summary>
public sealed class ReplayWriter
{
    private readonly List<string> _lines = new();

    public ReplayWriter(ulong seed, string setup, ulong? catalogueChecksum = null)
    {
        _lines.Add("ferrostorm-replay v3");
        _lines.Add($"seed {seed.ToString(CultureInfo.InvariantCulture)}");
        _lines.Add($"setup {setup}");
        // ADR-006: recorded so playback under a different /data refuses with
        // both checksums named instead of reporting DIVERGED after the fact.
        if (catalogueChecksum is { } cc) _lines.Add($"catalogue {cc:X16}");
    }

    public void Record(in Command c)
        => _lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"c {c.Tick} {c.PlayerId} {(byte)c.Type} {c.EntityId} {c.AuxId} {c.X.Raw} {c.Y.Raw} {(c.Queued ? 1 : 0)}"));

    public void Finish(ulong finalHash, string path)
    {
        _lines.Add($"hash {finalHash:X16}");
        File.WriteAllLines(path, _lines);
    }
}

public sealed class Replay
{
    public ulong Seed { get; private set; }
    public string Setup { get; private set; } = "";
    public ulong FinalHash { get; private set; }
    /// <summary>ADR-006: the catalogue checksum the recording world played
    /// against, when the stream carries one. v2 streams carry none, and none
    /// means unchecked, never refused.</summary>
    public bool HasCatalogueChecksum { get; private set; }
    public ulong CatalogueChecksum { get; private set; }
    private readonly Dictionary<int, List<Command>> _byTick = new();
    private static readonly Command[] Empty = Array.Empty<Command>();

    public IReadOnlyList<Command> CommandsFor(int tick)
        => _byTick.TryGetValue(tick, out var list) ? list : Empty;

    /// <summary>ADR-006: playback under a different catalogue is refused before
    /// a single tick re-simulates, with both checksums named, rather than being
    /// allowed to run to an inevitable DIVERGED verdict. A recording with no
    /// checksum (pre-v3) cannot be checked and passes untouched.</summary>
    public void AssertCatalogueMatches(ulong worldChecksum)
    {
        if (!HasCatalogueChecksum || CatalogueChecksum == worldChecksum) return;
        throw new InvalidDataException(
            $"catalogue mismatch: this replay was recorded with catalogue checksum 0x{CatalogueChecksum:X16} " +
            $"but the game is running catalogue 0x{worldChecksum:X16}. " +
            "Playback is refused rather than allowed to diverge (ADR-006). " +
            "Restore the /data files the recording was made with.");
    }

    public static Replay Load(string path)
    {
        var r = new Replay();
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || (lines[0] != "ferrostorm-replay v2" && lines[0] != "ferrostorm-replay v3"))
            throw new FormatException("not a ferrostorm replay (v2/v3)");
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(' ');
            switch (parts[0])
            {
                case "seed": r.Seed = ulong.Parse(parts[1], CultureInfo.InvariantCulture); break;
                case "setup": r.Setup = parts[1]; break;
                case "catalogue":
                    r.CatalogueChecksum = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    r.HasCatalogueChecksum = true;
                    break;
                case "hash": r.FinalHash = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture); break;
                case "c":
                {
                    int tick = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    var cmd = new Command(
                        tick,
                        int.Parse(parts[2], CultureInfo.InvariantCulture),
                        (CommandType)byte.Parse(parts[3], CultureInfo.InvariantCulture),
                        int.Parse(parts[4], CultureInfo.InvariantCulture),
                        new Fix64(long.Parse(parts[6], CultureInfo.InvariantCulture)),
                        new Fix64(long.Parse(parts[7], CultureInfo.InvariantCulture)),
                        int.Parse(parts[5], CultureInfo.InvariantCulture),
                        parts[8] == "1");
                    if (!r._byTick.TryGetValue(tick, out var list)) r._byTick[tick] = list = new List<Command>();
                    list.Add(cmd);
                    break;
                }
                default: throw new FormatException($"unknown replay line '{parts[0]}'");
            }
        }
        return r;
    }
}
