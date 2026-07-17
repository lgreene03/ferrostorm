#!/usr/bin/env python3
"""Ferrostorm procedural SFX synthesiser.

Renders the game's first SFX set as 16-bit 44.1 kHz mono WAV files into
game/audio/. Pure standard library (wave, math, array, random); no numpy,
no downloaded assets. Every sound is an original synthesis recipe and
deliberately avoids any resemblance to C&C or EVA-style audio motifs.

Each file is normalised to a peak of -3 dBFS and the script prints the
RMS level of every rendered file so silence or clipping is obvious.

Run:  python3 art/audio/synth.py
"""

import array
import math
import os
import random
import wave

SR = 44100                       # sample rate, Hz
PEAK_TARGET = 10.0 ** (-3.0 / 20.0)  # -3 dBFS linear peak (~0.708)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "..", "game", "audio"))


# ---------------------------------------------------------------------------
# Small DSP toolkit
# ---------------------------------------------------------------------------

def silence(duration):
    """A buffer of zeros lasting `duration` seconds."""
    return [0.0] * int(SR * duration)


def white_noise(duration, rng):
    """Uniform white noise in [-1, 1]."""
    n = int(SR * duration)
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def brown_noise(duration, rng, leak=0.998):
    """Brownian (red) noise: leaky integral of white noise.

    The leak keeps the random walk from wandering off to DC, which would
    otherwise thump the speaker when the file starts.
    """
    n = int(SR * duration)
    out = []
    acc = 0.0
    for _ in range(n):
        acc = acc * leak + rng.uniform(-1.0, 1.0) * 0.02
        out.append(acc)
    peak = max(abs(s) for s in out) or 1.0
    return [s / peak for s in out]


def sine_sweep(duration, f0, f1, curve=1.0):
    """Sine whose frequency moves from f0 to f1 over the buffer.

    curve > 1 spends longer near f0 before sweeping; curve < 1 moves early.
    Phase-accumulated, so the sweep stays click-free.
    """
    n = int(SR * duration)
    out = []
    phase = 0.0
    for i in range(n):
        t = (i / n) ** curve
        freq = f0 + (f1 - f0) * t
        phase += 2.0 * math.pi * freq / SR
        out.append(math.sin(phase))
    return out


def sine(duration, freq, phase=0.0):
    """Plain constant-frequency sine."""
    return sine_sweep(duration, freq, freq) if phase == 0.0 else [
        math.sin(phase + 2.0 * math.pi * freq * i / SR)
        for i in range(int(SR * duration))
    ]


def band_pass(samples, centre, q=2.0):
    """State-variable band-pass filter (Chamberlin form).

    `centre` is either a constant Hz value or a callable index -> Hz so
    generators can sweep the filter. q controls how narrow the band is.
    """
    low = band = 0.0
    damp = 1.0 / max(q, 0.1)
    out = []
    fixed = None if callable(centre) else centre
    for i, s in enumerate(samples):
        fc = fixed if fixed is not None else centre(i)
        fc = min(max(fc, 10.0), SR / 6.5)  # keep the SVF stable
        f = 2.0 * math.sin(math.pi * fc / SR)
        low += f * band
        high = s - low - damp * band
        band += f * high
        out.append(band)
    return out


def low_pass(samples, cutoff):
    """One-pole low-pass; cutoff is a constant Hz or a callable index -> Hz."""
    out = []
    y = 0.0
    fixed = None if callable(cutoff) else cutoff
    for i, s in enumerate(samples):
        fc = fixed if fixed is not None else cutoff(i)
        a = 1.0 - math.exp(-2.0 * math.pi * max(fc, 5.0) / SR)
        y += a * (s - y)
        out.append(y)
    return out


def exp_decay(samples, tau, attack=0.002):
    """Fast linear attack then exponential decay with time constant `tau`."""
    n_attack = max(1, int(SR * attack))
    out = []
    for i, s in enumerate(samples):
        env = (i / n_attack) if i < n_attack else math.exp(-(i - n_attack) / (tau * SR))
        out.append(s * env)
    return out


def envelope(samples, points):
    """Piecewise-linear envelope. `points` is [(time_fraction, gain), ...]."""
    n = len(samples)
    out = []
    for i, s in enumerate(samples):
        x = i / max(n - 1, 1)
        g = points[-1][1]
        for (x0, g0), (x1, g1) in zip(points, points[1:]):
            if x0 <= x <= x1:
                span = (x1 - x0) or 1.0
                g = g0 + (g1 - g0) * (x - x0) / span
                break
        out.append(s * g)
    return out


def mix(*layers):
    """Sum layers of possibly different lengths, padded with silence."""
    n = max(len(layer) for layer in layers)
    out = [0.0] * n
    for layer in layers:
        for i, s in enumerate(layer):
            out[i] += s
    return out


def gain(samples, g):
    return [s * g for s in samples]


def edge_fade(samples, fade_in=0.001, fade_out=0.01):
    """Short linear fades at both ends so nothing clicks in the mix."""
    n = len(samples)
    ni = max(1, int(SR * fade_in))
    no = max(1, int(SR * fade_out))
    out = list(samples)
    for i in range(min(ni, n)):
        out[i] *= i / ni
    for i in range(min(no, n)):
        out[n - 1 - i] *= i / no
    return out


def normalise(samples, peak=PEAK_TARGET):
    """Scale so the absolute peak sits exactly at -3 dBFS. No clipping."""
    m = max(abs(s) for s in samples) or 1.0
    return [s * peak / m for s in samples]


def rms_db(samples):
    acc = sum(s * s for s in samples)
    r = math.sqrt(acc / len(samples))
    return 20.0 * math.log10(r) if r > 0 else float("-inf")


def write_wav(name, samples):
    samples = normalise(samples)
    path = os.path.join(OUT_DIR, name)
    data = array.array("h", (int(round(s * 32767)) for s in samples))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print("%-24s %6.0f ms   RMS %6.1f dBFS" % (
        name, 1000.0 * len(samples) / SR, rms_db(samples)))
    return path


# ---------------------------------------------------------------------------
# Sound generators (one per required asset, each with its own seed)
# ---------------------------------------------------------------------------

def ui_click():
    """Soft filtered tick (~60 ms): a narrow band-passed noise burst layered
    with a tiny sine blip that drops in pitch. Aiming for a dry, papery
    interface tick that never fatigues at high click rates."""
    rng = random.Random(101)
    noise = band_pass(white_noise(0.06, rng), 2200.0, q=6.0)
    noise = exp_decay(noise, tau=0.010, attack=0.001)
    blip = exp_decay(sine_sweep(0.05, 1500.0, 700.0), tau=0.012, attack=0.001)
    return edge_fade(mix(noise, gain(blip, 0.5)))


def ui_confirm():
    """Two ascending clean blips (~120 ms): a perfect fourth up, each blip a
    near-pure sine with a whisper of second harmonic. Aiming for a tidy,
    positive 'accepted' cue that reads even on laptop speakers."""
    def blip(freq, dur):
        tone = mix(sine_sweep(dur, freq, freq),
                   gain(sine_sweep(dur, freq * 2.0, freq * 2.0), 0.18))
        return exp_decay(tone, tau=0.030, attack=0.002)
    first = blip(620.0, 0.055)
    second = blip(830.0, 0.065)
    out = first + silence(0.004) + second
    return edge_fade(out)


def order_move():
    """Quick low blip (~100 ms): a single round sine around 300 Hz with a
    slight downward settle. Aiming for a neutral, unobtrusive 'order
    received' acknowledgement that stays below the combat frequencies."""
    tone = sine_sweep(0.10, 340.0, 285.0, curve=0.7)
    body = exp_decay(tone, tau=0.035, attack=0.003)
    knock = exp_decay(sine_sweep(0.03, 700.0, 500.0), tau=0.006, attack=0.001)
    return edge_fade(mix(body, gain(knock, 0.25)))


def shot_rifle():
    """Sharp noise crack (~120 ms): bright band-passed white noise with a
    very fast exponential decay plus a mid snap. Aiming for a dry, small
    calibre report that can repeat rapidly without smearing the mix."""
    rng = random.Random(104)
    crack = band_pass(white_noise(0.12, rng), 3400.0, q=1.2)
    crack = exp_decay(crack, tau=0.018, attack=0.0005)
    snap = band_pass(white_noise(0.05, rng), 1200.0, q=2.5)
    snap = exp_decay(snap, tau=0.010, attack=0.0005)
    return edge_fade(mix(crack, gain(snap, 0.6)))


def shot_cannon():
    """Deeper boom (~350 ms): low sine thump sweeping 110 to 45 Hz under a
    darker noise layer with a longer tail. Aiming for weighty vehicle
    artillery that contrasts clearly with the rifle crack."""
    rng = random.Random(105)
    thump = exp_decay(sine_sweep(0.35, 110.0, 45.0, curve=0.6),
                      tau=0.090, attack=0.002)
    blast = band_pass(white_noise(0.30, rng), 900.0, q=0.8)
    blast = exp_decay(blast, tau=0.060, attack=0.001)
    return edge_fade(mix(thump, gain(blast, 0.55)), fade_out=0.03)


def shot_rocket():
    """Whoosh (~500 ms): white noise pushed through a band-pass whose centre
    rises fast then falls away, with the level swelling and dying to match.
    Aiming for a rocket leaving the rail rather than a jet flyby."""
    rng = random.Random(106)
    n = int(SR * 0.5)

    def centre(i):
        t = i / n
        if t < 0.35:
            return 350.0 + (2600.0 - 350.0) * (t / 0.35)
        return 2600.0 - (2600.0 - 500.0) * ((t - 0.35) / 0.65)

    woosh = band_pass(white_noise(0.5, rng), centre, q=1.6)
    woosh = envelope(woosh, [(0.0, 0.0), (0.12, 1.0), (0.45, 0.8), (1.0, 0.0)])
    return edge_fade(woosh, fade_out=0.02)


def explosion_small():
    """Brown-noise burst (~600 ms): exponential decay with a 65 Hz sub thump
    underneath. Aiming for a compact infantry-scale detonation that sits
    between the cannon shot and the large explosion."""
    rng = random.Random(107)
    body = exp_decay(brown_noise(0.60, rng), tau=0.130, attack=0.001)
    sub = exp_decay(sine_sweep(0.30, 70.0, 48.0, curve=0.7),
                    tau=0.080, attack=0.002)
    return edge_fade(mix(body, gain(sub, 0.7)), fade_out=0.04)


def explosion_large():
    """Layered blast (~1.4 s): two brown-noise layers (dark rumble plus a
    brighter initial burst) over a sub-bass sweep from 90 down to 35 Hz.
    Aiming for a building-killer with a long, settling tail."""
    rng = random.Random(108)
    rumble = exp_decay(low_pass(brown_noise(1.4, rng), 500.0),
                       tau=0.380, attack=0.002)
    burst = band_pass(white_noise(0.5, rng), 1400.0, q=0.7)
    burst = exp_decay(burst, tau=0.070, attack=0.001)
    sub = exp_decay(sine_sweep(1.0, 90.0, 35.0, curve=0.8),
                    tau=0.300, attack=0.003)
    out = mix(rumble, gain(burst, 0.45), gain(sub, 0.8))
    return edge_fade(out, fade_out=0.10)


def alert_attack():
    """Urgent two-tone klaxon (~700 ms): four short pulses alternating a
    minor third (D5 down to B4), each pulse a sine with a bite of third
    harmonic and a hard gate. Deliberately NOT a sweeping siren; the
    interval and pulse rhythm are original to Ferrostorm."""
    hi, lo = 587.33, 493.88  # D5 and B4, a minor third apart
    pulse_dur, gap = 0.13, 0.045
    out = []
    for k in range(4):
        freq = hi if k % 2 == 0 else lo
        tone = mix(sine_sweep(pulse_dur, freq, freq),
                   gain(sine_sweep(pulse_dur, freq * 3.0, freq * 3.0), 0.22))
        tone = envelope(tone, [(0.0, 0.0), (0.05, 1.0), (0.75, 0.9), (1.0, 0.0)])
        out += tone + silence(gap)
    return edge_fade(out, fade_out=0.02)


def alert_harvester():
    """Rising two-blip motif (~400 ms): two gated pulses a perfect fifth
    apart (A4 up to E5), each with a slight upward chirp inside it and a
    touch of second harmonic. Urgent but lighter than alert_attack, and
    shaped to be distinct at a glance: the klaxon alternates DOWN a minor
    third four times, this steps UP a fifth once. GDD s7 line 85 wants
    each alert audibly its own; the harvester alert leaned on a pitch
    shift of the klaxon until this cue existed."""
    def blip(f0, f1, dur):
        tone = mix(sine_sweep(dur, f0, f1),
                   gain(sine_sweep(dur, f0 * 2.0, f1 * 2.0), 0.20))
        return envelope(tone, [(0.0, 0.0), (0.06, 1.0), (0.7, 0.85), (1.0, 0.0)])
    first = blip(440.0, 466.0, 0.12)
    second = blip(659.26, 698.0, 0.16)
    return edge_fade(first + silence(0.055) + second, fade_out=0.02)


def alert_low_power():
    """Sagging descent (~900 ms): a tone that starts near 280 Hz, holds
    briefly, then droops to 110 Hz with a detuned partner beating against
    it and a quiet octave-down sub underneath - the sound of something
    winding down rather than an alarm going off. Replaces the 0.82 pitch
    shift of alert_attack that stood in for a real cue (GDD s7 line 85)."""
    sag = sine_sweep(0.9, 280.0, 110.0, curve=1.7)
    detune = sine_sweep(0.9, 285.0, 113.0, curve=1.7)
    sub = sine_sweep(0.9, 140.0, 55.0, curve=1.7)
    out = mix(sag, gain(detune, 0.55), gain(sub, 0.4))
    out = envelope(out, [(0.0, 0.0), (0.05, 1.0), (0.65, 0.8), (1.0, 0.0)])
    return edge_fade(out, fade_out=0.05)


def alert_radar():
    """Signal-loss cue (~750 ms): a steady high carrier with a whisper of
    octave-up harmonic holds for a fifth of a second, snaps off, and
    fractures into band-passed static that decays to nothing, with a soft
    low thump under the break. The sound of a feed dropping out rather
    than an alarm going off - deliberately the opposite register from
    alert_low_power's sagging tone, because losing the radar picture and
    losing production speed are different bad news. Completes GDD s7 line
    85's alert set ("radar goes dark", ADR-008 clause 4 / ALERT-02's last
    clause). Original synthesis; no resemblance to EVA-style audio."""
    rng = random.Random(112)
    carrier = mix(sine_sweep(0.20, 932.0, 932.0),
                  gain(sine_sweep(0.20, 1864.0, 1864.0), 0.18))
    carrier = envelope(carrier, [(0.0, 0.0), (0.08, 1.0), (0.9, 0.95), (1.0, 0.0)])
    static = band_pass(white_noise(0.50, rng), 1500.0, q=0.9)
    static = exp_decay(static, tau=0.140, attack=0.002)
    thump = exp_decay(sine_sweep(0.16, 120.0, 60.0, curve=0.7), tau=0.050, attack=0.002)
    tail = mix(static, gain(thump, 0.5))
    return edge_fade(carrier + silence(0.015) + tail, fade_out=0.04)


def production_done():
    """Pleasant confirmation chime (~400 ms): a struck bar around G5 with
    second and third harmonics decaying faster than the fundamental, plus a
    soft octave pre-tap. Aiming for a warm 'unit ready' that never nags."""
    f = 784.0  # G5
    fundamental = exp_decay(sine_sweep(0.40, f, f), tau=0.130, attack=0.004)
    h2 = exp_decay(sine_sweep(0.28, f * 2.0, f * 2.0), tau=0.070, attack=0.003)
    h3 = exp_decay(sine_sweep(0.18, f * 3.02, f * 3.02), tau=0.045, attack=0.002)
    tap = exp_decay(sine_sweep(0.08, f / 2.0, f / 2.0), tau=0.030, attack=0.002)
    out = mix(fundamental, gain(h2, 0.35), gain(h3, 0.18), gain(tap, 0.3))
    return edge_fade(out, fade_out=0.05)


def superweapon_charge():
    """Rising shimmer (~2 s): three detuned sines sweeping slowly from 180 Hz
    up past 700 Hz, beating against each other, with a faint air layer of
    high band-passed noise. Aiming for menace-building anticipation rather
    than an alarm."""
    rng = random.Random(111)
    detunes = (0.0, 4.0, -6.5)
    layers = []
    for d in detunes:
        layers.append(gain(sine_sweep(2.0, 180.0 + d, 720.0 + d * 2.0, curve=1.4),
                           1.0 / len(detunes)))
    air = band_pass(white_noise(2.0, rng), lambda i: 2000.0 + 2500.0 * (i / (SR * 2.0)), q=4.0)
    out = mix(mix(*layers), gain(air, 0.15))
    out = envelope(out, [(0.0, 0.0), (0.15, 0.5), (0.9, 1.0), (1.0, 0.0)])
    return edge_fade(out, fade_in=0.01, fade_out=0.04)


def superweapon_impact():
    """The big one (~2.5 s): an initial wide noise wall, a sub sine dropping
    from 75 Hz to 22 Hz, and a long brown-noise decay. Aiming for a strike
    that momentarily owns the whole mix, then leaves room again."""
    rng = random.Random(112)
    wall = exp_decay(white_noise(0.8, rng), tau=0.150, attack=0.001)
    wall = low_pass(wall, lambda i: 6000.0 * math.exp(-i / (SR * 0.35)) + 300.0)
    sub = exp_decay(sine_sweep(2.0, 75.0, 22.0, curve=0.6),
                    tau=0.700, attack=0.002)
    tail = exp_decay(low_pass(brown_noise(2.5, rng), 350.0),
                     tau=0.650, attack=0.002)
    out = mix(gain(wall, 0.8), gain(sub, 0.9), gain(tail, 0.7))
    return edge_fade(out, fade_out=0.20)


def ambient_wind():
    """Low filtered wind bed (~8 s, seamless loop): brown noise through a
    slowly wandering low-pass with gentle amplitude swells. The final 1.5 s
    is generated as extra material and crossfaded into the head so the loop
    point is inaudible. No edge fades here; the crossfade owns continuity."""
    rng = random.Random(113)
    dur, fade = 8.0, 1.5
    n, nf = int(SR * dur), int(SR * fade)
    raw = brown_noise(dur + fade, rng)

    # Slow wandering cutoff between about 220 and 480 Hz.
    def cutoff(i):
        t = i / SR
        return 350.0 + 130.0 * math.sin(2.0 * math.pi * 0.09 * t + 1.3)

    raw = low_pass(raw, cutoff)

    # Gentle swells so the bed breathes instead of hissing statically.
    swelled = []
    for i, s in enumerate(raw):
        t = i / SR
        g = 0.75 + 0.25 * math.sin(2.0 * math.pi * 0.16 * t) \
                 + 0.10 * math.sin(2.0 * math.pi * 0.05 * t + 0.7)
        swelled.append(s * g)

    # Equal-power crossfade of the surplus tail into the head.
    out = swelled[:n]
    for i in range(nf):
        a = i / nf
        up = math.sin(a * math.pi / 2.0)
        down = math.cos(a * math.pi / 2.0)
        out[i] = out[i] * up + swelled[n + i] * down
    return out


# ---------------------------------------------------------------------------
# TICKET-P6-MUSIC-01: the placeholder score. Two beds of identical length so
# the client's music player pair stays bar-aligned when both loop; the calm
# bed is the room the battle happens in, the combat layer is additive
# percussion and tension crossfaded in over it. A composed soundtrack is a
# later art decision (doc 24 says so out loud); these exist so the SYSTEM
# ships and nobody mistakes the placeholder for the destination.
# ---------------------------------------------------------------------------

MUSIC_LOOP_SECONDS = 64.0


def _loop_crossfade(samples, dur, fade):
    """The ambient_wind loop idiom, named once for the music beds: the
    surplus tail is equal-power crossfaded into the head so the loop point is
    inaudible. A drone that edge-fades to zero instead dips audibly."""
    n, nf = int(SR * dur), int(SR * fade)
    out = samples[:n]
    for i in range(nf):
        a = i / nf
        up = math.sin(a * math.pi / 2.0)
        down = math.cos(a * math.pi / 2.0)
        out[i] = out[i] * up + samples[n + i] * down
    return out


def _check_loop_seam(name, samples, limit=0.02):
    """The loop-point discipline VERIFIED rather than trusted: after
    normalisation, the step from the final sample back to the first must be
    no larger than an ordinary adjacent-sample step of a low drone. A failed
    seam raises rather than shipping a click."""
    normed = normalise(samples)
    step = abs(normed[-1] - normed[0])
    if step > limit:
        raise AssertionError(
            "%s loop seam steps %.4f (limit %.3f): the loop would click"
            % (name, step, limit))
    print("%-24s loop seam %.5f (limit %.3f)" % (name, step, limit))


def _place(buf, samples, at):
    """Add a hit into a loop buffer at `at` seconds, wrapping past the end:
    a hit still decaying when the loop closes rings into the head, which is
    exactly what it does on playback, so the seam is seamless by
    construction."""
    i0 = int(SR * at)
    n = len(buf)
    for i, s in enumerate(samples):
        buf[(i0 + i) % n] += s


def music_calm():
    """The calm bed (64 s seamless loop): root-and-fifth drones low in the
    register (D2 and A2 over a D1 sub), a faint minor-colour partial and a
    whisper of filtered air. Each layer breathes on its own slow cycle and
    none of the cycle lengths divides the loop, so the bed never audibly
    repeats inside itself. Deliberately understated: low, slow, no melody."""
    rng = random.Random(114)
    dur, fade = MUSIC_LOOP_SECONDS, 4.0
    total = dur + fade
    root = sine(total, 73.42)     # D2
    fifth = sine(total, 110.0)    # A2
    sub = sine(total, 36.71)      # D1
    colour = sine(total, 174.61)  # F3, the minor third two octaves up, faint
    air = low_pass(brown_noise(total, rng), 420.0)
    out = []
    for i in range(len(root)):
        t = i / SR
        g_root = 0.80 + 0.20 * math.sin(2.0 * math.pi * t / 23.0)
        g_fifth = 0.55 + 0.25 * math.sin(2.0 * math.pi * t / 31.0 + 1.1)
        g_sub = 0.45 + 0.10 * math.sin(2.0 * math.pi * t / 41.0 + 2.3)
        g_colour = 0.16 + 0.12 * math.sin(2.0 * math.pi * t / 53.0 + 0.4)
        g_air = 0.10 + 0.05 * math.sin(2.0 * math.pi * t / 19.0 + 2.9)
        out.append(root[i] * g_root + fifth[i] * g_fifth + sub[i] * g_sub
                   + colour[i] * g_colour + air[i] * g_air)
    out = _loop_crossfade(out, dur, fade)
    _check_loop_seam("music_calm.wav", out)
    return out


def music_combat():
    """The combat layer (64 s, the calm bed's exact length): a low war-drum
    pattern with answering toms, a tight tick and a staccato tension pulse,
    at 90 BPM so 24 four-beat bars close the loop exactly. Additive over the
    calm bed, sharing its D root, never a replacement for it. Hits that are
    still decaying when the loop closes wrap into the head (_place), so the
    seam is silent by construction and the check proves it."""
    rng = random.Random(115)
    dur = MUSIC_LOOP_SECONDS
    beat = 60.0 / 90.0
    out = silence(dur)

    drum = exp_decay(sine_sweep(0.30, 82.0, 44.0, curve=0.6), tau=0.075, attack=0.002)
    tom = exp_decay(band_pass(white_noise(0.16, rng), 210.0, q=3.0), tau=0.050, attack=0.001)
    tom2 = exp_decay(band_pass(white_noise(0.14, rng), 300.0, q=3.0), tau=0.042, attack=0.001)
    tick = exp_decay(band_pass(white_noise(0.05, rng), 4200.0, q=5.0), tau=0.010, attack=0.0005)
    pulse = exp_decay(sine_sweep(0.10, 110.0, 108.0), tau=0.030, attack=0.002)

    bars = int(dur / (4.0 * beat))
    for bar in range(bars):
        t0 = bar * 4.0 * beat
        _place(out, drum, t0)                              # the downbeat
        _place(out, gain(drum, 0.8), t0 + 2.0 * beat)
        _place(out, gain(tom, 0.7), t0 + 1.0 * beat)
        _place(out, gain(tom2, 0.6), t0 + 3.0 * beat)
        if bar % 4 == 3:                                   # a fill closes every fourth bar
            _place(out, gain(tom, 0.5), t0 + 3.25 * beat)
            _place(out, gain(tom2, 0.5), t0 + 3.5 * beat)
            _place(out, gain(drum, 0.6), t0 + 3.75 * beat)
        for eighth in range(8):
            _place(out, gain(tick, 0.35 if eighth % 2 else 0.20), t0 + eighth * 0.5 * beat)
        for q in range(4):
            _place(out, gain(pulse, 0.5), t0 + q * beat + 0.5 * beat)
    out = edge_fade(out, fade_in=0.002, fade_out=0.03)
    _check_loop_seam("music_combat.wav", out)
    return out


# ---------------------------------------------------------------------------

SOUNDS = [
    ("ui_click.wav", ui_click),
    ("ui_confirm.wav", ui_confirm),
    ("order_move.wav", order_move),
    ("shot_rifle.wav", shot_rifle),
    ("shot_cannon.wav", shot_cannon),
    ("shot_rocket.wav", shot_rocket),
    ("explosion_small.wav", explosion_small),
    ("explosion_large.wav", explosion_large),
    ("alert_attack.wav", alert_attack),
    ("alert_harvester.wav", alert_harvester),
    ("alert_low_power.wav", alert_low_power),
    ("alert_radar.wav", alert_radar),
    ("production_done.wav", production_done),
    ("superweapon_charge.wav", superweapon_charge),
    ("superweapon_impact.wav", superweapon_impact),
    ("ambient_wind.wav", ambient_wind),
    ("music_calm.wav", music_calm),
    ("music_combat.wav", music_combat),
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Rendering %d sounds to %s (peak -3 dBFS, 16-bit %d Hz mono)\n"
          % (len(SOUNDS), OUT_DIR, SR))
    for name, generator in SOUNDS:
        write_wav(name, generator())
    print("\nDone.")


if __name__ == "__main__":
    main()
