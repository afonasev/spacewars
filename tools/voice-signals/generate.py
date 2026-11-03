import asyncio, json, hashlib, shutil, subprocess
from pathlib import Path
import edge_tts
P=Path(__file__).resolve().parents[2]/'unity/Assets/Spacewars/Resources/Sfx/Voice'
samples={
'system-building-attacked':'Здание атаковано.','system-units-attacked':'Юниты атакованы.','system-building-ready':'Здание готово.','system-unit-ready':'Юнит готов.','system-research-ready':'Исследование готово.','system-unit-captured':'Юнит захвачен.','system-mine-captured':'Шахта захвачена.','system-relay-lost':'Вышка потеряна.','system-mine-hit':'Мина!','system-mine-triggered':'Мина сработала.','system-upgrade-ready':'Улучшение готово.','system-army-limit':'Лимит армии.','system-credits-needed':'Нет кредитов.','system-exit-blocked':'Выход заблокирован.',
'ally-attack':'Атаковать!','ally-defend':'Защищать!','ally-recon':'Разведать!','ally-advance':'Выдвигаюсь.','ally-engage':'Вступил в бой с противником!','ally-raid':'Начинаю рейд.','ally-retreat':'Отступаю.','ally-need-defense':'Нужна защита!'}
trim='silenceremove=start_periods=1:start_duration=0.01:start_threshold=-48dB,areverse,silenceremove=start_periods=1:start_duration=0.01:start_threshold=-48dB,areverse'
filters={'system':trim+',highpass=f=90,lowpass=f=11000,acompressor=threshold=0.14:ratio=2:attack=8:release=80:makeup=1.2,loudnorm=I=-18:TP=-3:LRA=7,aresample=48000,apad=pad_dur=0.08','ally':trim+',highpass=f=350,lowpass=f=3100,acompressor=threshold=0.08:ratio=4:attack=3:release=90:makeup=2.2,asoftclip=type=tanh:threshold=0.75:output=0.9,loudnorm=I=-18:TP=-3:LRA=7,aresample=48000,apad=pad_dur=0.08'}
preview=P/'approved-previews'
# Approved clips are committed directly; re-synthesis is opt-in for missing outputs.
reuse={'system-mine-hit':'system-mine','system-unit-captured':'system-captured','ally-advance':'ally-moving','ally-need-defense':'ally-defense'}
async def main():
 semaphore=asyncio.Semaphore(3)
 async def make(key,text):
  async with semaphore:
   family=key.split('-')[0]
   if key in reuse and (preview/(reuse[key]+'.wav')).exists():
    shutil.copyfile(preview/(reuse[key]+'.wav'),P/(key+'.wav'))
   else:
    raw=P/(key+'-raw.mp3')
    if not raw.exists():
     await asyncio.wait_for(edge_tts.Communicate(text,'ru-RU-SvetlanaNeural' if family=='system' else 'ru-RU-DmitryNeural',rate='+6%' if family=='system' else '+0%',pitch='+4Hz' if family=='system' else '-6Hz').save(str(raw)),60)
    subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-i',str(raw),'-af',filters[family],'-ac','1','-ar','48000','-c:a','pcm_s16le',str(P/(key+'.wav'))],check=True)
   print('Ready',key,flush=True)
 await asyncio.gather(*(make(k,t) for k,t in samples.items()))
 manifest={'generator':'edge-tts 7.3.1; offline game assets, no runtime service','female':'ru-RU-SvetlanaNeural +6% +4Hz','male':'ru-RU-DmitryNeural +0% -6Hz','filters':filters,'samples':[{'key':k,'text':t,'sha256':hashlib.sha256((P/(k+'.wav')).read_bytes()).hexdigest()} for k,t in samples.items()]}
 (P/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
asyncio.run(main())
