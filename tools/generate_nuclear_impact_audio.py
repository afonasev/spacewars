#!/usr/bin/env python3
"""Render the mono orbital impact effect at 48 kHz from deterministic layers."""
import argparse
import math
import random
import struct
import wave
from pathlib import Path


def render():
    rate = 48_000
    duration = 2.75
    random_source = random.Random(1079)
    pressure_noise = 0.0
    grit_noise = 0.0
    low_noise = 0.0
    phase = 0.0
    samples = []
    for index in range(int(duration * rate)):
        t = index / rate
        white = random_source.uniform(-1, 1)
        pressure_noise += 0.035 * (white - pressure_noise)
        grit_noise += 0.21 * (white - grit_noise)
        low_noise += 0.007 * (white - low_noise)
        pressure = pressure_noise - low_noise
        grit = grit_noise - pressure_noise

        # A short crack carries the onset on small speakers. The falling pressure
        # wave and several later reflections give the impact its weight.
        crack = (white - grit_noise) * 0.65 * math.exp(-t / 0.025)
        body = (1 - math.exp(-t / 0.008)) * math.exp(-t / 0.32)
        phase += math.tau * (34 + 69 * math.exp(-t / 0.19)) / rate
        pressure_wave = body * (0.57 * math.sin(phase) + 0.21 * math.sin(phase * 2 + 0.9))
        reflected_pressure = sum(
            level * (1 - math.exp(-(t - delay) / 0.032)) * math.exp(-(t - delay) / decay)
            for delay, level, decay in [(0.063, 1.25, 0.25), (0.17, 0.92, 0.39), (0.33, 0.66, 0.53)]
            if t > delay
        )
        air = grit * (0.76 * math.exp(-t / 0.14) + 0.22 * math.exp(-t / 0.67))
        rumble = low_noise * (1 - math.exp(-t / 0.13)) * math.exp(-t / 0.71) * 2.1
        tail = min(1.0, (duration - t) / 0.23)
        start = min(1.0, t / 0.0018)
        sample = crack + pressure_wave + pressure * reflected_pressure + air + rumble
        samples.append(math.tanh(sample * 1.55) * start * tail * tail)
    scale = 0.72 / max(abs(sample) for sample in samples)
    return rate, [round(sample * scale * 32767) for sample in samples]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    rate, samples = render()
    with wave.open(str(args.output), "wb") as clip:
        clip.setparams((1, 2, rate, len(samples), "NONE", "not compressed"))
        clip.writeframes(struct.pack(f"<{len(samples)}h", *samples))


if __name__ == "__main__":
    main()
