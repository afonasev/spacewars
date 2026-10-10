#!/usr/bin/env python3
"""Reproduce locally authored hybrid base/economy signals. No network or TTS.

Only writes this catalogue's WAV/importer files and manifest in Sfx/Hybrid.
"""
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'unity/Assets/Spacewars/Resources/Sfx/Hybrid'
SR = 24000
CATALOGUE = {
    'construction': (.55, (170, 430), 3), 'demolition': (.9, (65, 140), 3),
    'repair': (.55, (240, 720), 3), 'damage': (.4, (95, 510), 3),
    'motion-start': (.32, (55, 110), 1), 'motion-stop': (.28, (90, 45), 1),
    'queued': (.18, (420, 620), 1), 'work-start': (.22, (320, 540), 1),
    'ready': (.34, (520, 780, 1040), 1), 'cancel': (.22, (620, 370), 1),
    'warning': (.45, (430, 430, 580), 1), 'select': (.09, (610,), 1),
    'ui': (.12, (480,), 1), 'match-start': (.42, (260, 390, 520), 1),
    'pause': (.18, (520, 390), 1), 'result': (.65, (390, 520, 780), 1),
}

def samples(kind, duration, notes, variation):
    rng = random.Random(81200 + list(CATALOGUE).index(kind)*100 + variation)
    result = []
    mechanical = kind in ('construction', 'demolition', 'repair', 'damage')
    low = 0
    for i in range(round(duration*SR)):
        t = i/SR
        low = low*.93 + rng.uniform(-1, 1)*.07
        if mechanical:
            decay = .22 if kind == 'demolition' else .10
            x = low*5*math.exp(-t/decay)
            for j, f in enumerate(notes):
                x += .25/(j+1)*math.sin(2*math.pi*f*(1+.01*variation)*t)*math.exp(-t/(decay*1.2))
            # Tool/clank details; demolition stays short and dry, unlike combat explosion.
            for offset in (.10, .23):
                u=t-offset
                if u>=0:x+=.12*math.sin(2*math.pi*notes[-1]*1.7*u)*math.exp(-u/.025)
        elif kind.startswith('motion-'):
            a,b=notes
            x=.4*math.sin(2*math.pi*(a*t+(b-a)*t*t/(2*duration)))*math.sin(math.pi*t/duration)**2+low*.15
        else:
            step=duration/len(notes);index=min(len(notes)-1,int(t/step));u=t-index*step
            x=(math.sin(2*math.pi*notes[index]*u)+.13*math.sin(2*math.pi*notes[index]*2*u))*math.exp(-u/(step*.4))
        x*=min(1,t/.005)*min(1,(duration-t)/.025)
        result.append(x)
    peak=max(abs(x) for x in result)
    return [x*.60/peak for x in result]

def main():
    importer=(OUT/'order-1.wav.meta').read_text()
    assets=[]
    for kind,(duration,notes,count) in CATALOGUE.items():
        for variation in range(1,count+1):
            path=OUT/f'{kind}-{variation}.wav'
            values=samples(kind,duration,notes,variation)
            with wave.open(str(path),'wb') as f:
                f.setnchannels(1);f.setsampwidth(2);f.setframerate(SR)
                f.writeframes(b''.join(struct.pack('<h',round(x*32767)) for x in values))
            guid=hashlib.md5(('spacewars:hybrid-economy-v1:'+path.name).encode()).hexdigest()
            lines=importer.splitlines();lines[1]='guid: '+guid
            path.with_suffix('.wav.meta').write_text('\n'.join(lines)+'\n')
            assets.append({'file':path.name,'seconds':duration,'sample_peak':max(abs(x) for x in values),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    (OUT/'economy-manifest.json').write_text(json.dumps({'catalogue':'hybrid-economy-v1','authorship':'Local deterministic oscillator/noise synthesis; no external content','generator_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'sample_rate':SR,'assets':assets},indent=2)+'\n')
    print(f'Generated {len(assets)} local hybrid clips; sample peak <= 0.60')

if __name__=='__main__':main()
