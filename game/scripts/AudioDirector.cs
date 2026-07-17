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
///   - 8 x AudioStreamPlayer3D for positional battlefield sounds (PlayAt)
///
/// Dependency-free: no scene file required. Game code simply does
/// AddChild(new AudioDirector()) and calls the methods below.
/// </summary>
public partial class AudioDirector : Node
{
    private const string AudioDir = "res://audio/";
    private const int UiPoolSize = 4;
    private const int PositionalPoolSize = 8;
    private const float AmbientVolumeDb = -18.0f;
    // TICKET-P6-MUSIC-01: the score's mix positions. The calm bed sits well
    // under the effects (the score is atmosphere, not a lead line); the combat
    // layer tops out a little above it and fades to silence at intensity 0.
    private const float MusicCalmDb = -14.0f;
    private const float MusicCombatMaxDb = -10.0f;
    private const float MusicSilentDb = -60.0f;
    private const float MusicDuckDb = 4.0f;         // calm bed duck at full intensity
    private const float CrossfadeSeconds = 1.5f;    // level slew, so the swell is musical

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _uiPool = new();
    private readonly List<AudioStreamPlayer3D> _positionalPool = new();
    private AudioStreamPlayer _ambientPlayer = null!;   // created in _Ready, like every scene field in this codebase
    // TICKET-P6-MUSIC-01: the music player pair, both on the Music bus. The
    // pair plays in lockstep (same length loops, started together) and the
    // crossfade is a volume move, never a seek, so the beds stay bar-aligned.
    private AudioStreamPlayer _musicCalm = null!;
    private AudioStreamPlayer _musicCombat = null!;
    private float _combatIntensity;   // target 0..1, written by the scene
    private float _combatLevel;       // smoothed level actually on the fader

    // Round-robin cursors; stealing the oldest voice is acceptable for RTS SFX.
    private int _uiCursor;
    private int _positionalCursor;

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
            _positionalPool.Add(player);
        }

        _ambientPlayer = new AudioStreamPlayer { Name = "AmbientVoice", Bus = AudioBuses.Ambient };
        AddChild(_ambientPlayer);

        _musicCalm = new AudioStreamPlayer { Name = "MusicCalm", Bus = AudioBuses.Music };
        AddChild(_musicCalm);
        _musicCombat = new AudioStreamPlayer { Name = "MusicCombat", Bus = AudioBuses.Music };
        AddChild(_musicCombat);
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

    /// <summary>Play a positional battlefield sound at a world position.</summary>
    public void PlayAt(string name, Vector3 pos, float pitch = 1f)
    {
        if (!TryGetStream(name, out var stream) || _positionalPool.Count == 0)
            return;

        var player = _positionalPool[_positionalCursor];
        _positionalCursor = (_positionalCursor + 1) % _positionalPool.Count;

        player.Stop();
        player.Stream = stream;
        player.GlobalPosition = pos;
        player.PitchScale = pitch;
        player.Play();
    }

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

    // ---------------- TICKET-P6-MUSIC-01: the score ----------------

    /// <summary>Loop a WAV whose import settings did not mark it as a loop -
    /// the PlayAmbient rule, needed by both music beds.</summary>
    private static void EnsureLooped(AudioStream stream)
    {
        if (stream is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            // 16-bit mono: two bytes per frame.
            wav.LoopEnd = wav.Data.Length / 2;
        }
    }

    /// <summary>Start the two-layer score: the calm bed at its resting level
    /// and the combat layer silent underneath it, both looping, started
    /// together so they stay bar-aligned (they are authored the same length).</summary>
    public void PlayMusic()
    {
        if (!TryGetStream("music_calm", out var calm) || !TryGetStream("music_combat", out var combat))
            return;
        EnsureLooped(calm);
        EnsureLooped(combat);
        _combatIntensity = 0f;
        _combatLevel = 0f;
        _musicCalm.Stream = calm;
        _musicCalm.VolumeDb = MusicCalmDb;
        _musicCalm.Play();
        _musicCombat.Stream = combat;
        _musicCombat.VolumeDb = MusicSilentDb;
        _musicCombat.Play();
    }

    public void StopMusic()
    {
        _musicCalm?.Stop();
        _musicCombat?.Stop();
    }

    /// <summary>The scene's combat-intensity signal, 0..1. The director only
    /// smooths it onto the faders; deciding what counts as combat is the
    /// scene's job.</summary>
    public void SetCombatIntensity(float v) => _combatIntensity = Mathf.Clamp(v, 0f, 1f);

    /// <summary>The crossfade. Level slews toward the intensity target over
    /// CrossfadeSeconds; the combat fader follows an equal-power-ish curve
    /// (square root into dB) so a low intensity is already audible tension
    /// rather than nothing, and the calm bed ducks a little as the combat
    /// layer rises so the sum stays level.</summary>
    public override void _Process(double delta)
    {
        if (_musicCombat is not { Playing: true }) return;
        _combatLevel = Mathf.MoveToward(_combatLevel, _combatIntensity, (float)delta / CrossfadeSeconds);
        _musicCombat.VolumeDb = _combatLevel <= 0.001f
            ? MusicSilentDb
            : Mathf.Max(MusicSilentDb, MusicCombatMaxDb + Mathf.LinearToDb(Mathf.Sqrt(_combatLevel)));
        _musicCalm.VolumeDb = MusicCalmDb - MusicDuckDb * _combatLevel;
    }

    /// <summary>Verification read (TICKET-P5-ALERT-02): did the loader find
    /// this cue? Play answers a missing name with a warning and silence, so a
    /// test that only calls Play proves nothing about the asset existing.</summary>
    public bool Has(string name) => _streams.ContainsKey(name);

    // ---- TICKET-P6-MUSIC-01 / TICKET-P6-VO-01 verification reads: the state
    // of the shipped players, never a recomputation of it.
    public bool MusicCalmPlaying => _musicCalm is { Playing: true };
    public bool MusicCombatPlaying => _musicCombat is { Playing: true };
    public float MusicCombatVolumeDb => _musicCombat?.VolumeDb ?? MusicSilentDb;
    public float MusicCalmVolumeDb => _musicCalm?.VolumeDb ?? MusicSilentDb;
    /// <summary>Is a UI-pool player actually carrying this named stream right
    /// now? This is the read that proves a VO line reached a live player with
    /// the right clip, not merely that Play was called with a string.</summary>
    public bool IsVoicePlaying(string name)
    {
        if (!_streams.TryGetValue(name, out var s)) return false;
        foreach (var p in _uiPool)
            if (p.Playing && p.Stream == s) return true;
        return false;
    }

    private bool TryGetStream(string name, [NotNullWhen(true)] out AudioStream? stream)
    {
        if (_streams.TryGetValue(name, out stream))
            return true;

        GD.PushWarning($"AudioDirector: unknown sound '{name}'");
        return false;
    }
}
