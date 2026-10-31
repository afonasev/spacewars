#!/usr/bin/env python3
"""Reproduce locally authored concept-A collapse; no external recordings or service."""
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'unity/Assets/Spacewars/Resources/Sfx/Hybrid'
RATE = 24000
DURATION = 2.4


def main():
    rng = random.Random(25102026)
    low = scrape = 0.0
    values = []
    for i in range(round(RATE * DURATION)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low = low * .985 + noise * .015
        scrape = scrape * .75 + noise * .25
        # Initial structural impact, then settling metal/grit and restrained local bursts.
        x = (low * 8 + math.sin(2*math.pi*58*t)*.35) * math.exp(-t/.23)
        active = min(1, t/.12) * max(0, 1-t/1.95)
        x += active * (.32*scrape + .08*math.sin(2*math.pi*(135*t-16*t*t)))
        for j, at in enumerate((.18, .48, .86, 1.24, 1.65)):
            u = t-at
            if u >= 0:
                x += (.18*scrape + .07*math.sin(2*math.pi*(230+j*79)*u))*math.exp(-u/.07)
                x += low*2.0*math.exp(-u/.14)
        u = max(0, t-1.65)
        if t >= 1.65:
            x += .19*math.sin(2*math.pi*46*u)*math.exp(-u/.24)
        x *= min(1, t/.004)*min(1, (DURATION-t)/.06)
        values.append(x)
    peak = max(abs(x) for x in values)
    values = [x*.6/peak for x in values]
    path = OUT/'building-collapse-1.wav'
    with wave.open(str(path), 'wb') as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(b''.join(struct.pack('<h', round(x*32767)) for x in values))
    lines = (OUT/'order-1.wav.meta').read_text().splitlines()
    lines[1] = 'guid: '+hashlib.md5(b'spacewars:building-collapse-a-v1').hexdigest()
    path.with_suffix('.wav.meta').write_text('\n'.join(lines)+'\n')
    manifest = {'id': 'building-collapse-a-v1', 'authorship': 'Local deterministic oscillator/noise synthesis',
                'generator': 'tools/generate_building_collapse_audio.py', 'sample_rate': RATE,
                'seconds': DURATION, 'sample_peak': max(abs(x) for x in values),
                'rms': math.sqrt(sum(x*x for x in values)/len(values)),
                'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    (OUT/'building-collapse-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
