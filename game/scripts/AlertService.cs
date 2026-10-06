using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-10: how much a message matters, GDD s10's alert ladder made explicit.
/// Critical is "drop everything" (the base is being hit, an enemy strike is
/// inbound, a building was taken, an enemy superweapon stands charged);
/// Urgent is a problem to answer soon (a harvester under fire, a brown-out,
/// the radar lost or jammed, a building sabotaged, our own superweapon
/// ready); Notice is good or neutral news; Routine is the acknowledgement or
/// refusal of something the player just pressed.
/// </summary>
public enum AlertPriority { Routine = 0, Notice = 1, Urgent = 2, Critical = 3 }

/// <summary>One message and everything that goes with it. Only Text is
/// required: a bare toast is an Alert with nothing else set.</summary>
public sealed record Alert(string Text, AlertPriority Priority = AlertPriority.Routine)
{
    /// <summary>A non-positional cue by AudioDirector name, or null.</summary>
    public string? Cue { get; init; }
    public float CueDb { get; init; } = -4f;
    /// <summary>A battlefield voice line by name, or null. Played through the
    /// scene's PlayVo, so its per-line cooldown still holds, and (P8-44) handed
    /// to the announcer at THIS alert's Priority, so the voice queue ranks by
    /// the same ladder as the stack and keeps no ranking of its own.</summary>
    public string? Vo { get; init; }
    /// <summary>A minimap ping at this map position (world X, Z), or null.</summary>
    public Vector2? PingAt { get; init; }
    public Color PingColour { get; init; }
    /// <summary>Record the ping position for the jump-to-event key.</summary>
    public bool Jump { get; init; }
    /// <summary>What the message is ABOUT: an entity id, or a seat for a
    /// seat-level message; -1 for neither. Part of the de-duplication key with
    /// the text and the ping position, so two sabotaged plants or two fallen
    /// commanders are two lines while a repeat about the same one is one.</summary>
    public int Subject { get; init; } = -1;
}

/// <summary>
/// P8-10: THE ONE ALERT SERVICE. Every toast and alert the battle scene
/// raises comes through Raise, which owns the toast stack, the cue, the voice
/// line, the minimap ping and the jump-to-event record together.
///
/// It replaces a single Label that the next ShowToast overwrote and whose
/// tween it killed (FEEL-10), so the last call in a frame won: a "GUARD (3
/// UNITS)" pressed in the same moment as a base alert erased the alert. Three
/// rules now hold instead.
///
/// STACKED. Up to Capacity toasts show at once, one line each, so two things
/// happening together are both read.
///
/// DE-DUPLICATED. The same message raised again inside DedupeSeconds refreshes
/// the toast already standing (its life restarts and it moves to the top of
/// its band) and replays nothing: no second cue, voice or ping. "The same" is
/// the text AND the subject AND the ping position, never the text alone: a key
/// pressed five times is one line, but two plants sabotaged in the same tick
/// carry the same words about two buildings, and each keeps its own line,
/// ping, cue and jump record.
///
/// PRIORITY ORDERED. Higher priorities sit above lower ones, newest first
/// within a band, and a toast is only ever pushed out by one of its own
/// priority or higher. When the stack is full of more important messages, a
/// lesser one waits for a free line rather than evicting one: a routine
/// acknowledgement can never push a base alert off the screen.
///
/// Wall time, not ticks, for the same reason the scene's alert cooldowns use
/// it: a toast is read by a person, and the harness steps ticks far faster
/// than a person reads. It is presentation only: nothing here touches the
/// sim, and nothing is saved.
/// </summary>
public partial class AlertService : VBoxContainer
{
    public const int Capacity = 5;
    public const double DedupeSeconds = 3.0;
    /// <summary>How long a lesser toast may wait for a line before it is
    /// dropped as stale. The cue, voice and ping still fired when it was
    /// raised; only the line is lost, and only after this long.</summary>
    public const double MaxWaitSeconds = 10.0;
    private const double FadeInSeconds = 0.15;
    private const double FadeOutSeconds = 0.5;

    /// <summary>A toast's life on screen by priority. Routine matches the old
    /// single toast exactly (0.15 s in, 2.2 s held, 0.5 s out).</summary>
    public static double LifeOf(AlertPriority p) => p switch
    {
        AlertPriority.Critical => 8.0,
        AlertPriority.Urgent => 5.5,
        AlertPriority.Notice => 4.0,
        _ => 2.85,
    };

    // The closed doc 16 palette: the routine toast keeps the gold it always
    // wore, good news reads in bone, and the two warning bands take the alert
    // red the base ping and the desync notice already use.
    private static Color ColourOf(AlertPriority p) => p switch
    {
        AlertPriority.Critical => new Color(0.92f, 0.28f, 0.22f),
        AlertPriority.Urgent => new Color(0.95f, 0.62f, 0.15f),
        AlertPriority.Notice => UplinkUi.Bone,
        _ => UplinkUi.FerriteGold,
    };

    private sealed class Entry
    {
        public string Text = "";
        public int Subject = -1;
        public Vector2? PingAt;
        public AlertPriority Priority;
        public double RaisedAt;
        public double ShownAt = -1;
        public long Seq;
        public Label? Line;
    }

    private readonly List<Entry> _live = new();
    private readonly List<Entry> _waiting = new();
    private long _seq;
    private System.Action<string, float>? _playCue;
    private System.Action<string, AlertPriority>? _playVo;
    private System.Action<Vector2, Color>? _ping;
    private System.Action<Vector2>? _jump;
    private System.Func<double> _now = () => Time.GetTicksMsec() / 1000.0;

    /// <summary>The text of the most recent toast raised, refreshed by a
    /// repeat. It is what the player was last told, which is what the old
    /// single toast's text meant and what every harness check reading
    /// ToastText relies on.</summary>
    public string LastSaid { get; private set; } = "";
    /// <summary>Verification reads: counted, never recomputed.</summary>
    public int RaisedCount { get; private set; }
    public int DedupedCount { get; private set; }
    public int DroppedCount { get; private set; }

    /// <summary>Wire the side effects. Each is the scene's own, so a cue
    /// still goes through the AudioDirector, a voice line through PlayVo and
    /// its cooldowns, a ping onto the minimap and the jump record through
    /// RecordAlert. The voice line goes with the alert's priority (P8-44).</summary>
    public void Init(System.Action<string, float> playCue, System.Action<string, AlertPriority> playVo,
                     System.Action<Vector2, Color> ping, System.Action<Vector2> jump)
    {
        _playCue = playCue;
        _playVo = playVo;
        _ping = ping;
        _jump = jump;
        Name = "AlertStack";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
    }

    /// <summary>Raise one message. Returns false when it was a repeat inside
    /// the de-duplication window and so only refreshed the standing toast.</summary>
    public bool Raise(Alert a)
    {
        double now = _now();
        Prune(now);
        LastSaid = a.Text;
        var same = Find(_live, a, now) ?? Find(_waiting, a, now);
        if (same != null)
        {
            DedupedCount++;
            same.RaisedAt = now;
            if (same.ShownAt >= 0) same.ShownAt = now;
            if (a.Priority > same.Priority) same.Priority = a.Priority;
            same.Seq = ++_seq;
            Relayout();
            return false;
        }
        RaisedCount++;
        var e = new Entry
        {
            Text = a.Text, Subject = a.Subject, PingAt = a.PingAt, Priority = a.Priority, RaisedAt = now, Seq = ++_seq,
        };
        if (_live.Count < Capacity) Show(e, now);
        else
        {
            var weakest = Weakest();
            if (weakest != null && weakest.Priority <= e.Priority)
            {
                Retire(weakest);
                Show(e, now);
            }
            else _waiting.Add(e);
        }
        // The side effects fire NOW whether or not the line had to wait: the
        // klaxon and the ping are the alert, and the line is its caption.
        if (a.Cue != null) _playCue?.Invoke(a.Cue, a.CueDb);
        if (a.Vo != null) _playVo?.Invoke(a.Vo, a.Priority);
        if (a.PingAt is { } at)
        {
            _ping?.Invoke(at, a.PingColour);
            if (a.Jump) _jump?.Invoke(at);
        }
        Relayout();
        return true;
    }

    public override void _Process(double delta)
    {
        double now = _now();
        Prune(now);
        foreach (var e in _live)
        {
            if (e.Line is null) continue;
            double fadeIn = (now - e.ShownAt) / FadeInSeconds;
            double fadeOut = (e.ShownAt + LifeOf(e.Priority) - now) / FadeOutSeconds;
            e.Line.Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(System.Math.Min(fadeIn, fadeOut), 0.0, 1.0));
        }
    }

    /// <summary>A standing toast that is the SAME message: text, subject and
    /// ping position all equal, raised inside the window.</summary>
    private static Entry? Find(List<Entry> list, Alert a, double now)
    {
        foreach (var e in list)
            if (e.Text == a.Text && e.Subject == a.Subject && e.PingAt == a.PingAt
                && now - e.RaisedAt < DedupeSeconds) return e;
        return null;
    }

    /// <summary>The live toast a newcomer may displace: the lowest priority,
    /// oldest within it.</summary>
    private Entry? Weakest()
    {
        Entry? w = null;
        foreach (var e in _live)
            if (w == null || e.Priority < w.Priority || (e.Priority == w.Priority && e.Seq < w.Seq)) w = e;
        return w;
    }

    private void Show(Entry e, double now)
    {
        e.ShownAt = now;
        var line = new Label
        {
            Text = e.Text,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        line.AddThemeFontSizeOverride("font_size", e.Priority == AlertPriority.Critical ? 16 : 15);
        line.AddThemeColorOverride("font_color", ColourOf(e.Priority));
        AddChild(line);
        e.Line = line;
        _live.Add(e);
    }

    private void Retire(Entry e)
    {
        _live.Remove(e);
        if (e.Line != null)
        {
            RemoveChild(e.Line);
            e.Line.QueueFree();
            e.Line = null;
        }
    }

    /// <summary>Retire what has run its life, drop what waited too long, and
    /// move waiting toasts up into free lines, most important first.</summary>
    private void Prune(double now)
    {
        bool changed = false;
        for (int i = _live.Count - 1; i >= 0; i--)
            if (now - _live[i].ShownAt >= LifeOf(_live[i].Priority)) { Retire(_live[i]); changed = true; }
        for (int i = _waiting.Count - 1; i >= 0; i--)
            if (now - _waiting[i].RaisedAt >= MaxWaitSeconds) { _waiting.RemoveAt(i); DroppedCount++; changed = true; }
        while (_live.Count < Capacity && _waiting.Count > 0)
        {
            Entry best = _waiting[0];
            foreach (var w in _waiting)
                if (w.Priority > best.Priority || (w.Priority == best.Priority && w.Seq < best.Seq)) best = w;
            _waiting.Remove(best);
            Show(best, now);
            changed = true;
        }
        if (changed) Relayout();
    }

    /// <summary>Display order: priority descending, newest first within it.
    /// Written as child order, which is what the VBoxContainer lays out.</summary>
    private void Relayout()
    {
        _live.Sort((x, y) => x.Priority != y.Priority ? y.Priority.CompareTo(x.Priority) : y.Seq.CompareTo(x.Seq));
        for (int i = 0; i < _live.Count; i++)
        {
            var line = _live[i].Line;
            if (line == null) continue;
            line.AddThemeColorOverride("font_color", ColourOf(_live[i].Priority));
            MoveChild(line, i);
        }
    }

    // ---- Verification surface: what the stack SHOWS, never a recomputation.

    /// <summary>The toasts on screen now, top to bottom.</summary>
    public List<string> StackTexts()
    {
        Prune(_now());
        var l = new List<string>(_live.Count);
        foreach (var e in _live) l.Add(e.Text);
        return l;
    }

    /// <summary>The priority of the toast on screen with this text, or null.</summary>
    public AlertPriority? PriorityShown(string text)
    {
        Prune(_now());
        foreach (var e in _live) if (e.Text == text) return e.Priority;
        return null;
    }

    public int WaitingCount => _waiting.Count;
    public bool AnyShown => _live.Count > 0;

    /// <summary>Clear the stack and the de-duplication memory, so a stage
    /// starts from a quiet screen whatever the stage before it said.</summary>
    public void ResetForTest()
    {
        foreach (var e in new List<Entry>(_live)) Retire(e);
        _waiting.Clear();
        LastSaid = "";
    }
}
