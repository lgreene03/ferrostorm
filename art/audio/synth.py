#!/usr/bin/env python3
"""Ferrostorm procedural SFX synthesiser and interim score.

Renders the game's SFX set as 16-bit 44.1 kHz mono WAV files into
game/audio/, and (P8-46, decision D26) the interim score: six tracks of at
least 180 seconds each into game/audio/music/ as Ogg Vorbis. Synthesis is
pure standard library (wave, math, array, random); no numpy, no downloaded
assets. Every sound is an original synthesis recipe and deliberately avoids
any resemblance to the audio motifs, announcer or soundtracks of the classic
RTS games of the 90s.

Each SFX file is normalised to a peak of -3 dBFS and the script prints the
RMS level of every rendered file so silence or clipping is obvious. Each
score track is loudness matched (EBU R128, measured by ffmpeg) and the script
prints its length, loudness, peak, loop seam and the level of every section,
so a silent section or a clipped one is visible without listening.

The score needs ffmpeg on PATH: its built-in Vorbis encoder writes the .ogg
files and its ebur128 filter measures loudness. Six 180-second tracks in the
SFX format (16-bit 44.1 kHz mono WAV) would add about 95 MB to the repo; as
Vorbis they add about 6 MB.

Run:  python3 art/audio/synth.py            (everything)
      python3 art/audio/synth.py sfx        (the SFX set only)
      python3 art/audio/synth.py score      (the score only)
"""

import array
import collections
import functools
import itertools
import math
import operator
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
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


def shot_heavy_mg():
    """Heavy machine-gun burst (~300 ms), P8-44: four rounds at about 14 a
    second, each a mid band-passed crack over a short low body thump, the
    rounds jittered in time and level by the seed so the burst does not
    read as a loop. Lower, heavier and longer than the rifle's single
    crack: the autocannon and the emplacement gun, which shared the rifle's
    report until now."""
    rng = random.Random(116)
    out = silence(0.30)
    for k in range(4):
        at = int(SR * (k * 0.070 + rng.uniform(-0.004, 0.004)))
        crack = band_pass(white_noise(0.07, rng), 1700.0, q=1.1)
        crack = exp_decay(crack, tau=0.016, attack=0.0005)
        body = exp_decay(sine_sweep(0.07, 170.0, 85.0, curve=0.6), tau=0.020, attack=0.001)
        level = (1.0 - 0.12 * k) * rng.uniform(0.85, 1.0)
        for i, s in enumerate(mix(crack, gain(body, 0.7))):
            if at + i < len(out):
                out[at + i] += s * level
    return edge_fade(out, fade_out=0.03)


def shot_flak():
    """Flak airburst (~600 ms), P8-44: TWO events, the launch and the burst
    in the air a beat later, which is what tells it from every gun on the
    ground. A dull low pop leaves the barrel; 0.17 s on, a bright sharp
    crack with a hollow overpressure boom under it, and a short ringing
    tail of high band-passed noise as the shell's fragments spread. The
    flak track's gun, the game's only anti-air weapon."""
    rng = random.Random(117)
    pop = exp_decay(sine_sweep(0.12, 95.0, 60.0, curve=0.6), tau=0.030, attack=0.001)
    pop = mix(pop, gain(exp_decay(band_pass(white_noise(0.08, rng), 700.0, q=1.0), tau=0.015), 0.5))
    crack = exp_decay(band_pass(white_noise(0.10, rng), 2800.0, q=0.7), tau=0.012, attack=0.0005)
    boom = exp_decay(low_pass(brown_noise(0.40, rng), 900.0), tau=0.110, attack=0.002)
    ring = exp_decay(band_pass(white_noise(0.35, rng), 4200.0, q=2.5), tau=0.090, attack=0.004)
    burst = mix(crack, gain(boom, 0.8), gain(ring, 0.18))
    out = mix(gain(pop, 0.55), silence(0.17) + burst)
    return edge_fade(out, fade_out=0.04)


def shot_howitzer():
    """Artillery report (~1.2 s), P8-44: a deep long-throated boom, lower
    and longer than the cannon. A sub sine sagging 70 to 30 Hz, a dark
    brown-noise body with a slow decay, a short blast transient, and the
    body's rumble returning 0.22 s later at a quarter level, the roll of a
    big gun across open ground."""
    rng = random.Random(118)
    sub = exp_decay(sine_sweep(0.9, 70.0, 30.0, curve=0.7), tau=0.260, attack=0.002)
    body = exp_decay(low_pass(brown_noise(1.0, rng), 420.0), tau=0.300, attack=0.002)
    blast = exp_decay(band_pass(white_noise(0.25, rng), 650.0, q=0.8), tau=0.045, attack=0.0005)
    first = mix(sub, gain(body, 0.8), gain(blast, 0.6))
    echo = silence(0.22) + gain(low_pass(first, 300.0), 0.25)
    return edge_fade(mix(first, echo), fade_out=0.10)


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


def death_infantry():
    """Soft fall (~320 ms), P8-43: a squad going down, not a vehicle going
    up. A muffled body knock (a sine dropping 150 to 70 Hz with a very short
    decay) under a short scuff of low band-passed brown noise, the dust of
    the puff the effect draws. No detonation layer and nothing bright, so a
    rifle squad's death can never be mistaken for explosion_small; no voice
    and no cry."""
    rng = random.Random(114)
    knock = exp_decay(sine_sweep(0.16, 150.0, 70.0, curve=0.6), tau=0.035, attack=0.002)
    scuff = band_pass(brown_noise(0.32, rng), 520.0, q=0.9)
    scuff = envelope(scuff, [(0.0, 0.0), (0.06, 1.0), (0.35, 0.55), (1.0, 0.0)])
    settle = band_pass(white_noise(0.22, rng), 1800.0, q=1.5)
    settle = exp_decay(settle, tau=0.040, attack=0.010)
    out = mix(knock, gain(scuff, 0.8), gain(settle, 0.12))
    return edge_fade(out, fade_out=0.04)


def collapse_rumble():
    """A building giving way (~1.3 s), P8-43: the third stage of a
    structure's collapse, after the blast and the secondary pops. A slow
    low-passed brown-noise rumble swelling in and settling out, a sub sine
    sagging 55 to 30 Hz underneath, and a scatter of short band-passed
    debris ticks thinning out over the tail, so it reads as mass coming
    down rather than another explosion. Original synthesis."""
    rng = random.Random(115)
    dur = 1.3
    rumble = low_pass(brown_noise(dur, rng), lambda i: 700.0 - 450.0 * (i / (SR * dur)))
    rumble = envelope(rumble, [(0.0, 0.0), (0.12, 1.0), (0.45, 0.8), (1.0, 0.0)])
    sub = sine_sweep(dur, 55.0, 30.0, curve=0.8)
    sub = envelope(sub, [(0.0, 0.0), (0.1, 0.9), (0.6, 0.5), (1.0, 0.0)])
    debris = silence(dur)
    for k in range(14):
        at = int(SR * (0.08 + 0.9 * (k / 14.0) ** 1.4 + rng.uniform(-0.02, 0.02)))
        tick = band_pass(white_noise(0.05, rng), rng.uniform(1100.0, 2600.0), q=3.0)
        tick = exp_decay(tick, tau=0.010, attack=0.0005)
        level = 0.5 * (1.0 - k / 16.0)
        for i, s in enumerate(tick):
            if 0 <= at + i < len(debris):
                debris[at + i] += s * level
    out = mix(rumble, gain(sub, 0.7), gain(debris, 0.5))
    return edge_fade(out, fade_in=0.005, fade_out=0.10)


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
    clause). Original synthesis; no resemblance to the classic games'
    announcer audio."""
    rng = random.Random(112)
    carrier = mix(sine_sweep(0.20, 932.0, 932.0),
                  gain(sine_sweep(0.20, 1864.0, 1864.0), 0.18))
    carrier = envelope(carrier, [(0.0, 0.0), (0.08, 1.0), (0.9, 0.95), (1.0, 0.0)])
    static = band_pass(white_noise(0.50, rng), 1500.0, q=0.9)
    static = exp_decay(static, tau=0.140, attack=0.002)
    thump = exp_decay(sine_sweep(0.16, 120.0, 60.0, curve=0.7), tau=0.050, attack=0.002)
    tail = mix(static, gain(thump, 0.5))
    return edge_fade(carrier + silence(0.015) + tail, fade_out=0.04)


def alert_jammed():
    """Jamming cue (~800 ms), P8-10: two close carriers near 640 and 680 Hz
    beating against each other, CHOPPED at 16 Hz into a stutter, with a
    band of hiss swelling underneath and the whole thing sliding apart in
    pitch. The sound of a signal being TAMPERED WITH, which is the point:
    alert_radar is a feed dropping out (a clean carrier that snaps into
    static), and a jam is the enemy's doing and ends on its own, so it must
    not sound like the loss (FEEL-09, ADR-065's "interface bug" worry).
    Original synthesis; no resemblance to the classic games' audio."""
    rng = random.Random(113)
    dur = 0.80
    tone = mix(sine_sweep(dur, 640.0, 612.0), gain(sine_sweep(dur, 677.0, 706.0), 0.8))
    chop = [1.0 if int(i * 16 / SR) % 2 == 0 else 0.22 for i in range(len(tone))]
    tone = [t * c for t, c in zip(tone, chop)]
    tone = envelope(tone, [(0.0, 0.0), (0.04, 1.0), (0.80, 0.7), (1.0, 0.0)])
    hiss = band_pass(white_noise(dur, rng), 2400.0, q=1.4)
    hiss = envelope(hiss, [(0.0, 0.0), (0.6, 0.55), (1.0, 0.0)])
    return edge_fade(mix(tone, gain(hiss, 0.6)), fade_out=0.04)


def production_done():
    """Pleasant confirmation chime (~400 ms): a struck bar around G5 with
    second and third harmonics decaying faster than the fundamental, plus a
    soft octave pre-tap. Aiming for a warm 'it is done' that never nags."""
    f = 784.0  # G5
    fundamental = exp_decay(sine_sweep(0.40, f, f), tau=0.130, attack=0.004)
    h2 = exp_decay(sine_sweep(0.28, f * 2.0, f * 2.0), tau=0.070, attack=0.003)
    h3 = exp_decay(sine_sweep(0.18, f * 3.02, f * 3.02), tau=0.045, attack=0.002)
    tap = exp_decay(sine_sweep(0.08, f / 2.0, f / 2.0), tau=0.030, attack=0.002)
    out = mix(fundamental, gain(h2, 0.35), gain(h3, 0.18), gain(tap, 0.3))
    return edge_fade(out, fade_out=0.05)


def _metal(freq, dur, tau):
    """A small struck-metal tone: the first three modes of a free bar
    (1, 2.76, 5.40 times the fundamental), the higher ones dying faster."""
    return mix(exp_decay(sine(dur, freq), tau=tau, attack=0.002),
               gain(exp_decay(sine(dur, freq * 2.76), tau=tau * 0.5, attack=0.001), 0.35),
               gain(exp_decay(sine(dur, freq * 5.40), tau=tau * 0.25, attack=0.001), 0.15))


def cue_promoted():
    """Rank earned (~520 ms), P8-44: two bright struck-metal tones stepping
    up a major sixth, the second ringing on over a soft shimmer of high
    band-passed noise, a pin set on a collar rather than a fanfare. Its own
    cue where P8-10 lent the promotion ui_confirm; deliberately no brass
    and no melody, so it can never be mistaken for a musical sting."""
    rng = random.Random(119)
    first = _metal(698.0, 0.16, 0.050)
    second = _metal(1174.0, 0.40, 0.120)
    shimmer = band_pass(white_noise(0.40, rng), 6000.0, q=3.0)
    shimmer = envelope(shimmer, [(0.0, 0.0), (0.15, 0.6), (1.0, 0.0)])
    out = mix(first, silence(0.11) + mix(second, gain(shimmer, 0.10)))
    return edge_fade(out, fade_out=0.05)


def cue_deployed():
    """Unfold and lock (~760 ms), P8-44: the MCV becoming a yard. A servo
    whirr, a sine with two harmonics climbing 160 to 380 Hz under a band of
    mechanical hiss and swelling as it goes, cut off by a heavy clunk (a
    low thump dropping 120 to 55 Hz, a burst of 900 Hz noise and a short
    metal tick) as the frame locks. Its own cue where P8-10 lent ui_confirm."""
    rng = random.Random(120)
    dur = 0.48
    whirr = mix(sine_sweep(dur, 160.0, 380.0, curve=0.8),
                gain(sine_sweep(dur, 320.0, 760.0, curve=0.8), 0.30),
                gain(sine_sweep(dur, 480.0, 1140.0, curve=0.8), 0.12))
    hiss = band_pass(white_noise(dur, rng), lambda i: 1500.0 + 1500.0 * (i / (SR * dur)), q=1.8)
    whirr = envelope(mix(gain(whirr, 0.5), gain(hiss, 0.25)), [(0.0, 0.0), (0.2, 0.5), (0.95, 1.0), (1.0, 0.2)])
    thump = exp_decay(sine_sweep(0.22, 120.0, 55.0, curve=0.6), tau=0.060, attack=0.001)
    knock = exp_decay(band_pass(white_noise(0.10, rng), 900.0, q=1.2), tau=0.020, attack=0.0005)
    clunk = mix(thump, gain(knock, 0.7), gain(_metal(1350.0, 0.12, 0.030), 0.25))
    return edge_fade(whirr + clunk + silence(0.06), fade_out=0.04)


def cue_placed():
    """Foundation set down (~420 ms), P8-44: the sim accepting a placement.
    A deep soft thud (a sine sagging 85 to 40 Hz), a crunch of band-passed
    brown noise as gravel takes the weight, and a faint metal tick on top,
    heavy and short so a wall run's one cue per tick never drones."""
    rng = random.Random(121)
    thud = exp_decay(sine_sweep(0.32, 85.0, 40.0, curve=0.6), tau=0.085, attack=0.002)
    crunch = exp_decay(band_pass(brown_noise(0.30, rng), 1200.0, q=0.9), tau=0.060, attack=0.003)
    tick = _metal(1500.0, 0.10, 0.020)
    out = mix(thud, gain(crunch, 0.6), silence(0.015) + gain(tick, 0.18))
    return edge_fade(out + silence(0.08), fade_out=0.05)


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
# P8-46 (decision D26): the interim score. Six tracks of at least 180 s each
# replace TICKET-P6-MUSIC-01's two 64-second loops, which a 30-minute match
# heard about 28 times apiece. Three are CALM (the room the battle happens in,
# one of them a slow build) and three are COMBAT; the client plays them as an
# intensity playlist (AudioDirector), never the same track twice in a row.
#
# Commissioned music is spend, which the owner keeps for himself, so this is
# still an interim score. It exists so a match stops sounding cheap, and
# nobody should mistake it for the destination.
#
# The palette is the house one the P6 beds and the SFX set established: sine
# drones, struck bars, filtered-noise drums and ticks, swept sines. Variety
# comes from key, tempo, METRE (4/4, 3/4, 7/8, 6/8 and 5/4) and a lead timbre
# per track. Deliberately absent: guitars, sampled breakbeats and
# four-on-the-floor synth stabs, the sound of the soundtracks this game must
# not resemble. Every melodic line is a seeded random walk over chord tones,
# so nothing is transcribed from anywhere.
#
# Every track is deterministic from its seed: a re-run regenerates the same
# PCM, and ffmpeg's bitexact flags make the same PCM encode to the same bytes.
# ---------------------------------------------------------------------------

MUSIC_DIR = os.path.join(OUT_DIR, "music")
SCORE_MIN_SECONDS = 180.0
# File loudness per intensity. The client plays calm tracks at -14 dB and
# combat tracks at -10 dB (AudioDirector, the P6 levels, kept), so these land
# each state where the P6 pair sat in a match: the calm bed measured -16.0
# LUFS, and at full intensity the ducked bed plus the combat layer summed to
# about -31 LUFS at the bus, which -21 LUFS at -10 dB reproduces.
SCORE_LUFS = {"calm": -16.0, "combat": -21.0}
SCORE_PEAK_CEILING = 10.0 ** (-1.0 / 20.0)   # -1 dBFS: headroom for the encoder
# Low drums and drones spend headroom without adding much measured loudness,
# so a track can hit its peak ceiling before its loudness target. A
# look-ahead limiter (_limit) takes the difference out of the peaks; past
# this much gain reduction the track is too peaky and the mix itself must
# change, so write_score refuses it.
SCORE_LIMIT_MAX_DB = 6.0
VORBIS_QUALITY = "6"                          # ffmpeg's built-in encoder: 36 to 56 kbps on this score
LOOP_FADE_SECONDS = 4.0


def hz(midi):
    """Equal-tempered frequency of a MIDI note number (A4 = 69 = 440 Hz)."""
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def zeros(n):
    return array.array("d", bytes(8 * n))


def tone(n, freq, phase=0.0):
    """n samples of a sine. Built from C-level iterators rather than a
    Python loop, so a three-minute layer costs seconds rather than minutes."""
    w = 2.0 * math.pi * freq / SR
    return array.array("d", map(math.sin, map(operator.add,
                                              map(operator.mul, range(n), itertools.repeat(w)),
                                              itertools.repeat(phase))))


def times(a, b):
    """Elementwise product; as long as the shorter input."""
    return array.array("d", map(operator.mul, a, b))


def scaled(a, g):
    return array.array("d", map(operator.mul, a, itertools.repeat(g)))


def plus(a, b):
    """Elementwise sum; as long as the longer input."""
    if len(a) < len(b):
        a, b = b, a
    out = array.array("d", a)
    out[:len(b)] = array.array("d", map(operator.add, out[:len(b)], b))
    return out


def fit(a, n):
    """Exactly n samples: truncated, or padded with silence."""
    a = array.array("d", a)
    if len(a) >= n:
        return a[:n]
    a.extend(itertools.repeat(0.0, n - len(a)))
    return a


def add_into(buf, src, at, g=1.0):
    """Mix src into buf from sample `at`, wrapping past the end. A note still
    sounding when the loop closes rings on into the head, which is exactly
    what it does on playback, so the seam is continuous by construction (the
    rule the P6 combat layer placed its hits by)."""
    n, m = len(buf), len(src)
    if g != 1.0:
        src = scaled(src, g)
    pos, done = at % n, 0
    while done < m:
        k = min(m - done, n - pos)
        buf[pos:pos + k] = array.array("d", map(operator.add, buf[pos:pos + k], src[done:done + k]))
        done += k
        pos = 0


def curve(n, points):
    """Piecewise-linear automation over n samples from (sample, value)
    points, held flat before the first and after the last. Points at the
    same sample make a step, in the order given."""
    pts = sorted(((int(i), float(v)) for i, v in points), key=lambda p: p[0])
    if pts[0][0] > 0:
        pts.insert(0, (0, pts[0][1]))
    if pts[-1][0] < n:
        pts.append((n, pts[-1][1]))
    out = array.array("d")
    for (i0, v0), (i1, v1) in zip(pts, pts[1:]):
        span = i1 - i0
        if span <= 0:
            continue
        step = (v1 - v0) / span
        out.extend(map(operator.add, itertools.repeat(v0, span),
                       map(operator.mul, range(span), itertools.repeat(step))))
    return fit(out, n)


def breathe(n, period, base, depth, phase=0.0):
    """A slow sinusoidal gain, base plus or minus depth, one cycle per
    `period` seconds: how every bed layer breathes on its own clock."""
    w = 2.0 * math.pi / (period * SR)
    return array.array("d", map(operator.add, itertools.repeat(base),
                                map(operator.mul, itertools.repeat(depth),
                                    map(math.sin, map(operator.add,
                                                      map(operator.mul, range(n), itertools.repeat(w)),
                                                      itertools.repeat(phase))))))


@functools.lru_cache(maxsize=None)
def decay_env(n, tau, attack):
    """Linear attack then exponential decay with time constant tau."""
    na = max(1, int(SR * attack))
    head = array.array("d", (i / na for i in range(min(na, n))))
    k = -1.0 / (tau * SR)
    tail = array.array("d", map(math.exp, map(operator.mul, range(n - len(head)), itertools.repeat(k))))
    return head + tail


def tail_fade(a, seconds=0.05):
    """Fade the last `seconds` to zero, so a decaying note never ends on a step."""
    out = array.array("d", a)
    m = min(len(out), max(1, int(SR * seconds)))
    for i in range(m):
        out[len(out) - 1 - i] *= i / m
    return out


def brown(n, rng, leak=0.998):
    """brown_noise into an array, exactly n samples, without the two
    intermediate lists that make a three-minute bed cost half a gigabyte."""
    out = zeros(n)
    acc = 0.0
    uniform = rng.uniform
    for i in range(n):
        acc = acc * leak + uniform(-1.0, 1.0) * 0.02
        out[i] = acc
    return scaled(out, 1.0 / (max(map(abs, out)) or 1.0))


def one_pole(samples, cutoff):
    """low_pass at a constant cutoff, without the per-sample exp."""
    a = 1.0 - math.exp(-2.0 * math.pi * cutoff / SR)
    out = zeros(len(samples))
    y = 0.0
    for i, s in enumerate(samples):
        y += a * (s - y)
        out[i] = y
    return out


def voiced(chord, low):
    """A chord's distinct pitch classes stacked upward from MIDI `low`. A
    line written as chord-tone INDICES into this is consonant over whatever
    chord is sounding, which is how every melody here stays in key without
    a note of it being transcribed."""
    pcs = []
    for m in chord:
        if m % 12 not in pcs:
            pcs.append(m % 12)
    return sorted(low + (pc - low) % 12 for pc in pcs)


def chord_tone(tones, idx):
    """Index into voiced() tones, climbing an octave past the top."""
    return tones[idx % len(tones)] + 12 * (idx // len(tones))


def motif(rng, length, span=5):
    """A seeded random walk over chord-tone indices 0..span-1 that touches
    at least three of them, so it is a line and not a repeated note."""
    while True:
        out = [rng.randrange(0, 3)]
        for _ in range(length - 1):
            out.append(min(max(out[-1] + rng.choice((-2, -1, 1, 2)), 0), span - 1))
        if len(set(out)) >= 3:
            return out


# ---- Instruments. Hits are cached by their arguments, so a drum placed four
# hundred times is synthesised once.

@functools.lru_cache(maxsize=None)
def struck_bar(freq, dur=3.0, tau=0.9):
    """The production_done chime stretched into a bell: a fundamental with
    upper partials (one slightly stretched) dying faster than it does."""
    n = int(SR * dur)
    out = zeros(n)
    for ratio, amp, t in ((1.0, 1.0, 1.0), (2.0, 0.35, 0.45), (3.02, 0.18, 0.25), (4.17, 0.06, 0.15)):
        if freq * ratio < SR / 3.0:
            out = plus(out, scaled(times(tone(n, freq * ratio), decay_env(n, tau * t, 0.004)), amp))
    return tail_fade(out)


@functools.lru_cache(maxsize=None)
def wire_pluck(freq, dur=2.0, tau=0.6):
    """A plucked stiff wire: six harmonics sharpened slightly by stiffness
    (f_k = k f sqrt(1 + B k^2)), the upper ones dying first."""
    n = int(SR * dur)
    out = zeros(n)
    for k in range(1, 7):
        fk = k * freq * math.sqrt(1.0 + 0.0004 * k * k)
        if fk > 8000.0:
            break
        out = plus(out, scaled(times(tone(n, fk), decay_env(n, tau / k ** 0.7, 0.002)), 1.0 / k ** 1.3))
    return tail_fade(out)


@functools.lru_cache(maxsize=None)
def fm_bell(freq, dur=4.0, tau=1.2, ratio=1.4, index=2.4):
    """A low two-operator FM bell. The inharmonic ratio gives it the clang
    of a struck plate rather than a chime."""
    n = int(SR * dur)
    env = decay_env(n, tau, 0.003)
    ienv = decay_env(n, tau * 0.5, 0.001)
    wc = 2.0 * math.pi * freq / SR
    wm = wc * ratio
    sin = math.sin
    return tail_fade(array.array("d", (sin(wc * i + index * ienv[i] * sin(wm * i)) * env[i]
                                       for i in range(n))), 0.2)


@functools.lru_cache(maxsize=None)
def war_drum(f0=82.0, f1=44.0, dur=0.30, tau=0.075):
    """The P6 combat layer's drum, a swept sine thump; retuned per track."""
    return tail_fade(array.array("d", exp_decay(sine_sweep(dur, f0, f1, curve=0.6),
                                                tau=tau, attack=0.002)), 0.02)


@functools.lru_cache(maxsize=None)
def tom(centre, dur, tau, seed):
    """A band-passed noise skin over a short pitched body sweeping down."""
    rng = random.Random(seed)
    skin = exp_decay(band_pass(white_noise(dur, rng), centre, q=3.0), tau=tau, attack=0.001)
    body = exp_decay(sine_sweep(dur, centre * 1.15, centre * 0.85, curve=0.7), tau=tau * 1.4, attack=0.002)
    return tail_fade(array.array("d", mix(skin, gain(body, 0.6))), 0.02)


@functools.lru_cache(maxsize=None)
def tick(centre, seed):
    """The P6 combat layer's tick: a tight burst of high band-passed noise."""
    rng = random.Random(seed)
    return tail_fade(array.array("d", exp_decay(band_pass(white_noise(0.05, rng), centre, q=5.0),
                                                tau=0.010, attack=0.0005)), 0.01)


@functools.lru_cache(maxsize=None)
def anvil(freq, seed):
    """Struck metal: the inharmonic modes of a free bar (1, 2.76, 5.40,
    8.93) over a short noise click. The foundry this world is built in."""
    rng = random.Random(seed)
    n = int(SR * 1.2)
    out = zeros(n)
    for ratio, amp, t in ((1.0, 1.0, 0.45), (2.756, 0.6, 0.25), (5.404, 0.35, 0.12), (8.933, 0.18, 0.07)):
        if freq * ratio < SR / 3.0:
            out = plus(out, scaled(times(tone(n, freq * ratio), decay_env(n, t, 0.001)), amp))
    click = exp_decay(band_pass(white_noise(0.03, rng), 3000.0, q=1.5), tau=0.004, attack=0.0005)
    return tail_fade(plus(out, scaled(array.array("d", click), 0.5)))


@functools.lru_cache(maxsize=None)
def bass_note(freq, dur, tau, bright=0.5):
    """A staccato bass: a sine with three quiet harmonics, so the line
    still reads on small speakers that cannot reproduce its fundamental."""
    n = int(SR * dur)
    out = tone(n, freq)
    for k, amp in ((2, 0.5), (3, 0.25), (4, 0.12)):
        out = plus(out, scaled(tone(n, freq * k), amp * bright))
    return tail_fade(times(out, decay_env(n, tau, 0.004)), 0.02)


@functools.lru_cache(maxsize=None)
def fm_bass(freq, dur, tau, index=1.8):
    """A plucked metal bass: FM at a 1:1 ratio with the index dying faster
    than the note, so each attack is bright and the body is round."""
    n = int(SR * dur)
    env = decay_env(n, tau, 0.003)
    ienv = decay_env(n, tau * 0.4, 0.001)
    w = 2.0 * math.pi * freq / SR
    sin = math.sin
    return tail_fade(array.array("d", (sin(w * i + index * ienv[i] * sin(w * i)) * env[i]
                                       for i in range(n))), 0.02)


@functools.lru_cache(maxsize=None)
def bowed_bass(freq, dur, attack=0.06, release=0.3):
    """A bowed low string: four harmonics under a quick swell and a soft release."""
    n = int(SR * dur)
    env = curve(n, [(0, 0.0), (int(SR * attack), 1.0), (n - int(SR * release), 0.8), (n, 0.0)])
    out = zeros(n)
    for k, amp in ((1, 1.0), (2, 0.45), (3, 0.22), (4, 0.10)):
        out = plus(out, scaled(tone(n, freq * k * (1.0 + 0.0015 * (k - 1))), amp))
    return times(out, env)


@functools.lru_cache(maxsize=None)
def blip(freq, dur=0.12, tau=0.04):
    """The P6 combat layer's staccato tension pulse, pitched."""
    n = int(SR * dur)
    return tail_fade(times(plus(tone(n, freq), scaled(tone(n, 2.0 * freq), 0.2)),
                           decay_env(n, tau, 0.002)), 0.01)


def _swell(n, attack, release):
    ai, ri = int(SR * attack), int(SR * release)
    return curve(n, [(0, 0.0), (ai, 1.0), (max(ai, n - ri), 1.0), (n, 0.0)])


def pad(rng, chord, dur, attack, release, bright=0.3, detune=0.0015):
    """A sustained chord: two slightly detuned oscillators per note, each
    with a quiet octave, so the chord beats slowly against itself."""
    n = int(SR * dur)
    out = zeros(n)
    for m in chord:
        f = hz(m)
        for d in (1.0 - detune, 1.0 + detune):
            out = plus(out, tone(n, f * d, rng.uniform(0.0, 2.0 * math.pi)))
            out = plus(out, scaled(tone(n, 2.0 * f * d, rng.uniform(0.0, 2.0 * math.pi)), bright))
    return scaled(times(out, _swell(n, attack, release)), 1.0 / len(chord))


def horn(rng, chord, dur, attack, release, harmonics=6, rolloff=1.5):
    """A low brass-like swell: harmonics at 1/k^rolloff whose upper members
    open later than the fundamental (each follows the envelope raised to a
    higher power), so the chord brightens as it grows instead of starting
    buzzy. Slow attacks only: a swell, never a stab."""
    n = int(SR * dur)
    env = _swell(n, attack, release)
    out = zeros(n)
    for m in chord:
        f = hz(m)
        for k in range(1, harmonics + 1):
            if f * k > 5000.0:
                break
            opening = array.array("d", map(pow, env, itertools.repeat(1.0 + 0.5 * (k - 1))))
            partial = tone(n, f * k * (1.0 + 0.0012 * (k % 2)), rng.uniform(0.0, 2.0 * math.pi))
            out = plus(out, scaled(times(partial, opening), 1.0 / k ** rolloff))
    return scaled(out, 1.0 / len(chord))


def riser(rng, dur, noise_from, noise_to, tone_from, tone_to):
    """A build into a downbeat: noise through a band-pass sweeping upward
    under three detuned sines sweeping too (the superweapon_charge recipe),
    swelling to the end."""
    n = int(SR * dur)
    noise = band_pass(white_noise(dur, rng),
                      lambda i: noise_from + (noise_to - noise_from) * (i / n) ** 2, q=2.5)
    sweep = mix(*[gain(sine_sweep(dur, tone_from + d, tone_to + d * 2.0, curve=1.4), 0.33)
                  for d in (0.0, 3.0, -4.5)])
    swell = curve(n, [(0, 0.0), (n, 1.0)])
    return tail_fade(times(fit(mix(gain(noise, 0.6), sweep), n), times(swell, swell)), 0.08)


class ScoreTrack:
    """One score track under construction: a buffer exactly `bars` bars long,
    so the loop closes on a bar line. Notes are placed by bar and beat and
    wrap past the end (add_into); continuous layers are rendered
    LOOP_FADE_SECONDS longer and crossfaded into the head (bed). Either way
    the seam is continuous by construction, and _check_score_seam proves it."""

    def __init__(self, bpm, beats_per_bar, bars, seed):
        self.beat = 60.0 / bpm
        self.bar = self.beat * beats_per_bar
        self.n = int(round(SR * self.bar * bars))
        self.nf = int(SR * LOOP_FADE_SECONDS)
        self.buf = zeros(self.n)
        self.rng = random.Random(seed)
        self.sections = []

    @property
    def span(self):
        return self.n + self.nf

    def at(self, bar, beat=0.0):
        return int(round(SR * (bar * self.bar + beat * self.beat)))

    def hit(self, src, bar, beat=0.0, g=1.0):
        add_into(self.buf, src, self.at(bar, beat), g)

    def auto(self, points):
        """Bed automation from (bar, value) points over the bed's span."""
        return curve(self.span, [(self.at(b), v) for b, v in points])

    def bed(self, layer):
        """The ambient_wind loop idiom: the surplus tail is equal-power
        crossfaded into the head, so a drone never dips at the seam."""
        n, nf = self.n, self.nf
        layer = fit(layer, self.span)
        out = layer[:n]
        for i in range(nf):
            a = i / nf
            out[i] = out[i] * math.sin(a * math.pi / 2.0) + layer[n + i] * math.cos(a * math.pi / 2.0)
        add_into(self.buf, out, 0)

    def section(self, name, first_bar, end_bar):
        self.sections.append((name, first_bar, end_bar))


def drone(tr, voices, air=0.0):
    """The house bed: sine voices (midi, level, breath period s, breath
    depth, phase, bar automation or None), each breathing on a cycle that
    does not divide the track, plus optional low-passed brown-noise air."""
    n = tr.span
    out = zeros(n)
    for midi, level, period, depth, phase, shape in voices:
        voice = times(tone(n, hz(midi)), breathe(n, period, 1.0 - depth, depth, phase))
        if shape:
            voice = times(voice, tr.auto(shape))
        out = plus(out, scaled(voice, level))
    if air:
        wind = one_pole(brown(n, tr.rng), 420.0)
        out = plus(out, scaled(times(wind, breathe(n, 19.0, 0.67, 0.33, 2.9)), air))
    tr.bed(out)


def _chord_at(bar, progression, default):
    """The chord sounding at `bar` in a [(first bar, chord), ...] list."""
    current = default
    for first, chord in progression:
        if first <= bar:
            current = chord
    return current


# ---- The six tracks.

def score_ferrite_dawn():
    """CALM. D dorian, 72 BPM, 4/4, 54 bars (180.0 s). Lead timbre: struck
    bars. The P6 calm bed's voicing (D1, D2, A2 and air) is the floor, so the
    house sound survives the change; over it a slow pad progression and a
    four-note bell line that only ever plays chord tones.
      intro   0-8   the bed alone
      rise    8-24  the pad (Dm C G Dm; the major IV is the dorian colour), the bell line from bar 12
      break  24-32  the fifth drops out under one low Am7; single bells
      turn   32-48  a darker borrowed progression (Bb F C Dm), the line inverted, a soft pulse from bar 36
      settle 48-54  one open D chord released before the seam, so the loop lands on the bed it began on"""
    tr = ScoreTrack(bpm=72, beats_per_bar=4, bars=54, seed=4601)
    for sec in (("intro", 0, 8), ("rise", 8, 24), ("break", 24, 32), ("turn", 32, 48), ("settle", 48, 54)):
        tr.section(*sec)
    drone(tr, [(38, 0.30, 23.0, 0.20, 0.0, None),
               (45, 0.22, 31.0, 0.30, 1.1, [(0, 1.0), (24, 1.0), (26, 0.0), (30, 0.0), (32, 1.0)]),
               (26, 0.18, 41.0, 0.20, 2.3, None)], air=0.06)
    rise = [(8, (50, 57, 62, 65)), (12, (48, 55, 64, 67)), (16, (43, 55, 59, 62)), (20, (50, 57, 62, 65))]
    turn = [(32, (46, 53, 57, 62)), (36, (41, 53, 57, 60)), (40, (48, 55, 60, 64)), (44, (50, 57, 62, 65))]
    for bar, chord in rise + turn:
        tr.hit(pad(tr.rng, chord, 4 * tr.bar + 2.5, attack=2.0, release=2.5), bar, g=0.16)
    am7 = (45, 52, 55, 60)
    tr.hit(pad(tr.rng, am7, 8 * tr.bar + 2.5, attack=3.5, release=3.0), 24, g=0.12)
    tr.hit(pad(tr.rng, (50, 57, 64), 6 * tr.bar, attack=2.5, release=9.0), 48, g=0.14)

    line = motif(tr.rng, 4)
    for k, bar in enumerate(range(12, 24, 2)):
        tones = voiced(_chord_at(bar, rise, rise[0][1]), 69)
        notes = line if k % 2 == 0 else line[:3] + [line[3] + 1]
        for beat, i in zip((0.0, 1.5, 3.0, 6.0), notes):
            tr.hit(struck_bar(hz(chord_tone(tones, i))), bar, beat, g=0.22)
    for k, bar in enumerate((25, 27, 29, 31)):
        tr.hit(struck_bar(hz(chord_tone(voiced(am7, 69), (0, 2, 1, 3)[k]))), bar, 0.0, g=0.16)
    inverted = [4 - i for i in line]
    for k, bar in enumerate(range(32, 48, 2)):
        tones = voiced(_chord_at(bar, turn, turn[0][1]), 69)
        notes = inverted if k % 2 == 0 else inverted[::-1]
        for beat, i in zip((0.0, 2.0, 2.5, 5.0), notes):
            tr.hit(struck_bar(hz(chord_tone(tones, i))), bar, beat, g=0.20)
    tr.hit(struck_bar(hz(74), dur=5.0, tau=1.4), 48, 0.0, g=0.20)
    thump = war_drum(60.0, 40.0, 0.40, 0.10)
    for bar in range(36, 48):
        tr.hit(thump, bar, 0.0, g=0.22)
        tr.hit(thump, bar, 2.0, g=0.16)
    return tr


def score_slag_hollow():
    """CALM. F# aeolian, 66 BPM, 3/4, 66 bars (180.0 s). Lead timbre: a
    plucked stiff wire arpeggiating each chord over an F# pedal.
      intro   0-8   pedal and a sparse arpeggio in quarters
      flow    8-24  the arpeggio in eighths (F#m D A E, two bars each); a high pad from bar 12
      hollow 24-36  the arpeggio stops; the pad holds Bm then D; chimes placed by the seed
      return 36-56  a varied arpeggio, a low pluck on every downbeat, the pad
      fade   56-66  back to quarters at the intro's level, so the loop closes on it"""
    tr = ScoreTrack(bpm=66, beats_per_bar=3, bars=66, seed=4602)
    for sec in (("intro", 0, 8), ("flow", 8, 24), ("hollow", 24, 36), ("return", 36, 56), ("fade", 56, 66)):
        tr.section(*sec)
    drone(tr, [(42, 0.30, 29.0, 0.25, 0.0, None),
               (30, 0.18, 37.0, 0.20, 1.7, None),
               (49, 0.12, 43.0, 0.40, 0.6, None)], air=0.05)
    # (bass root, chord tones)
    cycle = ((42, (54, 57, 61)), (38, (50, 54, 57)), (45, (52, 57, 61)), (40, (52, 56, 59)))
    bm, d = (35, (47, 50, 54)), (38, (50, 54, 57))

    def chord_at(bar):
        if 24 <= bar < 30:
            return bm
        if 30 <= bar < 36:
            return d
        return cycle[(bar // 2) % 4]

    quarters = ((0.0, 0), (1.0, 1), (2.0, 2))
    flow = tuple((k * 0.5, i) for k, i in enumerate((0, 1, 2, 3, 2, 1)))
    varied = tuple((k * 0.5, i) for k, i in enumerate((0, 2, 1, 3, 2, 4)))
    for bar in range(66):
        if 24 <= bar < 36:
            continue
        root, chord = chord_at(bar)
        tones = voiced(chord, 54)
        if bar < 8:
            pattern, level = quarters, 0.20
        elif bar < 24:
            pattern, level = flow, 0.26
        elif bar < 56:
            pattern, level = varied, 0.26
        else:
            pattern, level = quarters, 0.30 - 0.01 * (bar - 56)
        for beat, i in pattern:
            tr.hit(wire_pluck(hz(chord_tone(tones, i))), bar, beat, g=level * (1.25 if beat == 0.0 else 1.0))
        if 36 <= bar < 56:
            tr.hit(wire_pluck(hz(root), dur=2.5, tau=0.9), bar, 0.0, g=0.30)
    for bar in list(range(12, 24, 2)) + list(range(36, 56, 2)):
        high = tuple(t + 12 for t in chord_at(bar)[1])
        tr.hit(pad(tr.rng, high, 2 * tr.bar + 1.5, attack=1.2, release=1.5), bar, g=0.13)
    tr.hit(pad(tr.rng, (59, 62, 66), 6 * tr.bar + 2.5, attack=2.5, release=2.5), 24, g=0.15)
    tr.hit(pad(tr.rng, (62, 66, 69), 6 * tr.bar + 2.5, attack=2.5, release=2.5), 30, g=0.15)
    for bar in range(24, 36):
        if tr.rng.random() < 0.55:
            beat = tr.rng.choice((0.0, 1.0, 1.5, 2.0))
            note = chord_tone(voiced(chord_at(bar)[1], 78), tr.rng.randrange(0, 5))
            tr.hit(struck_bar(hz(note), dur=3.5, tau=1.1), bar, beat, g=0.09)
    return tr


def score_watchfire():
    """CALM, the build. B aeolian, 84 BPM, 4/4, 63 bars (180.0 s). Lead
    timbre: a low FM bell. Tension that gathers and never breaks into
    combat: a soft heartbeat, a clock tick and a staccato bass ostinato.
      watch   0-8   bed, tick, a bell every four bars
      pulse   8-24  heartbeat and the bass ostinato (Bm G Em F#m, two bars each)
      gather 24-40  a pad over both, the heartbeat stronger
      hush   40-48  heartbeat and bass drop out under one held G chord; a riser from bar 44
      vigil  48-60  everything back, the tick doubled to eighths
      embers 60-63  bass and heartbeat stop; tick, bed and a last bell lead back to the watch"""
    tr = ScoreTrack(bpm=84, beats_per_bar=4, bars=63, seed=4603)
    for sec in (("watch", 0, 8), ("pulse", 8, 24), ("gather", 24, 40), ("hush", 40, 48),
                ("vigil", 48, 60), ("embers", 60, 63)):
        tr.section(*sec)
    drone(tr, [(35, 0.22, 27.0, 0.25, 0.4, None),
               (42, 0.18, 33.0, 0.30, 1.9, None),
               (47, 0.10, 47.0, 0.50, 0.2, [(0, 1.0), (40, 1.0), (42, 1.6), (47, 1.6), (48, 1.0)])], air=0.06)
    # (bass root, pad chord): Bm G Em F#m
    prog = ((35, (50, 54, 59, 62)), (31, (50, 55, 59, 62)), (40, (52, 55, 59, 64)), (42, (49, 54, 57, 61)))

    def chord_at(bar):
        return prog[(bar // 2) % 4]

    tk = tick(4200.0, 4631)
    heart = war_drum(70.0, 42.0, 0.35, 0.09)
    figure = (0, 0, 7, 0, 0, 0, 7, 12)
    for bar in range(63):
        for beat in range(4):
            tr.hit(tk, bar, beat, g=0.10 if beat == 0 else 0.07)
            if 48 <= bar < 60:
                tr.hit(tk, bar, beat + 0.5, g=0.05)
        if 8 <= bar < 60 and not 40 <= bar < 48:
            level = 0.30 if bar < 24 else (0.40 if bar < 40 else 0.50)
            tr.hit(heart, bar, 0.0, g=level)
            tr.hit(heart, bar, 0.5, g=level * 0.6)
            root = chord_at(bar)[0]
            bass_level = 0.30 if bar < 48 else 0.36
            for k, step in enumerate(figure):
                tr.hit(bass_note(hz(root + step), 0.30, 0.12), bar, k * 0.5,
                       g=bass_level * (1.2 if k == 0 else 1.0))
    for bar in list(range(24, 40, 2)) + list(range(48, 60, 2)):
        tr.hit(pad(tr.rng, chord_at(bar)[1], 2 * tr.bar + 2.0, attack=1.2, release=1.8), bar,
               g=0.12 if bar < 40 else 0.13)
    tr.hit(pad(tr.rng, (50, 55, 59, 62), 8 * tr.bar + 1.0, attack=2.5, release=2.0), 40, g=0.10)
    for bar in (0, 4, 24, 28, 32, 36, 44, 48, 52, 56, 60):
        root = voiced((chord_at(bar)[0],), 47)[0]
        tr.hit(fm_bell(hz(root)), bar, 0.0, g=0.22)
    tr.hit(riser(tr.rng, 4 * tr.bar, 300.0, 2500.0, hz(59), hz(71)), 44, g=0.20)
    return tr


def score_seven_hammers():
    """COMBAT. C aeolian, 7/8 grouped 2+2+3, eighth note 0.25 s, 103 bars
    (180.25 s). Lead timbre: struck anvils over the house war drum. The odd
    metre is the identity: the downbeat comes round an eighth early, every bar.
      intro   0-8    war drum and ticks; toms from bar 4
      drive   8-32   a staccato bass ostinato (Cm Ab Bb Cm, four bars each) over the full kit
      forge  32-56   anvils and low horn swells (Cm Fm Ab Bb)
      quench 56-68   one drum a bar under held horns; a riser from bar 64
      hammer 68-96   the full kit, the horns and a high plucked ostinato
      cool   96-103  drums alone, thinning back to the intro"""
    tr = ScoreTrack(bpm=240, beats_per_bar=7, bars=103, seed=4604)
    for sec in (("intro", 0, 8), ("drive", 8, 32), ("forge", 32, 56), ("quench", 56, 68),
                ("hammer", 68, 96), ("cool", 96, 103)):
        tr.section(*sec)
    drone(tr, [(36, 0.26, 29.0, 0.20, 0.0, [(0, 1.0), (56, 1.0), (58, 1.4), (67, 1.4), (68, 1.0)]),
               (43, 0.14, 37.0, 0.30, 1.3, None),
               (24, 0.16, 43.0, 0.20, 2.1, None)])
    drum = war_drum()
    t_lo, t_hi = tom(210.0, 0.16, 0.050, 4641), tom(300.0, 0.14, 0.042, 4642)
    tk = tick(4200.0, 4643)
    hammer = anvil(hz(79), 4644)
    # (bass root, ostinato in semitones, horn chord); every step stays in C aeolian
    cm = (36, (0, 0, 7, 0, 3, 7, 10), (48, 55, 63))
    ab = (32, (0, 0, 7, 0, 4, 7, 11), (44, 51, 60))
    bb = (34, (0, 0, 7, 0, 4, 7, 10), (46, 53, 62))
    fm = (41, (0, 0, 7, 0, 3, 7, 10), (41, 48, 56))

    def chord_at(bar):
        if bar < 32:
            return (cm, ab, bb, cm)[(max(bar - 8, 0) // 4) % 4]
        if bar < 56:
            return (cm, fm, ab, bb)[((bar - 32) // 4) % 4]
        if bar < 68:
            return cm if bar < 62 else ab
        return (cm, ab, fm, bb)[((bar - 68) // 4) % 4]

    for bar in range(103):
        quench = 56 <= bar < 68
        hammering = 68 <= bar < 96
        if quench:
            tr.hit(drum, bar, 0, g=0.8)
        else:
            for e, g in ((0, 1.0), (2, 0.65), (4, 0.85)):
                tr.hit(drum, bar, e, g)
            if hammering and bar % 2 == 0:
                tr.hit(drum, bar, 6, g=0.5)
        for e in range(7):
            if quench and e not in (0, 2, 4):
                continue
            tr.hit(tk, bar, e, g=0.20 if e in (0, 2, 4) else 0.11)
        if bar >= 4 and not quench:
            fade = 1.0 if bar < 96 else 1.0 - 0.08 * (bar - 96)
            tr.hit(t_lo, bar, 1, 0.40 * fade)
            tr.hit(t_hi, bar, 3, 0.35 * fade)
            tr.hit(t_lo, bar, 5, 0.30 * fade)
            if not (hammering and bar % 2 == 0):
                tr.hit(t_hi, bar, 6, 0.40 * fade)
            if bar % 4 == 3 and 8 <= bar < 96:   # a fill closes every fourth bar
                tr.hit(t_hi, bar, 5.5, 0.35)
                tr.hit(t_lo, bar, 6.0, 0.40)
                tr.hit(drum, bar, 6.5, 0.55)
        if 8 <= bar < 56 or hammering:
            root, figure, _ = chord_at(bar)
            for e, step in enumerate(figure):
                tr.hit(bass_note(hz(root + step), 0.22, 0.09), bar, e, g=0.42 if e in (0, 2, 4) else 0.34)
        if 32 <= bar < 56 or hammering:
            tr.hit(hammer, bar, 4, g=0.30)
            if bar % 4 == 0:
                tr.hit(hammer, bar, 0, g=0.42)
        if hammering:
            tones = voiced(chord_at(bar)[2], 72)
            for e, i in enumerate((0, 2, 1, 2, 0, 1, 3)):
                tr.hit(wire_pluck(hz(chord_tone(tones, i)), 1.2, 0.35), bar, e, g=0.13)
    for bar in list(range(32, 56, 4)) + list(range(68, 96, 4)):
        tr.hit(horn(tr.rng, chord_at(bar)[2], 4 * tr.bar + 1.5, attack=2.5, release=2.0), bar, g=0.12)
    tr.hit(horn(tr.rng, cm[2], 6 * tr.bar + 1.5, attack=3.0, release=2.5), 56, g=0.12)
    tr.hit(horn(tr.rng, ab[2], 6 * tr.bar + 1.5, attack=3.0, release=2.5), 62, g=0.12)
    tr.hit(riser(tr.rng, 4 * tr.bar, 250.0, 3000.0, hz(48), hz(60)), 64, g=0.22)
    return tr


def score_gantry_run():
    """COMBAT. E aeolian, 6/8, eighth note 0.25 s (80 dotted-quarter beats a
    minute), 120 bars (180.0 s). Lead timbre: a bowed low string section.
    A galloping compound metre: two drums a bar, tom pairs driving into each.
      intro    0-8    taiko and drums
      run      8-40   gallop toms, ticks and a bowed bass in dotted quarters (Em C D Em, two bars each)
      span    40-64   a string chorale (Am C D Bm C D, four bars each) and anvils
      hold    64-80   taiko every other bar under held strings; a riser from bar 76
      charge  80-112  everything, with a high plucked ostinato
      slow   112-120  drums and taiko, the toms thinning back to the intro"""
    tr = ScoreTrack(bpm=240, beats_per_bar=6, bars=120, seed=4605)
    for sec in (("intro", 0, 8), ("run", 8, 40), ("span", 40, 64), ("hold", 64, 80),
                ("charge", 80, 112), ("slow", 112, 120)):
        tr.section(*sec)
    drone(tr, [(40, 0.26, 31.0, 0.25, 0.3, [(0, 1.0), (64, 1.0), (66, 1.4), (79, 1.4), (80, 1.0)]),
               (28, 0.15, 41.0, 0.20, 1.2, None),
               (47, 0.10, 23.0, 0.40, 2.6, None)])
    taiko = war_drum(60.0, 34.0, 0.60, 0.15)
    drum = war_drum(95.0, 50.0, 0.25, 0.06)
    t_lo, t_hi = tom(260.0, 0.14, 0.045, 4651), tom(380.0, 0.12, 0.038, 4652)
    tk = tick(5200.0, 4653)
    clang = anvil(hz(76), 4654)
    # (bass root, string chord)
    em, c, d = (40, (52, 55, 59, 64)), (36, (48, 55, 60, 64)), (38, (50, 54, 57, 62))
    am, bm = (33, (45, 52, 57, 60)), (35, (47, 54, 59, 62))

    def chord_at(bar):
        if bar < 40:
            return (em, c, d, em)[(max(bar - 8, 0) // 2) % 4]
        if bar < 64:
            return (am, c, d, bm, c, d)[(bar - 40) // 4]
        if bar < 80:
            return em if bar < 72 else c
        if bar < 112:
            return (em, c, am, d)[((bar - 80) // 4) % 4]
        return em

    for bar in range(120):
        hold = 64 <= bar < 80
        charging = 80 <= bar < 112
        if bar < 8 or hold:
            if bar % 2 == 0:
                tr.hit(taiko, bar, 0, 0.9)
        elif charging:
            tr.hit(taiko, bar, 0, 0.85)
        elif bar % 4 == 0:
            tr.hit(taiko, bar, 0, 0.8)
        if not hold:
            tr.hit(drum, bar, 0, 1.0)
            tr.hit(drum, bar, 3, 0.75)
        if 8 <= bar < 64 or bar >= 80:
            fade = 1.0 if bar < 112 else 1.0 - 0.07 * (bar - 112)
            for e, g, t in ((1, 0.30, t_lo), (2, 0.42, t_hi), (4, 0.30, t_lo), (5, 0.42, t_hi)):
                tr.hit(t, bar, e, g * fade)
        if 8 <= bar < 112:
            for e in range(6):
                if hold and e not in (0, 3):
                    continue
                tr.hit(tk, bar, e, 0.18 if e in (0, 3) else 0.10)
        if 8 <= bar < 64 or charging:
            root = chord_at(bar)[0]
            tr.hit(bowed_bass(hz(root), 0.70), bar, 0, 0.40)
            tr.hit(bowed_bass(hz(root + (7 if bar % 2 else 0)), 0.70), bar, 3, 0.34)
        if (40 <= bar < 64 and bar % 2 == 0) or charging:
            tr.hit(clang, bar, 3, 0.28 if bar < 64 else 0.32)
        if charging:
            tones = voiced(chord_at(bar)[1], 64)
            for e, i in enumerate((0, 1, 2, 1, 3, 1)):
                tr.hit(wire_pluck(hz(chord_tone(tones, i)), 1.2, 0.35), bar, e, g=0.12)
    for bar in list(range(40, 64, 4)) + list(range(80, 112, 4)):
        tr.hit(horn(tr.rng, chord_at(bar)[1], 4 * tr.bar + 1.5, attack=1.5, release=1.5,
                    harmonics=5, rolloff=1.2), bar, g=0.11)
    tr.hit(horn(tr.rng, em[1], 8 * tr.bar + 1.5, attack=3.0, release=2.5, harmonics=5, rolloff=1.2), 64, g=0.11)
    tr.hit(horn(tr.rng, c[1], 8 * tr.bar + 1.5, attack=3.0, release=2.5, harmonics=5, rolloff=1.2), 72, g=0.11)
    tr.hit(riser(tr.rng, 4 * tr.bar, 300.0, 3200.0, hz(52), hz(64)), 76, g=0.22)
    return tr


def score_five_furnaces():
    """COMBAT. G aeolian, 5/4 grouped 3+2, 108 BPM, 65 bars (180.6 s). Lead
    timbre: a plucked FM metal bass. Five beats a bar with the second drum on
    beat four, so the pulse limps forward rather than marching.
      intro   0-4   drums alone
      stoke   4-20  the FM bass ostinato (Gm Eb F Dm, four bars each) and ticks
      blaze  20-36  low horn swells (Gm Cm Eb F) and a frame drum
      bank   36-44  one drum a bar, a pulse of fifths, held horns; a riser from bar 40
      melt   44-60  everything, with anvils, and the pulse again from bar 52
      draw   60-65  drums and toms alone, back to the intro"""
    tr = ScoreTrack(bpm=108, beats_per_bar=5, bars=65, seed=4606)
    for sec in (("intro", 0, 4), ("stoke", 4, 20), ("blaze", 20, 36), ("bank", 36, 44),
                ("melt", 44, 60), ("draw", 60, 65)):
        tr.section(*sec)
    drone(tr, [(31, 0.18, 29.0, 0.20, 0.5, [(0, 1.0), (36, 1.0), (38, 1.4), (43, 1.4), (44, 1.0)]),
               (38, 0.16, 35.0, 0.30, 1.6, None),
               (43, 0.10, 45.0, 0.40, 2.4, None)])
    drum = war_drum(78.0, 40.0, 0.32, 0.08)
    frame = plus(war_drum(70.0, 40.0, 0.30, 0.08), scaled(tom(150.0, 0.25, 0.07, 4661), 0.8))
    t_lo, t_hi = tom(170.0, 0.18, 0.055, 4662), tom(240.0, 0.15, 0.045, 4663)
    tk = tick(3800.0, 4664)
    clang = anvil(hz(79), 4665)
    # (bass root, ostinato in semitones over 3+2, horn chord); every step stays in G aeolian
    gm = (31, (0, 12, 7, 10, 7), (43, 50, 58))
    eb = (39, (0, 12, 7, 11, 7), (39, 46, 55))
    f = (29, (0, 12, 7, 10, 7), (41, 48, 57))
    dm = (38, (0, 12, 7, 10, 7), (38, 45, 53))
    cm = (36, (0, 12, 7, 10, 7), (36, 43, 51))

    def chord_at(bar):
        if bar < 20:
            return (gm, eb, f, dm)[(max(bar - 4, 0) // 4) % 4]
        if bar < 36:
            return (gm, cm, eb, f)[((bar - 20) // 4) % 4]
        if bar < 44:
            return gm if bar < 40 else eb
        return (gm, eb, f, dm)[((bar - 44) // 4) % 4]

    for bar in range(65):
        bank = 36 <= bar < 44
        melting = 44 <= bar < 60
        if bank:
            tr.hit(drum, bar, 0, 0.6)
        else:
            tr.hit(drum, bar, 0, 0.85)
            tr.hit(drum, bar, 3, 0.70)
            fade = 1.0 if bar < 60 else 1.0 - 0.1 * (bar - 60)
            tr.hit(t_hi, bar, 2, 0.38 * fade)
            tr.hit(t_lo, bar, 4.5, 0.34 * fade)
            if 20 <= bar < 36 or melting:
                tr.hit(frame, bar, 1.5, 0.30)
        if 4 <= bar < 60:
            for e in range(10):
                if bank and e not in (0, 6):
                    continue
                tr.hit(tk, bar, e * 0.5, 0.18 if e in (0, 6) else 0.09)
        if 4 <= bar < 36 or melting:
            root, figure, _ = chord_at(bar)
            for beat, step in enumerate(figure):
                tr.hit(fm_bass(hz(root + step), 0.45, 0.18), bar, beat, 0.36 if beat in (0, 3) else 0.30)
        if melting:
            tr.hit(clang, bar, 3, 0.32)
            if bar % 2 == 0:
                tr.hit(clang, bar, 0, 0.40)
        if bank or 52 <= bar < 60:
            for e in range(10):
                tr.hit(blip(hz(67 if e % 2 == 0 else 74)), bar, e * 0.5, 0.06)
    for bar in list(range(20, 36, 4)) + list(range(44, 60, 4)):
        tr.hit(horn(tr.rng, chord_at(bar)[2], 4 * tr.bar + 1.5, attack=2.0, release=2.0), bar, g=0.12)
    tr.hit(horn(tr.rng, gm[2], 4 * tr.bar + 1.5, attack=2.5, release=2.0), 36, g=0.12)
    tr.hit(horn(tr.rng, eb[2], 4 * tr.bar + 1.5, attack=2.5, release=2.0), 40, g=0.12)
    tr.hit(riser(tr.rng, 4 * tr.bar, 280.0, 3000.0, hz(50), hz(62)), 40, g=0.22)
    return tr


# The registry the client mirrors (AudioDirector.Score): file name, intensity.
SCORE = [
    ("calm_ferrite_dawn", "calm", score_ferrite_dawn),
    ("calm_slag_hollow", "calm", score_slag_hollow),
    ("calm_watchfire", "calm", score_watchfire),
    ("combat_seven_hammers", "combat", score_seven_hammers),
    ("combat_gantry_run", "combat", score_gantry_run),
    ("combat_five_furnaces", "combat", score_five_furnaces),
]


def _check_score_seam(name, samples):
    """The loop seam VERIFIED rather than trusted. At the -3 dBFS working
    scale, the step from the last sample back to the first must be no larger
    than twice the largest step in the 10 ms either side of it, or the P6
    limit of 0.02 where the music there is quieter. Music continuous across
    the seam passes; a seam that jumps raises rather than shipping a click."""
    k = PEAK_TARGET / (max(map(abs, samples)) or 1.0)
    w = int(SR * 0.01)
    tail, head = samples[-w:], samples[:w]
    local = k * max(max(abs(b - a) for a, b in zip(tail, tail[1:])),
                    max(abs(b - a) for a, b in zip(head, head[1:])))
    limit = max(0.02, 2.0 * local)
    step = k * abs(samples[0] - samples[-1])
    if step > limit:
        raise AssertionError("%s loop seam steps %.4f (limit %.4f): the loop would click"
                             % (name, step, limit))
    return step, limit


def _ffmpeg():
    exe = shutil.which("ffmpeg")
    if exe is None:
        raise SystemExit("synth.py: the score needs ffmpeg on PATH (its built-in Vorbis encoder "
                         "writes the .ogg files and its ebur128 filter measures loudness)")
    return exe


def _write_pcm16(path, samples):
    data = array.array("h", map(round, map(operator.mul, samples, itertools.repeat(32767.0))))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def _loudness(ffmpeg, path):
    """Integrated loudness (LUFS) and loudness range (LU), EBU R128."""
    res = subprocess.run([ffmpeg, "-hide_banner", "-nostats", "-i", path,
                          "-af", "ebur128=framelog=quiet", "-f", "null", "-"],
                         capture_output=True, text=True, check=True)
    lufs = float(re.findall(r"I:\s+(-?[\d.]+) LUFS", res.stderr)[-1])
    lra = float(re.findall(r"LRA:\s+([\d.]+) LU", res.stderr)[-1])
    return lufs, lra


def _db(x):
    return 20.0 * math.log10(x) if x > 0 else float("-inf")


def _limit(y, ceiling, lookahead=0.005, release=0.08):
    """A look-ahead peak limiter, treating the track as the loop it is.

    The gain each sample needs is r = min(1, ceiling / |y|). Its minimum
    over the next `lookahead` seconds, averaged over the previous
    `lookahead` seconds, never exceeds r at any sample (every averaging
    window that touches a peak holds only values at or below that peak's
    r), so no sample passes the ceiling and the gain ramps down over the
    look-ahead instead of stepping. Recovery is a one-pole release. The
    buffer is padded with its own tail in front and its own head behind,
    so the gain is continuous across the loop seam as well.

    Returns the limited samples and the deepest gain reduction in dB."""
    n = len(y)
    la = max(1, int(SR * lookahead))
    pre = min(n, int(SR * release * 10.0))
    ext = y[n - pre:] + y + y[:2 * la]
    m = len(ext)
    need = array.array("d", (ceiling / a if a > ceiling else 1.0 for a in map(abs, ext)))
    ahead = zeros(m)
    window = collections.deque()
    for j in range(m - 1, -1, -1):
        v = need[j]
        while window and need[window[-1]] >= v:
            window.pop()
        window.append(j)
        if window[0] >= j + la:
            window.popleft()
        ahead[j] = need[window[0]]
    del need, ext
    sums = array.array("d", itertools.accumulate(ahead, initial=0.0))
    del ahead
    smooth = array.array("d", (sums[i + 1] / (i + 1) for i in range(min(la - 1, m))))
    smooth.extend(map(operator.mul, map(operator.sub, sums[la:], sums[:m + 1 - la]),
                      itertools.repeat(1.0 / la)))
    del sums
    k = 1.0 - math.exp(-1.0 / (release * SR))
    level = 1.0
    gains = zeros(n)
    for i in range(m):
        v = smooth[i]
        level = v if v < level else level + k * (v - level)
        if pre <= i < pre + n:
            gains[i - pre] = level
    return times(y, gains), max(0.0, -_db(min(gains)))


def write_score(name, intensity, track):
    """Loudness match, limit, check and encode one track. ffmpeg's built-in
    Vorbis encoder writes two channels only, so the mono master is
    duplicated at unity gain, which Godot plays exactly as it plays a mono
    stream."""
    samples = track.buf
    seconds = len(samples) / SR
    if seconds < SCORE_MIN_SECONDS:
        raise AssertionError("%s is %.1f s, under the %.0f s floor" % (name, seconds, SCORE_MIN_SECONDS))
    target = SCORE_LUFS[intensity]
    ff = _ffmpeg()
    os.makedirs(MUSIC_DIR, exist_ok=True)
    path = os.path.join(MUSIC_DIR, name + ".ogg")
    peak = max(map(abs, samples)) or 1.0
    with tempfile.TemporaryDirectory(prefix="ferrostorm-score-") as tmp:
        master = os.path.join(tmp, "master.wav")
        _write_pcm16(master, scaled(samples, PEAK_TARGET / peak))
        lufs, _ = _loudness(ff, master)
        g = PEAK_TARGET / peak * 10.0 ** ((target - lufs) / 20.0)
        # Limiting takes a little loudness with the peaks, so measure again
        # and correct; two or three passes land within a tenth of a LU.
        for _ in range(4):
            final, reduction = _limit(scaled(samples, g), SCORE_PEAK_CEILING)
            _write_pcm16(master, final)
            got, lra = _loudness(ff, master)
            if abs(got - target) <= 0.1:
                break
            g *= 10.0 ** ((target - got) / 20.0)
        if reduction > SCORE_LIMIT_MAX_DB:
            raise AssertionError("%s needs %.1f dB of limiting to reach %.1f LUFS (cap %.1f dB): "
                                 "rebalance its mix" % (name, reduction, target, SCORE_LIMIT_MAX_DB))
        step, limit = _check_score_seam(name, final)
        subprocess.run([ff, "-hide_banner", "-loglevel", "error", "-y", "-i", master,
                        "-af", "pan=stereo|c0=c0|c1=c0",
                        "-c:a", "vorbis", "-strict", "-2", "-q:a", VORBIS_QUALITY,
                        "-fflags", "+bitexact", "-flags:a", "+bitexact", "-map_metadata", "-1",
                        path], check=True)
    print("%-22s %-6s %6.1f s  %6.1f LUFS  LRA %4.1f LU  peak %5.1f dBFS  limiting %3.1f dB  "
          "seam %.4f (limit %.4f)  %4d KB"
          % (name, intensity, seconds, got, lra, _db(max(map(abs, final))), reduction,
             step, limit, os.path.getsize(path) // 1024))
    profile = []
    for sec, first, end in track.sections:
        a, b = track.at(first), min(track.at(end), len(final))
        rms = math.sqrt(sum(map(operator.mul, final[a:b], final[a:b])) / max(1, b - a))
        profile.append("%s %d-%d %.1f" % (sec, first, end, _db(rms)))
    print("    sections (RMS dBFS): " + " | ".join(profile))


# ---------------------------------------------------------------------------

SOUNDS = [
    ("ui_click.wav", ui_click),
    ("ui_confirm.wav", ui_confirm),
    ("order_move.wav", order_move),
    ("shot_rifle.wav", shot_rifle),
    ("shot_cannon.wav", shot_cannon),
    ("shot_rocket.wav", shot_rocket),
    ("shot_heavy_mg.wav", shot_heavy_mg),          # P8-44
    ("shot_flak.wav", shot_flak),                  # P8-44
    ("shot_howitzer.wav", shot_howitzer),          # P8-44
    ("explosion_small.wav", explosion_small),
    ("explosion_large.wav", explosion_large),
    ("death_infantry.wav", death_infantry),        # P8-43
    ("collapse_rumble.wav", collapse_rumble),      # P8-43
    ("alert_attack.wav", alert_attack),
    ("alert_harvester.wav", alert_harvester),
    ("alert_low_power.wav", alert_low_power),
    ("alert_radar.wav", alert_radar),
    ("alert_jammed.wav", alert_jammed),
    ("production_done.wav", production_done),
    ("cue_promoted.wav", cue_promoted),            # P8-44
    ("cue_deployed.wav", cue_deployed),            # P8-44
    ("cue_placed.wav", cue_placed),                # P8-44
    ("superweapon_charge.wav", superweapon_charge),
    ("superweapon_impact.wav", superweapon_impact),
    ("ambient_wind.wav", ambient_wind),
]


def main():
    wanted = set(sys.argv[1:]) or {"sfx", "score"}
    unknown = wanted - {"sfx", "score"}
    if unknown:
        raise SystemExit("synth.py: unknown target %s (use sfx, score, or nothing for both)"
                         % ", ".join(sorted(unknown)))
    if "sfx" in wanted:
        os.makedirs(OUT_DIR, exist_ok=True)
        print("Rendering %d sounds to %s (peak -3 dBFS, 16-bit %d Hz mono)\n"
              % (len(SOUNDS), OUT_DIR, SR))
        for name, generator in SOUNDS:
            write_wav(name, generator())
    if "score" in wanted:
        print("\nRendering %d score tracks to %s (Ogg Vorbis q%s, loudness matched per intensity)\n"
              % (len(SCORE), MUSIC_DIR, VORBIS_QUALITY))
        for name, intensity, composer in SCORE:
            write_score(name, intensity, composer())
    print("\nDone.")


if __name__ == "__main__":
    main()
