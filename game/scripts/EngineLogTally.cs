using Godot;

namespace Ferrostorm.Client;

/// <summary>
/// P8-57: what the ENGINE says while the client harness runs, counted where
/// the harness can read it. An engine error is not an exception: a transform
/// gone non-finite, a vector that cannot be normalised or a basis aimed along
/// its own up axis is reported on Godot's error stream and the frame carries
/// on, so a stage that asserts only that nothing threw passed a long match
/// that logged twenty thousand of them. Registered by VerifyRunner through
/// OS.AddLogger (Godot 4.5 and later), so every ERR_PRINT, WARN_PRINT,
/// push_error and push_warning anywhere in the process arrives here as it is
/// raised; a stage takes a mark, runs, and asserts on what arrived since.
///
/// Godot calls a Logger from whichever thread logged, possibly at once, so the
/// counts are interlocked and the first line since the mark is kept under a
/// lock. It never logs anything itself, which the engine forbids from inside a
/// Logger because it recurses. Harness only: nothing in a played game
/// registers it.
/// </summary>
public partial class EngineLogTally : Logger
{
    private int _errors, _warnings;
    private readonly object _gate = new();
    private string _firstSinceMark = "";

    /// <summary>Engine errors (and script and shader errors) since start.</summary>
    public int Errors => System.Threading.Volatile.Read(ref _errors);
    /// <summary>Engine warnings since start.</summary>
    public int Warnings => System.Threading.Volatile.Read(ref _warnings);

    /// <summary>The first error or warning raised since the last Mark, as
    /// "function: message", or empty when there has been none.</summary>
    public string FirstSinceMark
    {
        get { lock (_gate) return _firstSinceMark; }
    }

    /// <summary>Start a window: FirstSinceMark forgets what came before.</summary>
    public void Mark()
    {
        lock (_gate) _firstSinceMark = "";
    }

    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning) System.Threading.Interlocked.Increment(ref _warnings);
        else System.Threading.Interlocked.Increment(ref _errors);
        lock (_gate)
        {
            if (_firstSinceMark.Length == 0)
                _firstSinceMark = $"{function}: {(rationale.Length > 0 ? rationale : code)}";
        }
    }
}
