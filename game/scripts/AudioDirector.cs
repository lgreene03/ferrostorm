using Godot;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;   // NotNullWhen: the out stream really is null on the false path
using Ferrostorm.Client;   // AudioBuses (TICKET-P5-SET-01)

namespace Ferrostorm;

/// <summary>
/// AudioDirector: central SFX playback node for Ferrostorm.
///
/// Preloads every WAV under res://audio/ at startup and plays them by short
/// name (file name without extension), e.g. Play("ui_click").
///
/// Pools:
///   - 4 x AudioStreamPlayer   for UI and non-positional sounds (Play)
///   - 32 x AudioStreamPlayer3D for positional battlefield sounds (PlayAt),
///     shared out by family with a cap each (P8-44)
///   - 1 x AudioStreamPlayer   the announcer's channel, one voice line at a
///     time from a priority queue (Announce, P8-44)
///   - 3 x AudioStreamPlayer   music decks for the score playlist (P8-46),
///     which loads its Ogg tracks from res://audio/music/ by name
///
/// Dependency-free: no scene file required. Game code simply does
/// AddChild(new AudioDirector()) and calls the methods below.
/// </summary>
public partial class AudioDirector : Node
{
    private const string AudioDir = "res://audio/";
    private const int UiPoolSize = 4;
    private const float AmbientVolumeDb = -18.0f;
    // TICKET-P6-MUSIC-01: the score's mix positions, kept by P8-46. Calm tracks
    // sit well under the effects (the score is atmosphere, not a lead line);
    // combat tracks rest a little above them. art/audio/synth.py loudness
    // matches each intensity's files to these levels.
    public const float MusicCalmDb = -14.0f;
    public const float MusicCombatMaxDb = -10.0f;
    private const float MusicSilentDb = -60.0f;
    private const float MusicDuckDb = 4.0f;         // calm track duck at full intensity
    public const float CrossfadeSeconds = 1.5f;     // level slew and track crossfade, so every change is musical
    // P8-46: the intensity STATE the playlist follows. The scene's signal snaps
    // to 1 on any exchange of fire and decays over about five seconds; the
    // smoothed level crossing CombatEnterLevel switches to combat at once, and
    // combat holds until the level has sat at zero for CombatHoldSeconds. The
    // hold is new because a track change, unlike the P6 layer's swell, is an
    // event: without it every lull in a firefight would restart the score.
    private const float CombatEnterLevel = 0.5f;
    public const float CombatHoldSeconds = 10.0f;
    private const float QuietLevel = 0.001f;
    private const string MusicDir = "res://audio/music/";

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _uiPool = new();
    private AudioStreamPlayer _ambientPlayer = null!;   // created in _Ready, like every scene field in this codebase
    // P8-46: three decks on the Music bus. One carries the live track; the
    // others carry tracks fading out, so a change that lands mid-crossfade
    // still fades rather than cuts. Two would do for a single change; the
    // third covers an intensity switch landing inside a track-end crossfade.
    private const int MusicDeckCount = 3;
    private readonly ScoreDeck[] _decks = new ScoreDeck[MusicDeckCount];
    private ScoreDeck? _live;
    private readonly Dictionary<string, AudioStream> _score = new();
    private readonly ScorePlaylist _playlist = new();
    private MusicIntensity _musicState = MusicIntensity.Calm;
    private float _quietFor;          // seconds the level has sat at zero while in combat
    private bool _musicOn;
    private float _combatIntensity;   // target 0..1, written by the scene
    private float _combatLevel;       // smoothed level actually on the fader

    // Round-robin cursor for the UI pool; stealing the oldest click is fine.
    // The positional pool steals by family instead (P8-44, PlayAt).
    private int _uiCursor;

    public override void _Ready()
    {
        LoadStreams();

        // TICKET-P5-SET-01: every voice used to play on Master, which is why the
        // settings scene had nothing to hold on to. Each pool now names its own
        // bus, so a slider moves one layer of the mix and not the whole of it.
        AudioBuses.Ensure();

        for (int i = 0; i < UiPoolSize; i++)
        {
            var player = new AudioStreamPlayer { Name = $"UiVoice{i}", Bus = AudioBuses.Ui };
            AddChild(player);
            _uiPool.Add(player);
        }

        for (int i = 0; i < PositionalPoolSize; i++)
        {
            var player = new AudioStreamPlayer3D { Name = $"WorldVoice{i}", Bus = AudioBuses.Sfx };
            AddChild(player);
            _voices.Add(new Voice(player));
        }

        // P8-44: the announcer's own channel, on the Ui bus the voice lines
        // have always played on, so the settings slider still holds them.
        _announcer = new AudioStreamPlayer { Name = "AnnouncerVoice", Bus = AudioBuses.Ui };
        AddChild(_announcer);

        _ambientPlayer = new AudioStreamPlayer { Name = "AmbientVoice", Bus = AudioBuses.Ambient };
        AddChild(_ambientPlayer);

        for (int i = 0; i < MusicDeckCount; i++)
        {
            var player = new AudioStreamPlayer { Name = $"MusicDeck{i}", Bus = AudioBuses.Music };
            AddChild(player);
            _decks[i] = new ScoreDeck(player);
        }
    }

    /// <summary>
    /// Discover and preload every WAV in res://audio/, plus the VO set in
    /// res://audio/vo/ (TICKET-P6-VO-01: the loader was flat and the voice
    /// clips live in their own directory so a re-voicing is one folder swap).
    /// In exported builds the directory listing shows .import stubs, so those
    /// are trimmed back to their source names before loading.
    /// </summary>
    private void LoadStreams()
    {
        LoadStreamDir(AudioDir);
        LoadStreamDir(AudioDir + "vo/");
        LoadScore();
    }

    /// <summary>P8-46: the score loads by NAME from the registry (Score),
    /// not by listing a directory, so a track the playlist expects and the
    /// files lack is a named warning rather than a silently shorter rotation.
    /// The tracks are Ogg Vorbis and stream from memory compressed.</summary>
    private void LoadScore()
    {
        foreach (var (name, _) in Score)
        {
            var stream = ResourceLoader.Load<AudioStream>(MusicDir + name + ".ogg");
            if (stream == null)
            {
                GD.PushWarning($"AudioDirector: failed to load score track {MusicDir}{name}.ogg");
                continue;
            }
            // The playlist owns continuity: a track plays once and hands over
            // to the next before it ends, so a stream must not loop on its own.
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = false;
            _score[name] = stream;
        }
    }

    private void LoadStreamDir(string dirPath)
    {
        using var dir = DirAccess.Open(dirPath);
        if (dir == null)
        {
            GD.PushWarning($"AudioDirector: cannot open {dirPath}; no sounds loaded from it.");
            return;
        }

        dir.ListDirBegin();
        for (string file = dir.GetNext(); file != ""; file = dir.GetNext())
        {
            if (dir.CurrentIsDir())
                continue;

            string name = file;
            if (name.EndsWith(".import"))
                name = name.Substring(0, name.Length - ".import".Length);
            if (!name.EndsWith(".wav"))
                continue;

            string key = name.Substring(0, name.Length - ".wav".Length);
            if (_streams.ContainsKey(key))
                continue;

            var stream = ResourceLoader.Load<AudioStream>(dirPath + name);
            if (stream != null)
                _streams[key] = stream;
            else
                GD.PushWarning($"AudioDirector: failed to load {dirPath}{name}");
        }
        dir.ListDirEnd();
    }

    /// <summary>W3-21: random pitch multiplier, 1 +/- amount, for killing the
    /// machine-gun sameness of rapid selects and massed fire. Client-side
    /// System.Random is legal here; the determinism law binds /sim only
    /// (BattlefieldView header).</summary>
    private static readonly System.Random _sfxRng = new();
    public static float Jitter(float amount) => 1f + ((float)_sfxRng.NextDouble() * 2f - 1f) * amount;

    /// <summary>Play a non-positional sound (UI, orders, alerts) by name.
    /// Pitch is always written so pooled players never inherit a stale
    /// value (W3-21).</summary>
    public void Play(string name, float volumeDb = 0, float pitch = 1f)
    {
        CountRequest(name);
        if (!TryGetStream(name, out var stream) || _uiPool.Count == 0)
            return;

        var player = _uiPool[_uiCursor];
        _uiCursor = (_uiCursor + 1) % _uiPool.Count;

        player.Stop();
        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitch;
        player.Play();
    }

    // ---------------- P8-44: the positional effects pool ----------------
    //
    // FEEL-08. Eight voices taken round robin, so the ninth shot of a fight
    // cut off whatever had played eight sounds ago: a building's explosion,
    // the superweapon's impact, anything. A big fight was a stutter of
    // half-sounds and nothing else could be heard over it. Now 32 voices are
    // shared out by FAMILY, each family with a cap, and the caps sum to the
    // pool. That is the whole guarantee: a family under its cap always finds a
    // free voice, because the others together can hold no more than their own
    // caps, so gunfire can never starve the explosions, the impacts or
    // anything else, and the pool can never run out. A family AT its cap
    // steals from itself: the quietest voice at the listener first (distance,
    // in whole decibels), then the oldest.

    public const int PositionalPoolSize = 32;
    public enum SfxFamily { Gunfire, Explosion, Impact, Other }
    private static readonly int[] FamilyCaps = { 14, 10, 4, 4 };   // sum == PositionalPoolSize
    public static int FamilyCap(SfxFamily f) => FamilyCaps[(int)f];

    /// <summary>The family a positional sound belongs to, by its name: every
    /// weapon report is shot_*, every death and blast explosion_*, death_*
    /// or collapse_*, and the superweapon's impact has a family of its own so
    /// the loudest event in the game is never stolen by a firefight.</summary>
    public static SfxFamily FamilyOf(string name) =>
        name.StartsWith("shot_") ? SfxFamily.Gunfire
        : name.StartsWith("explosion_") || name.StartsWith("death_") || name.StartsWith("collapse_") ? SfxFamily.Explosion
        : name == "superweapon_impact" ? SfxFamily.Impact
        : SfxFamily.Other;

    private sealed class Voice
    {
        public readonly AudioStreamPlayer3D Player;
        public SfxFamily Family;
        public double EndsAt = -1;
        public long Seq;
        public Vector3 At;
        public Voice(AudioStreamPlayer3D player) => Player = player;
    }

    private readonly List<Voice> _voices = new();
    private long _voiceSeq;

    /// <summary>Play a positional battlefield sound at a world position, on a
    /// voice of its family (see above). A voice is busy until its stream's own
    /// length has played at its pitch, a declared end rather than the player's
    /// Playing flag, which a 3D player only raises on its next physics frame.</summary>
    public void PlayAt(string name, Vector3 pos, float pitch = 1f)
    {
        CountRequest(name);
        if (!TryGetStream(name, out var stream) || _voices.Count == 0)
            return;
        double now = Now();
        var family = FamilyOf(name);
        var voice = PickVoice(family, now);
        if (voice.EndsAt > now) StolenVoices++;

        voice.Player.Stop();
        voice.Player.Stream = stream;
        voice.Player.GlobalPosition = pos;
        voice.Player.PitchScale = pitch;
        voice.Player.Play();
        voice.Family = family;
        voice.At = pos;
        voice.Seq = ++_voiceSeq;
        voice.EndsAt = now + stream.GetLength() / Mathf.Max(pitch, 0.05f);

        int all = 0, mine = 0;
        foreach (var v in _voices)
            if (v.EndsAt > now) { all++; if (v.Family == family) mine++; }
        PeakPositionalSounding = System.Math.Max(PeakPositionalSounding, all);
        _peakFamily[(int)family] = System.Math.Max(_peakFamily[(int)family], mine);
    }

    private Voice PickVoice(SfxFamily family, double now)
    {
        int inFamily = 0;
        foreach (var v in _voices) if (v.EndsAt > now && v.Family == family) inFamily++;
        bool atCap = inFamily >= FamilyCap(family);
        if (!atCap)
            foreach (var v in _voices) if (v.EndsAt <= now) return v;
        // At the cap, steal within the family, and only a voice that is still
        // SOUNDING: an idle voice keeps its last family (or the enum's first)
        // and its last position, and taking one would grow the family past its
        // cap while calling it a steal. (Below the cap with no free voice
        // cannot happen while the caps sum to the pool; if it ever did, the
        // quietest sounding voice of any family goes rather than none.)
        Voice? pick = null;
        foreach (var v in _voices)
        {
            if (v.EndsAt <= now || (atCap && v.Family != family)) continue;
            if (pick == null || Quieter(v, pick)) pick = v;
        }
        return pick ?? _voices[0];
    }

    /// <summary>Is `a` a better voice to steal than `b`: quieter at the
    /// listener by a whole decibel or more, or as loud and older.</summary>
    private bool Quieter(Voice a, Voice b)
    {
        float da = Mathf.Floor(AudibilityDb(a)), db = Mathf.Floor(AudibilityDb(b));
        return da != db ? da < db : a.Seq < b.Seq;
    }

    /// <summary>A voice's level at the listener from distance alone, the
    /// player's own inverse-distance law: 0 dB inside its unit size, falling
    /// 6 dB for each doubling beyond. No listener, no difference.</summary>
    private float AudibilityDb(Voice v)
    {
        if (ListenerPosition() is not { } ear) return 0f;
        float unit = Mathf.Max(v.Player.UnitSize, 0.01f);
        float d = Mathf.Max(ear.DistanceTo(v.At), unit);
        return -20f * (float)System.Math.Log10(d / unit);
    }

    private Vector3? _listenerForTest;
    private Vector3? ListenerPosition() =>
        _listenerForTest ?? (IsInsideTree() ? GetViewport()?.GetCamera3D()?.GlobalPosition : null);

    // ---- P8-44 verification surface for the pool: the declared state the
    // pool steals by, never a recomputation of it.
    public int StolenVoices { get; private set; }
    public int PeakPositionalSounding { get; private set; }
    private readonly int[] _peakFamily = new int[4];
    public int PeakFamilySounding(SfxFamily f) => _peakFamily[(int)f];
    public int PositionalVoices => _voices.Count;
    public int PositionalSounding
    {
        get
        {
            double now = Now();
            int n = 0;
            foreach (var v in _voices) if (v.EndsAt > now) n++;
            return n;
        }
    }
    public int FamilySounding(SfxFamily f)
    {
        double now = Now();
        int n = 0;
        foreach (var v in _voices) if (v.EndsAt > now && v.Family == f) n++;
        return n;
    }
    /// <summary>The play order numbers of a family's sounding voices, so a
    /// check can see WHICH were stolen; LastVoiceSeq is the newest.</summary>
    public List<long> SoundingSeqs(SfxFamily f)
    {
        double now = Now();
        var l = new List<long>();
        foreach (var v in _voices) if (v.EndsAt > now && v.Family == f) l.Add(v.Seq);
        l.Sort();
        return l;
    }
    public long LastVoiceSeq => _voiceSeq;
    public void SetListenerForTest(Vector3? at) => _listenerForTest = at;
    /// <summary>Silence the pool and forget its peaks, so a stage starts from
    /// an empty pool whatever the stages before it played.</summary>
    public void ResetPoolForTest()
    {
        foreach (var v in _voices) { v.Player.Stop(); v.EndsAt = -1; }
        StolenVoices = 0;
        PeakPositionalSounding = 0;
        System.Array.Clear(_peakFamily);
    }

    // ---------------- P8-44: the announcer channel ----------------

    /// <summary>The level every voice line has played at (PlayVo's -4).</summary>
    public const float AnnouncerDb = -4f;
    private AudioStreamPlayer _announcer = null!;
    private readonly Announcer _announcerQueue = new();

    /// <summary>Ask the announcer for a voice line at an alert priority. It
    /// plays when the channel is free and nothing more important waits, or
    /// cuts in if it is critical (Announcer). Starting happens in
    /// StepAnnouncer, so lines asked for in the same frame are ranked
    /// together rather than in the order they happened to be asked.</summary>
    public void Announce(string line, AlertPriority priority)
    {
        CountRequest(line);
        if (!TryGetStream(line, out var stream)) return;
        _announcerQueue.Request(line, priority, stream.GetLength(), Now());
    }

    /// <summary>One step of the channel: what _Process calls every frame,
    /// public so the harness can move it on its own clock.</summary>
    public void StepAnnouncer()
    {
        var start = _announcerQueue.Advance(Now(), out bool cut);
        if (cut) _announcer.Stop();
        if (start == null || !_streams.TryGetValue(start, out var stream)) return;
        _announcer.Stop();
        _announcer.Stream = stream;
        _announcer.VolumeDb = AnnouncerDb;
        _announcer.PitchScale = 1f;
        _announcer.Play();
    }

    /// <summary>P8-44 verification surface: the queue itself, read only.</summary>
    public Announcer AnnouncerView => _announcerQueue;
    /// <summary>Players of any kind carrying a voice line (vo_*) and playing
    /// right now, read off the players: one channel means never more than one.</summary>
    public int VoiceLinesSounding
    {
        get
        {
            int n = 0;
            bool Vo(AudioStream? s)
            {
                if (s == null) return false;
                foreach (var (name, st) in _streams) if (st == s) return name.StartsWith("vo_");
                return false;
            }
            if (_announcer.Playing && Vo(_announcer.Stream)) n++;
            foreach (var p in _uiPool) if (p.Playing && Vo(p.Stream)) n++;
            return n;
        }
    }
    public void ResetAnnouncerForTest()
    {
        _announcer.Stop();
        _announcerQueue.Reset();
    }

    // ---- P8-44: one clock for the pool and the announcer. Wall time in play;
    // the harness hands in its own so a stage can freeze or step time.
    private System.Func<double>? _clockForTest;
    private double Now() => _clockForTest?.Invoke() ?? Time.GetTicksMsec() / 1000.0;
    public void SetClockForTest(System.Func<double>? clock) => _clockForTest = clock;

    /// <summary>
    /// Start the looping ambient wind bed at low volume. The WAV itself is
    /// authored to loop seamlessly; looping is enforced here in case the
    /// import settings did not mark it as a loop.
    /// </summary>
    public void PlayAmbient()
    {
        if (!TryGetStream("ambient_wind", out var stream))
            return;

        if (stream is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            // 16-bit mono: two bytes per frame.
            wav.LoopEnd = wav.Data.Length / 2;
        }

        _ambientPlayer.Stream = stream;
        _ambientPlayer.VolumeDb = AmbientVolumeDb;
        _ambientPlayer.Play();
    }

    /// <summary>Stop the ambient bed, if playing.</summary>
    public void StopAmbient()
    {
        _ambientPlayer?.Stop();
    }

    // ---------------- P8-46: the score, an intensity playlist ----------------
    //
    // TICKET-P6-MUSIC-01 shipped two 64-second loops, a calm bed and a combat
    // layer crossfaded over it, so a 30-minute match heard each about 28
    // times. Decision D26 replaces them with an interim procedural score of
    // six tracks of at least three minutes (art/audio/synth.py), played as a
    // playlist per intensity: the next track is drawn from a shuffled bag of
    // that intensity's tracks, so the same track never plays twice in a row,
    // and every change, whether a track ending or the intensity moving,
    // crossfades over CrossfadeSeconds on the P6 equal-power curve.

    /// <summary>The registered score: the file name under res://audio/music/
    /// and the intensity it plays at. It mirrors SCORE in art/audio/synth.py,
    /// which generates the files, and the client harness's scoregate fails if
    /// a registered track does not load or runs under three minutes.</summary>
    public static readonly (string Name, MusicIntensity Intensity)[] Score =
    {
        ("calm_ferrite_dawn", MusicIntensity.Calm),
        ("calm_slag_hollow", MusicIntensity.Calm),
        ("calm_watchfire", MusicIntensity.Calm),
        ("combat_seven_hammers", MusicIntensity.Combat),
        ("combat_gantry_run", MusicIntensity.Combat),
        ("combat_five_furnaces", MusicIntensity.Combat),
    };

    public static MusicIntensity IntensityOf(string track)
    {
        foreach (var (name, intensity) in Score)
            if (name == track) return intensity;
        return MusicIntensity.Calm;
    }

    /// <summary>Start the score: calm, the first track of a fresh shuffle
    /// straight in at its resting level, as the P6 calm bed started.</summary>
    public void PlayMusic()
    {
        foreach (var deck in _decks) deck.Clear();
        _live = null;
        _combatIntensity = 0f;
        _combatLevel = 0f;
        _quietFor = 0f;
        _musicState = MusicIntensity.Calm;
        _musicOn = _score.Count > 0;
        if (!_musicOn)
        {
            GD.PushWarning("AudioDirector: no score track loaded; the battle plays without music.");
            return;
        }
        BeginTrack(MusicIntensity.Calm);
        if (_live != null) _live.Level = 1f;   // the opening track does not fade in
        ApplyDeckVolumes();
    }

    public void StopMusic()
    {
        _musicOn = false;
        foreach (var deck in _decks) deck.Clear();
        _live = null;
    }

    /// <summary>The scene's combat-intensity signal, 0..1. The director only
    /// smooths it and turns it into a state; deciding what counts as combat
    /// is the scene's job.</summary>
    public void SetCombatIntensity(float v) => _combatIntensity = Mathf.Clamp(v, 0f, 1f);

    /// <summary>Reseed the playlist's shuffle. A match draws from an
    /// unseeded shuffle; the client harness seeds it so it can assert on the
    /// order. The next PlayMusic starts from the new seed.</summary>
    public void SeedScore(int seed) => _playlist.Reseed(seed);

    /// <summary>Hand over to the next track of the current intensity,
    /// crossfaded. This is the call a track nearing its end makes, public so
    /// the harness can drive twenty changes without waiting an hour.</summary>
    public void NextScoreTrack()
    {
        if (_musicOn) BeginTrack(_musicState);
    }

    public override void _Process(double delta)
    {
        StepMusic(delta);
        StepAnnouncer();   // P8-44
    }

    /// <summary>One frame of the score: smooth the intensity, move the state,
    /// hand over a track that is about to end, and slew every deck toward its
    /// target. _Process calls it with the frame time; the harness calls it
    /// with whatever time it wants to have passed.</summary>
    public void StepMusic(double delta)
    {
        if (!_musicOn) return;
        float d = (float)delta;
        _combatLevel = Mathf.MoveToward(_combatLevel, _combatIntensity, d / CrossfadeSeconds);

        if (_musicState == MusicIntensity.Calm)
        {
            if (_combatLevel >= CombatEnterLevel)
            {
                _musicState = MusicIntensity.Combat;
                _quietFor = 0f;
                BeginTrack(MusicIntensity.Combat);
            }
        }
        else
        {
            _quietFor = _combatLevel > QuietLevel ? 0f : _quietFor + d;
            if (_quietFor >= CombatHoldSeconds)
            {
                _musicState = MusicIntensity.Calm;
                BeginTrack(MusicIntensity.Calm);
            }
        }

        // A track hands over CrossfadeSeconds before its end, so the next one
        // is fully up as it finishes. A deck seen playing that has stopped
        // without handing over (a frame hitch longer than the crossfade) is
        // replaced too, rather than leaving the battle silent.
        if (_live != null)
        {
            var p = _live.Player;
            if (p.Playing)
            {
                _live.SeenPlaying = true;
                double length = p.Stream?.GetLength() ?? 0.0;
                if (length > 2.0 * CrossfadeSeconds && p.GetPlaybackPosition() >= length - CrossfadeSeconds)
                    BeginTrack(_musicState);
            }
            else if (_live.SeenPlaying)
            {
                BeginTrack(_musicState);
            }
        }

        float step = d / CrossfadeSeconds;
        foreach (var deck in _decks)
        {
            if (deck.Track == null) continue;
            deck.Level = Mathf.MoveToward(deck.Level, deck == _live ? 1f : 0f, step);
            if (deck != _live && deck.Level <= 0f) deck.Clear();
        }
        ApplyDeckVolumes();
    }

    /// <summary>Start the next track of `intensity` on a free deck and make
    /// it live; the deck that was live starts fading. With every deck busy,
    /// the quietest fading one is cut, which is at most a crossfade from
    /// silent anyway.</summary>
    private void BeginTrack(MusicIntensity intensity)
    {
        string next = _playlist.Next(intensity);
        if (!_score.TryGetValue(next, out var stream)) return;   // warned at load; keep what plays
        ScoreDeck? deck = null;
        foreach (var candidate in _decks)
            if (candidate.Track == null) { deck = candidate; break; }
        if (deck == null)
        {
            foreach (var candidate in _decks)
                if (candidate != _live && (deck == null || candidate.Level < deck.Level)) deck = candidate;
            deck!.Clear();
        }
        deck.Track = next;
        deck.Level = 0f;
        deck.Player.Stream = stream;
        deck.Player.VolumeDb = MusicSilentDb;
        deck.Player.Play();
        _live = deck;
    }

    /// <summary>The P6 fader curve, per deck: the square root of the fade
    /// level into dB (equal power, so a crossfade holds the sum level), on
    /// top of the track's intensity resting level. A calm track still ducks
    /// as the intensity rises, as the P6 calm bed did.</summary>
    private void ApplyDeckVolumes()
    {
        foreach (var deck in _decks)
        {
            if (deck.Track == null) continue;
            float rest = IntensityOf(deck.Track) == MusicIntensity.Combat
                ? MusicCombatMaxDb
                : MusicCalmDb - MusicDuckDb * _combatLevel;
            deck.Player.VolumeDb = deck.Level <= QuietLevel
                ? MusicSilentDb
                : Mathf.Max(MusicSilentDb, rest + Mathf.LinearToDb(Mathf.Sqrt(deck.Level)));
        }
    }

    /// <summary>Verification read (TICKET-P5-ALERT-02): did the loader find
    /// this cue? Play answers a missing name with a warning and silence, so a
    /// test that only calls Play proves nothing about the asset existing.</summary>
    public bool Has(string name) => _streams.ContainsKey(name);
    /// <summary>P8-44 verification read: a loaded sound's own length in
    /// seconds, 0 when absent.</summary>
    public double SoundSeconds(string name) => _streams.TryGetValue(name, out var s) ? s.GetLength() : 0.0;

    // P8-10: how many times each SFX name was ASKED for, through Play or
    // PlayAt, before the pool or the asset had any say. A request is the
    // client's decision to make a sound, which is what a rule such as "the
    // production chime is the local seat's alone" is about; whether a pooled
    // player was free to voice it is a separate question. Never read in play.
    private readonly Dictionary<string, int> _requests = new();
    private void CountRequest(string name) => _requests[name] = _requests.GetValueOrDefault(name) + 1;
    /// <summary>Verification read: requests for this SFX name so far.</summary>
    public int PlayRequests(string name) => _requests.GetValueOrDefault(name);

    // ---- TICKET-P6-MUSIC-01 / TICKET-P6-VO-01 verification reads: the state
    // of the shipped players, never a recomputation of it. P8-46 kept the four
    // music reads' meaning across the change from a layer pair to a playlist:
    // "calm playing" is the live deck carrying a calm track, and a volume is
    // that of the loudest deck carrying a track of that intensity.
    public bool MusicCalmPlaying => LiveIs(MusicIntensity.Calm);
    public bool MusicCombatPlaying => LiveIs(MusicIntensity.Combat);
    public float MusicCombatVolumeDb => LoudestDb(MusicIntensity.Combat);
    public float MusicCalmVolumeDb => LoudestDb(MusicIntensity.Calm);
    /// <summary>The track on the live deck, or null with the score stopped.</summary>
    public string? MusicNowPlaying => _live?.Track;
    public MusicIntensity MusicState => _musicState;
    /// <summary>Decks carrying a track: 2 or more mid-crossfade, 1 otherwise.</summary>
    public int MusicDecksSounding
    {
        get
        {
            int n = 0;
            foreach (var deck in _decks) if (deck.Track != null && deck.Player.Playing) n++;
            return n;
        }
    }
    public bool ScoreTrackLoaded(string name) => _score.ContainsKey(name);
    /// <summary>The loaded stream's own length, seconds (0 when absent).</summary>
    public double ScoreTrackSeconds(string name) => _score.TryGetValue(name, out var s) ? s.GetLength() : 0.0;

    private bool LiveIs(MusicIntensity intensity) =>
        _live is { Track: { } t } && _live.Player.Playing && IntensityOf(t) == intensity;

    private float LoudestDb(MusicIntensity intensity)
    {
        float db = MusicSilentDb;
        foreach (var deck in _decks)
            if (deck.Track != null && deck.Player.Playing && IntensityOf(deck.Track) == intensity)
                db = Mathf.Max(db, deck.Player.VolumeDb);
        return db;
    }
    /// <summary>Is the announcer's channel actually carrying this named stream
    /// right now? This is the read that proves a VO line reached a live player
    /// with the right clip, not merely that it was asked for. (P8-44: the
    /// lines left the UI pool for the channel.)</summary>
    public bool IsVoicePlaying(string name) =>
        _streams.TryGetValue(name, out var s) && _announcer.Playing && _announcer.Stream == s;

    private bool TryGetStream(string name, [NotNullWhen(true)] out AudioStream? stream)
    {
        if (_streams.TryGetValue(name, out stream))
            return true;

        GD.PushWarning($"AudioDirector: unknown sound '{name}'");
        return false;
    }

    /// <summary>P8-46: one music player and what it carries. Level is the
    /// crossfade position, 0 silent to 1 at the track's resting level.</summary>
    private sealed class ScoreDeck
    {
        public readonly AudioStreamPlayer Player;
        public string? Track;
        public float Level;
        public bool SeenPlaying;

        public ScoreDeck(AudioStreamPlayer player) => Player = player;

        public void Clear()
        {
            Player.Stop();
            Player.Stream = null;
            Track = null;
            Level = 0f;
            SeenPlaying = false;
        }
    }
}

/// <summary>P8-46: the two intensity states the score follows.</summary>
public enum MusicIntensity { Calm, Combat }

/// <summary>
/// P8-46: which score track plays next. Each intensity draws from its own
/// shuffled bag, so every track of an intensity plays once before any plays
/// again, and a fresh bag never opens on the track that closed the last one:
/// together, the same track never plays twice in a row. Client-side
/// System.Random is legal here; the determinism law binds /sim only. A match
/// is unseeded, so the order differs between matches; the harness seeds it.
/// </summary>
public sealed class ScorePlaylist
{
    private System.Random _rng = new();
    private readonly Dictionary<MusicIntensity, List<string>> _bags = new();
    private readonly Dictionary<MusicIntensity, string> _last = new();

    public void Reseed(int seed)
    {
        _rng = new System.Random(seed);
        _bags.Clear();
        _last.Clear();
    }

    public string Next(MusicIntensity intensity)
    {
        if (!_bags.TryGetValue(intensity, out var bag) || bag.Count == 0)
        {
            bag = new List<string>();
            foreach (var (name, i) in AudioDirector.Score)
                if (i == intensity) bag.Add(name);
            for (int k = bag.Count - 1; k > 0; k--)
            {
                int j = _rng.Next(k + 1);
                (bag[k], bag[j]) = (bag[j], bag[k]);
            }
            // The one rule a shuffle alone cannot keep: a new bag must not
            // open on the track the old one closed with.
            if (bag.Count > 1 && _last.TryGetValue(intensity, out var last) && bag[0] == last)
            {
                int j = 1 + _rng.Next(bag.Count - 1);
                (bag[0], bag[j]) = (bag[j], bag[0]);
            }
            _bags[intensity] = bag;
        }
        string next = bag[0];
        bag.RemoveAt(0);
        _last[intensity] = next;
        return next;
    }
}
