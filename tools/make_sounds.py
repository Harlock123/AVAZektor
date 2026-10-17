#!/usr/bin/env python3
"""Synthesises AVAZektor's sound effects and tunes into ../src/AVAZektor/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. Mission control's voice comes from the formant voice in speech.py, the way the
talking arcade games of the day spoke, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAZektor", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out



def echoed(dry, taps=((0.03, 0.15),)):
    """A little ring of the loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.3)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


# Mission control's lines, written out as phonemes.
LINES = {
    "Zone1": "Z OW1 N , W AH1 N !",
    "Zone2": "Z OW1 N , T UW1 !",
    "Zone3": "Z OW1 N , TH R IY1 !",
    "Zone4": "Z OW1 N , F AO1 R !",
    "Zone5": "Z OW1 N , F AA1 IY V !",
    "Zone6": "Z OW1 N , S IH1 K S !",
    "Zone7": "Z OW1 N , S EH1 V AH N !",
    "Zone8": "Z OW1 N , EY1 T !",
    "SayFortress": "F AO1 R T R EH S , AH HH EH1 D !",
    "SaySeekers": "W AO1 R N IH NG , S IY1 K ER Z !",
    "SayCity": "S IH1 T IY , AH1 N D ER AH T AE1 K !",
    "SayClear": "Z OW1 N , K L IY1 R !",
    "SayVictory": "F AO1 R T R EH S , D IH S T R AO1 IY D !",
    "SayDefend": "D IH F EH1 N D , D AH S IH1 T IY Z !",
}


def control(words):
    """Mission control: a clipped, metallic voice, a fifth above it humming along, the way the talking machines sounded."""
    voice = say(words, rate=RATE, pitch=118, speed=1.05, size=0.96)
    hum = say(words, rate=RATE, pitch=177, speed=1.05, size=0.96, seed=11)
    return echoed([a + 0.18 * b for a, b in zip(voice, hum)])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship.
    write("Fire.wav", tone(0.12, 1800, 500, "square", attack=0.001, release=1.2, gain=0.6), 0.2)
    write("Thrust.wav", tone(0.26, 140, 110, "noise", attack=0.03, release=0.5, gain=0.6), 0.18)
    write("Explode.wav", mix(tone(1.2, 2600, 120, "noise", attack=0.002, release=0.5), tone(1.0, 110, 30, "saw", vibrato=0.2, vib_rate=14, release=0.6, gain=0.6)), 0.5)
    write("ExtraShip.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E6", "B6", "E7", "B6", "E7"]]), 0.3)
    # Probes, seekers, the city.
    write("EnemyFire.wav", tone(0.16, 900, 1400, "square", attack=0.001, vibrato=0.08, vib_rate=40, release=1.0, gain=0.5), 0.18)
    write("ProbeHit.wav", mix(tone(0.3, 1800, 200, "noise", attack=0.001, release=0.9), tone(0.25, 600, 90, "square", release=0.8, gain=0.5)), 0.34)
    write("Dive.wav", tone(0.7, 1600, 300, "sine", vibrato=0.04, vib_rate=10, release=0.5, gain=0.7), 0.2)
    write("SeekerCome.wav", tone(0.8, 300, 900, "tri", vibrato=0.15, vib_rate=16, release=0.4), 0.22)
    write("SeekerHit.wav", mix(tone(0.4, 3000, 300, "noise", attack=0.001, release=0.8), tone(0.35, 1200, 200, "tri", vibrato=0.2, vib_rate=30, release=0.7, gain=0.6)), 0.34)
    write("TowerFall.wav", mix(tone(0.9, 900, 80, "noise", attack=0.01, release=0.4), tone(0.8, 80, 35, "saw", release=0.5, gain=0.6)), 0.42)
    write("Bonus.wav", tone(0.08, hz("A6"), kind="square", attack=0.001, release=0.6, gain=0.6), 0.2)
    # The fortress.
    write("FortressHit.wav", tone(0.08, 2400, 1600, "square", attack=0.001, release=1.5, gain=0.5), 0.18)
    write("Breach.wav", mix(tone(0.25, 200, 70, "saw", attack=0.002, release=0.8), tone(0.2, 2500, 600, "noise", release=1.0, gain=0.5)), 0.4)
    write("FortressDie.wav", cat(mix(tone(2.0, 3000, 60, "noise", attack=0.002, release=0.35), tone(1.8, 90, 25, "saw", vibrato=0.3, vib_rate=10, release=0.4, gain=0.7))), 0.55)
    # Mission control.
    for name, words in LINES.items():
        write(f"{name}.wav", control(words), 0.75)

    # The title tune (an original composition): a cool, pulsing line in A minor over an arpeggio, looping.
    lead = [("-", 1), ("A5", 0.5), ("C6", 0.5), ("E6", 1), ("D6", 0.5), ("C6", 0.5), ("B5", 2), ("-", 2),
            ("-", 1), ("G5", 0.5), ("B5", 0.5), ("D6", 1), ("C6", 0.5), ("B5", 0.5), ("A5", 2), ("-", 2),
            ("-", 1), ("F5", 0.5), ("A5", 0.5), ("C6", 1), ("B5", 0.5), ("A5", 0.5), ("G#5", 2), ("-", 2),
            ("A5", 0.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 1), ("G#5", 1), ("A5", 2), ("-", 2)]
    arp = [("A2", 0.5), ("E3", 0.5), ("A3", 0.5), ("E3", 0.5)] * 4 + [("G2", 0.5), ("D3", 0.5), ("G3", 0.5), ("D3", 0.5)] * 4 \
        + [("F2", 0.5), ("C3", 0.5), ("F3", 0.5), ("C3", 0.5)] * 4 + [("E2", 0.5), ("B2", 0.5), ("E3", 0.5), ("B2", 0.5)] * 3 + [("A2", 2)]
    write("TitleTune.wav", cat(tune(120, lead, arp, kind="pulse"), silence(0.5)), 0.36)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
