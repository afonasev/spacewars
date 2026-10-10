#!/usr/bin/env python3
"""Generate coherent Spacewars adaptive music pairs and menu asset (local SA3 MLX)."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import time
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

ROOT = Path(__file__).resolve().parents[1]
PROD = ROOT / "docs/music-concepts/production-v1"
UNITY = ROOT / "unity/Assets/Spacewars/Resources/Music"
RUNTIME = Path.home() / ".local/share/star-tournament-music/stable-audio-3/optimized/mlx"
PYTHON = RUNTIME / ".venv/bin/python"
SA3 = RUNTIME / "scripts/sa3_mlx.py"
SR = 44100
NFFT = 2048
HOP = 512
CONCEPTS = [
    ("unknown-sector", "Неизвестный сектор", 910901,
     "Instrumental science fiction real time strategy game soundtrack, no vocals, cinematic electronic orchestral hybrid, clean spacious production, deep clean bass, memorable restrained motif in D minor, 112 BPM, 4/4. Cosmic mystery, alien intelligence, shimmering analog synthesizers, delicate sequencer arpeggios, distant strings and subtle brass. A single continuous 180 second composition that develops naturally from quiet exploration into energetic battle and settles into a reflective ending. Tight electronic drums and rolling synth bass in the energetic passages. Keep one coherent melody and harmonic progression throughout; no abrupt cuts, no sound effects, no distortion."),
    ("steel-frontier", "Стальной рубеж", 910902,
     "Instrumental science fiction real time strategy soundtrack, no vocals, cinematic orchestral electronic hybrid, D minor, 112 BPM, 4/4, 180 seconds. Noble courage, determined resilience and hope under pressure; warm low strings, restrained French horns carrying a memorable motif, deep toms and a subtle futuristic synth undercurrent. One coherent composition gradually grows from spacious quiet reconnaissance to lively battle with string ostinatos and controlled powerful percussion, then returns to calm. Preserve the same motif, harmony and pulse across the entire composition. Earnest and dignified, no bombastic trailer drops, no sound effects, clean production."),
    ("shadow-protocol", "Теневой протокол", 910913,
     "180 second instrumental science fiction strategy game cue, no vocals, D minor, 112 BPM, 4/4. Espionage, betrayal, danger and cosmic mystery balanced with resolve. Dark atmospheric synth pads, sparse low cello carrying a memorable recurring motif, intricate restrained electronic details. One coherent continuous composition that gradually intensifies into urgent syncopated electronic drums, pulsing strings and clean driving bass, then resolves to a quiet spacious reflection. Keep the original melody, harmonic timeline and pulse throughout; no sudden cuts, no sound effects, no distortion."),
    ("through-fire", "Сквозь огонь", 911004,
     "Instrumental cinematic science fiction strategy game soundtrack, no vocals, 180 seconds, D minor, 112 BPM, 4/4. A distinct theme of noble courage and costly perseverance: intimate piano motif, warm strings and measured French horn, slowly gathering into an energetic forward-moving battle with precise drums and string ostinato, then a calm determined coda. Space remains vast and mysterious beneath the human warmth. One coherent melodic and harmonic timeline, natural development, clean deep bass, no trailer impacts, no sound effects, no abrupt edits."),
    ("last-bastion", "Последний бастион", 911005,
     "Instrumental science fiction real time strategy game soundtrack, no vocals, 180 seconds, D minor, 112 BPM, 4/4. Distinct atmosphere of a remote fortress at the edge of unexplored space: solemn low brass, glassy synthesizer, slow memorable ascending motif suggesting hope amid danger. Develop one continuous composition from quiet watchfulness to a driving energetic defense with strong measured percussion and layered strings, then return to spacious calm. Mysterious, noble, resilient, tactically focused; same motif, harmony and pulse throughout, clean production, no sound effects, no distortion."),
]
MENU = ("menu", "Spacewars — главное меню", 911006,
        "Instrumental peaceful science fiction main menu music, no vocals, 180 seconds, D minor, 88 BPM, 4/4. Quiet cosmic mystery and noble hope: a delicate piano and soft glassy analog synth introduce a memorable gentle motif, warm restrained strings and distant French horn gradually add a sense of courage and wonder. Keep it contemplative and welcoming, spacious and unhurried, with subtle electronic shimmer and a soft clean bass foundation. One coherent calm composition with no battle drums, no sudden changes, no sound effects, no distortion.")


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def run(args: list[str]) -> None:
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)


def measure(path: Path) -> dict:
    p = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(path), "-af",
                        "loudnorm=I=-20:TP=-3:LRA=11:print_format=json", "-f", "null", "-"],
                       text=True, capture_output=True, check=True)
    s = p.stderr[p.stderr.rfind("{"):]
    return json.JSONDecoder().raw_decode(s)[0]


def separate(x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Median-filter STFT HPSS with soft Wiener masks; outputs remain sample aligned."""
    if x.ndim == 2:
        mono = x.mean(axis=1)
    else:
        mono = x
    _, _, z = signal.stft(mono, fs=SR, window="hann", nperseg=NFFT,
                          noverlap=NFFT-HOP, boundary="zeros", padded=True)
    mag = np.abs(z)
    harm = signal.medfilt2d(mag, kernel_size=(1, 31))
    perc = signal.medfilt2d(mag, kernel_size=(31, 1))
    h2 = harm * harm
    p2 = perc * perc
    den = h2 + p2 + 1e-12
    zh = z * (h2 / den)
    zp = z * (p2 / den)
    _, h = signal.istft(zh, fs=SR, window="hann", nperseg=NFFT,
                        noverlap=NFFT-HOP, input_onesided=True, boundary=True)
    _, p = signal.istft(zp, fs=SR, window="hann", nperseg=NFFT,
                        noverlap=NFFT-HOP, input_onesided=True, boundary=True)
    n = len(mono)
    h, p = h[:n], p[:n]
    # Avoid channel-dependent phase artifacts: mono-to-stereo centered reproduction.
    return np.repeat(h[:, None], 2, axis=1), np.repeat(p[:, None], 2, axis=1)


def cyclic_loop(x: np.ndarray, seconds: float = 4.0) -> np.ndarray:
    """Circular overlap crossfade; rotate blend into loop interior to keep exact seam continuity."""
    n = int(seconds * SR)
    if len(x) < n * 3:
        raise ValueError("audio too short for loop crossfade")
    t = np.linspace(0, 1, n, endpoint=False, dtype=np.float32)[:, None]
    # Equal-power blend from tail to head, applied identically to both stems.
    blended = x[-n:] * np.cos(t * np.pi / 2) + x[:n] * np.sin(t * np.pi / 2)
    return np.concatenate((x[n:-n], blended), axis=0)


def make_adaptive_preview(ident: str, bed: np.ndarray, pulse: np.ndarray) -> None:
    """Render controller gains: calm .55 bed, battle .85 bed + .85 pulse."""
    total = np.zeros_like(bed[:SR*60])
    total[:SR*12] = .55 * bed[:SR*12]
    n = SR * 6
    t = np.linspace(0, 1, n, endpoint=False, dtype=np.float32)[:, None]
    total[SR*12:SR*18] = bed[SR*12:SR*18] * (.55 + .30*t) + pulse[SR*12:SR*18] * (.85*t)
    total[SR*18:SR*42] = .85 * (bed[SR*18:SR*42] + pulse[SR*18:SR*42])
    total[SR*42:SR*48] = bed[SR*42:SR*48] * (.85 - .30*t) + pulse[SR*42:SR*48] * (.85*(1-t))
    total[SR*48:SR*60] = .55 * bed[SR*48:SR*60]
    wav = PROD / f".{ident}-preview.wav"
    sf.write(wav, total, SR, subtype="PCM_16")
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(wav),
         "-c:a", "libmp3lame", "-b:a", "192k", str(PROD / f"{ident}-adaptive-preview.mp3")])
    wav.unlink()


def export_pair(ident: str, master: Path) -> dict:
    x, sr = sf.read(master, dtype="float32", always_2d=True)
    if sr != SR:
        raise ValueError(f"unexpected sample rate {sr}")
    bed_raw, pulse_raw = separate(x)
    # Trim leading silence from both layers at one shared frame boundary.
    frame = int(.01 * SR)
    mix = bed_raw + pulse_raw
    rms = np.sqrt(np.mean(mix[:len(mix)//frame*frame].reshape(-1, frame, 2)**2, axis=(1, 2)))
    onset_frames = np.flatnonzero(rms >= max(float(rms.max()) * 10**(-50/20), 1e-5))
    trim = int(onset_frames[0] * frame) if len(onset_frames) else 0
    bed, pulse = (cyclic_loop(s[trim:]) for s in (bed_raw, pulse_raw))
    # Estimate gain from the reconstructed full mix and constrain peak headroom.
    reconstructed = bed + pulse
    temp_mix = PROD / f".{ident}-mix-measure.wav"
    sf.write(temp_mix, reconstructed, SR, subtype="PCM_16")
    m = measure(temp_mix)
    temp_mix.unlink()
    integrated = float(m["input_i"])
    peak = float(m["input_tp"])
    gain_db = min(-20.0 - integrated, -3.0 - peak)
    gain = 10 ** (gain_db / 20)
    bed *= gain
    pulse *= gain
    amp = max(float(np.max(np.abs(bed))), float(np.max(np.abs(pulse))), float(np.max(np.abs(bed+pulse))))
    if amp >= 1:
        raise ValueError(f"clipping risk: {amp}")
    for layer, audio in (("bed", bed), ("pulse", pulse)):
        out = UNITY / f"{ident}-{layer}.ogg"
        tmp = PROD / f".{ident}-{layer}.wav"
        sf.write(tmp, audio, SR, subtype="PCM_24")
        run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(tmp),
             "-strict", "-2", "-c:a", "vorbis", "-q:a", "6", str(out)])
        tmp.unlink()
    shipped_bed, sr = sf.read(UNITY/f"{ident}-bed.ogg", dtype="float32", always_2d=True)
    shipped_pulse, sr2 = sf.read(UNITY/f"{ident}-pulse.ogg", dtype="float32", always_2d=True)
    if sr != sr2 or sr != SR or len(shipped_bed) != len(shipped_pulse):
        raise ValueError("shipped stems are not sample-aligned")
    make_adaptive_preview(ident, shipped_bed, shipped_pulse)
    return {"integrated_lufs_pre_gain": integrated, "true_peak_dbtp_pre_gain": peak,
            "common_pair_gain_db": gain_db, "max_abs_sample_post_gain": amp,
            "loop_seconds": len(bed)/SR, "sample_rate": SR, "shared_leading_trim_seconds": trim/SR,
            "onset_after_trim_seconds": 0.0, "bed_rms_first_200ms": float(np.sqrt(np.mean(bed[:int(.2*SR)]**2))),
            "bed_sha256": sha(UNITY/f"{ident}-bed.ogg"),
            "pulse_sha256": sha(UNITY/f"{ident}-pulse.ogg"),
            "preview_sha256": sha(PROD/f"{ident}-adaptive-preview.mp3")}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", choices=[x[0] for x in CONCEPTS] + ["menu"])
    ap.add_argument("--process-existing", action="store_true")
    args = ap.parse_args()
    PROD.mkdir(parents=True, exist_ok=True)
    UNITY.mkdir(parents=True, exist_ok=True)
    items = CONCEPTS + [MENU]
    report = {"status": "technical-assets-awaiting-human-listening", "model": "Stable Audio 3 sm-music / same-s MLX",
              "steps": 8, "duration_seconds": 180, "sample_rate": SR,
              "ffmpeg_version": subprocess.run(["ffmpeg", "-version"], text=True, capture_output=True).stdout.splitlines()[0],
              "separation": "SciPy STFT median-filter HPSS, Wiener masks; mono analysis reproduced as centered stereo",
              "loop_method": "trim one shared leading-silence offset, 4-second equal-power circular overlap, rotate blended region to loop interior; identical offsets for aligned layers",
              "items": []}
    for ident, title, seed, prompt in items:
        if args.only and args.only != ident:
            continue
        master = PROD / f"{ident}-master.flac"
        elapsed = time.time()
        if not args.process_existing and not master.exists():
            raw = PROD / f".{ident}.wav"
            print(f"Generating {ident}", flush=True)
            run([str(PYTHON), str(SA3), "--prompt", prompt, "--dit", "sm-music", "--decoder", "same-s",
                 "--seconds", "180", "--steps", "8", "--seed", str(seed), "--out", str(raw)])
            run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(raw), "-c:a", "flac", str(master)])
            raw.unlink()
        print(f"Processing {ident}", flush=True)
        metrics = export_pair(ident, master) if ident != "menu" else export_menu(ident, master)
        report["items"].append({"id": ident, "title": title, "seed": seed, "prompt": prompt,
                                "master": {"sha256": sha(master), "bytes": master.stat().st_size},
                                "metrics": metrics, "elapsed_seconds": round(time.time()-elapsed, 1)})
        (PROD/"manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")
        print(f"Finished {ident} ({time.time()-elapsed:.0f}s)", flush=True)


def export_menu(ident: str, master: Path) -> dict:
    x, sr = sf.read(master, dtype="float32", always_2d=True)
    if sr != SR:
        raise ValueError(f"unexpected sample rate {sr}")
    tmp = PROD / f".{ident}.wav"
    sf.write(tmp, x, SR, subtype="PCM_24")
    out = UNITY / "menu.ogg"
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(tmp), "-strict", "-2", "-c:a", "vorbis", "-q:a", "6", str(out)])
    preview = PROD / "menu-preview.mp3"
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(master), "-t", "60", "-c:a", "libmp3lame", "-b:a", "192k", str(preview)])
    tmp.unlink()
    return {"sample_rate": SR, "duration_seconds": len(x)/SR, "menu_sha256": sha(out), "preview_sha256": sha(preview)}


if __name__ == "__main__":
    main()
