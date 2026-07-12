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
///   ferrostorm-replay v2
///   seed &lt;ulong&gt;
///   setup &lt;name&gt;
///   c &lt;tick&gt; &lt;player&gt; &lt;type&gt; &lt;entity&gt; &lt;aux&gt; &lt;xraw&gt; &lt;yraw&gt;
///   hash &lt;hex16&gt;
/// </summary>
public sealed class ReplayWriter
{
    private readonly List<string> _lines = new();

    public ReplayWriter(ulong seed, string setup)
    {
        _lines.Add("ferrostorm-replay v2");
        _lines.Add($"seed {seed.ToString(CultureInfo.InvariantCulture)}");
        _lines.Add($"setup {setup}");
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
    private readonly Dictionary<int, List<Command>> _byTick = new();
    private static readonly Command[] Empty = Array.Empty<Command>();

    public IReadOnlyList<Command> CommandsFor(int tick)
        => _byTick.TryGetValue(tick, out var list) ? list : Empty;

    public static Replay Load(string path)
    {
        var r = new Replay();
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != "ferrostorm-replay v2")
            throw new FormatException("not a ferrostorm replay (v2)");
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(' ');
            switch (parts[0])
            {
                case "seed": r.Seed = ulong.Parse(parts[1], CultureInfo.InvariantCulture); break;
                case "setup": r.Setup = parts[1]; break;
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
