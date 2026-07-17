using Godot;
using System.Collections.Generic;
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

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _uiPool = new();
    private readonly List<AudioStreamPlayer3D> _positionalPool = new();
    private AudioStreamPlayer _ambientPlayer;

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
    }

    /// <summary>
    /// Discover and preload every WAV in res://audio/. In exported builds the
    /// directory listing shows .import stubs, so those are trimmed back to
    /// their source names before loading.
    /// </summary>
    private void LoadStreams()
    {
        using var dir = DirAccess.Open(AudioDir);
        if (dir == null)
        {
            GD.PushWarning($"AudioDirector: cannot open {AudioDir}; no sounds loaded.");
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

            var stream = ResourceLoader.Load<AudioStream>(AudioDir + name);
            if (stream != null)
                _streams[key] = stream;
            else
                GD.PushWarning($"AudioDirector: failed to load {AudioDir}{name}");
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

    /// <summary>Verification read (TICKET-P5-ALERT-02): did the loader find
    /// this cue? Play answers a missing name with a warning and silence, so a
    /// test that only calls Play proves nothing about the asset existing.</summary>
    public bool Has(string name) => _streams.ContainsKey(name);

    private bool TryGetStream(string name, out AudioStream stream)
    {
        if (_streams.TryGetValue(name, out stream))
            return true;

        GD.PushWarning($"AudioDirector: unknown sound '{name}'");
        return false;
    }
}
