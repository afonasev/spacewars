"""Build offline AI match dialogue with the existing allied-radio voice."""
import argparse
import asyncio
import hashlib
import json
import re
import subprocess
import tempfile
import uuid
import wave
from pathlib import Path

import edge_tts
from edge_tts.exceptions import NoAudioReceived

PROJECT = Path(__file__).resolve().parents[2]
PRESENTATION = PROJECT / "unity/Assets/Spacewars/Presentation"
VOICE = PROJECT / "unity/Assets/Spacewars/Resources/Sfx/Voice"
PAUSE_FILTER = "silenceremove=stop_periods=-1:stop_duration=0.42:stop_threshold=-43dB:stop_silence=0.26"
MAX_SECONDS = 4.0


def duration(clip):
    with wave.open(str(clip)) as audio:
        return audio.getnframes() / audio.getframerate()


def encode(source, clip, filters):
    subprocess.run([
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(source),
        "-af", filters, "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", str(clip),
    ], check=True)


async def synthesize(text, raw, voice, rate, pitch):
    for attempt in range(3):
        try:
            await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch).save(str(raw))
            return
        except NoAudioReceived:
            if attempt == 2:
                raise
            await asyncio.sleep(attempt + 1)


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--greetings", action="store_true", help="Generate match greetings instead of farewells")
    parser.add_argument("--replace", action="store_true", help="Regenerate existing clips")
    parser.add_argument("--raw-dir", type=Path, help="Reuse matching source MP3s from a resumable synthesis run")
    args = parser.parse_args()
    family = "greeting" if args.greetings else "defeat"
    catalog_file = "AiGreetingLines.cs" if args.greetings else "AiDefeatLines.cs"
    max_seconds = 5.0 if args.greetings else MAX_SECONDS
    catalog = (PRESENTATION / catalog_file).read_text()
    lines = re.findall(r'new Line\("([^"]+)","([^"]+)"', catalog)
    if not lines:
        raise ValueError(f"No lines in {catalog_file}")
    approved = json.loads((VOICE / "manifest.json").read_text())
    voice, rate, pitch = approved["male"].split()
    radio_filter = approved["filters"]["ally"]
    importer = (VOICE / "ally-raid.wav.meta").read_text()
    samples = []
    if args.raw_dir:
        args.raw_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f"spacewars-{family}-") as temp:
        generated = {}
        for suffix, text in lines:
            key = f"{family}-{suffix}"
            clip = VOICE / f"{key}.wav"
            if args.replace or not clip.exists():
                raw = (args.raw_dir or Path(temp)) / f"{suffix}.mp3"
                if not raw.exists() or not raw.stat().st_size:
                    with tempfile.NamedTemporaryFile(dir=raw.parent, suffix=".mp3", delete=False) as pending:
                        pending_path = Path(pending.name)
                    try:
                        await synthesize(text, pending_path, voice, rate, pitch)
                        pending_path.replace(raw)
                    finally:
                        pending_path.unlink(missing_ok=True)
                output = Path(temp) / f"{key}.wav"
                encode(raw, output, radio_filter)
                if duration(output) > max_seconds:
                    encode(raw, output, f"{radio_filter},{PAUSE_FILTER}")
                if duration(output) > max_seconds:
                    raise ValueError(f"{key} is still longer than {max_seconds:g} seconds")
                print(f"{key} {duration(output):.2f}s", flush=True)
                generated[clip] = output

        for clip, output in generated.items():
            clip.write_bytes(output.read_bytes())

        for suffix, text in lines:
            key = f"{family}-{suffix}"
            clip = VOICE / f"{key}.wav"
            meta = clip.with_suffix(".wav.meta")
            if not meta.exists():
                meta.write_text(re.sub(r"(?m)^guid: [a-f0-9]+$", f"guid: {uuid.uuid4().hex}", importer))
            samples.append({"key": key, "text": text, "sha256": hashlib.sha256(clip.read_bytes()).hexdigest()})
    manifest = {"generator": approved["generator"], "voice": approved["male"], "filter": radio_filter,
                "long_pause_filter": PAUSE_FILTER, "samples": samples}
    (VOICE / f"{family}-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    asyncio.run(main())
