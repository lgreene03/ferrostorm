using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-44 (FEEL-08): the announcer's channel, as a queue with no audio in it.
/// The AudioDirector owns the one player it drives; this decides which line
/// that player carries and when, so the rules can be read and checked apart
/// from the engine.
///
/// The voice lines used to play on the UI pool, four players taken round
/// robin, so two lines asked for together talked over each other, a fifth UI
/// click could cut a line off mid-word, and "construction complete" ran 0.18 s
/// into the next one (P8-2's residual). Four rules now hold instead.
///
/// ONE AT A TIME. A single channel: a line never overlaps another.
///
/// PRIORITY ORDERED, ON THE ALERT LADDER. A line carries the AlertPriority of
/// the alert that asked for it (AlertService passes its own), so there is no
/// second ranking to drift from the first. Waiting lines play highest first,
/// oldest first within a band. A CRITICAL line (the base under fire, a strike
/// inbound, an enemy weapon charged, the verdict) cuts in over a lesser line
/// already playing; every other line waits its turn. A line that is cut is
/// not repeated: its caption is still on the stack.
///
/// DUPLICATES COLLAPSE. A line asked for while it is playing, while it is
/// waiting, or within DuplicateWindowSeconds of starting is one line, not two
/// (a waiting duplicate keeps the higher of the two priorities). The window is
/// the alert stack's own, so a caption and its voice collapse alike.
///
/// NEVER LATE. A waiting line that has not started by the time its caption
/// would have left the screen (AlertService.LifeOf its priority) is dropped:
/// a routine "unit ready" said twelve seconds late is noise. At most MaxQueued
/// lines wait, the weakest going first.
///
/// Times are seconds from whatever clock the caller keeps; a line's length is
/// its stream's own, handed in with the request.
/// </summary>
public sealed class Announcer
{
    public const double DuplicateWindowSeconds = AlertService.DedupeSeconds;
    public const int MaxQueued = 8;
    public static double MaxWaitSeconds(AlertPriority p) => AlertService.LifeOf(p);

    private sealed class Line
    {
        public string Name = "";
        public AlertPriority Priority;
        public double RequestedAt;
        public double Seconds;
        public long Seq;
    }

    private readonly List<Line> _waiting = new();
    private readonly Dictionary<string, double> _lastStarted = new();
    private readonly List<string> _startLog = new();
    private Line? _current;
    private double _currentEnds;
    private long _seq;

    /// <summary>Verification reads, counted where each thing happens.</summary>
    public int Queued { get; private set; }
    public int Collapsed { get; private set; }
    public int Started { get; private set; }
    public int Preempted { get; private set; }
    public int Dropped { get; private set; }

    /// <summary>Ask for a line. Returns false when it collapsed into one
    /// already playing, waiting or just said.</summary>
    public bool Request(string name, AlertPriority priority, double seconds, double now)
    {
        if (_current != null && now < _currentEnds && _current.Name == name) { Collapsed++; return false; }
        foreach (var w in _waiting)
            if (w.Name == name)
            {
                if (priority > w.Priority) w.Priority = priority;
                Collapsed++;
                return false;
            }
        if (_lastStarted.TryGetValue(name, out double at) && now - at < DuplicateWindowSeconds) { Collapsed++; return false; }
        Queued++;
        _waiting.Add(new Line { Name = name, Priority = priority, RequestedAt = now, Seconds = seconds, Seq = ++_seq });
        if (_waiting.Count > MaxQueued)
        {
            Line weakest = _waiting[0];
            foreach (var w in _waiting)
                if (w.Priority < weakest.Priority || (w.Priority == weakest.Priority && w.Seq < weakest.Seq)) weakest = w;
            _waiting.Remove(weakest);
            Dropped++;
        }
        return true;
    }

    /// <summary>Move the channel on to `now`: retire a line that has finished,
    /// drop waiting lines that are too late, and start the best waiting line
    /// if the channel is free or it may cut in. Returns the line to start, or
    /// null; `cut` says the line playing must be stopped first.</summary>
    public string? Advance(double now, out bool cut)
    {
        cut = false;
        if (_current != null && now >= _currentEnds) _current = null;
        for (int i = _waiting.Count - 1; i >= 0; i--)
            if (now - _waiting[i].RequestedAt >= MaxWaitSeconds(_waiting[i].Priority))
            {
                _waiting.RemoveAt(i);
                Dropped++;
            }
        Line? best = null;
        foreach (var w in _waiting)
            if (best == null || w.Priority > best.Priority || (w.Priority == best.Priority && w.Seq < best.Seq)) best = w;
        if (best == null) return null;
        if (_current != null)
        {
            if (best.Priority != AlertPriority.Critical || _current.Priority >= AlertPriority.Critical) return null;
            cut = true;
            Preempted++;
        }
        _waiting.Remove(best);
        _current = best;
        _currentEnds = now + best.Seconds;
        _lastStarted[best.Name] = now;
        Started++;
        _startLog.Add(best.Name);
        if (_startLog.Count > 16) _startLog.RemoveAt(0);
        return best.Name;
    }

    /// <summary>The line on the channel at `now`, or null.</summary>
    public string? Playing(double now) => _current != null && now < _currentEnds ? _current.Name : null;
    public AlertPriority? PlayingPriority(double now) => Playing(now) != null ? _current!.Priority : null;
    public int WaitingCount => _waiting.Count;
    /// <summary>The priority a waiting line will be played at, or null.</summary>
    public AlertPriority? WaitingPriority(string name)
    {
        foreach (var w in _waiting) if (w.Name == name) return w.Priority;
        return null;
    }
    /// <summary>The last lines started, oldest first (at most 16).</summary>
    public List<string> StartLog() => new(_startLog);

    public void Reset()
    {
        _waiting.Clear();
        _lastStarted.Clear();
        _startLog.Clear();
        _current = null;
        _currentEnds = 0;
        Queued = Collapsed = Started = Preempted = Dropped = 0;
    }
}
