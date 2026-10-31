"""Matched actual Player gallery sheets; no resimulation or image retouching."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from distant_candidate_sheets import LABELS

LABELS.update(tankHybrid='Танк',hangar='Ангар')


def build(rows,source,output,title,pose):
    font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',17)
    small=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',15)
    head=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',26)
    canvas=Image.new('RGB',(1080,150+len(rows)*185),'#14232e');draw=ImageDraw.Draw(canvas)
    draw.text((20,15),title,font=head,fill='white')
    draw.text((20,53),'Реальный Player, один ракурс и масштаб. Дальний уровень увеличен для сравнения.',font=small,fill='#c5d8e5')
    for x,label in [(260,'Вблизи — исходная'),(530,'Обычная дистанция'),(810,'Сильное отдаление')]:draw.text((x,90),label,font=font,fill='white')
    for i,(row,upgraded) in enumerate(rows):
        y=130+i*185;key=row['key'];name=LABELS[key]
        if key=='hawk':name+=' — улучш.' if upgraded else ' — базовый'
        draw.text((15,y+25),name,font=small,fill='white')
        draw.text((15,y+65),f"{row['originalTriangles']:,} → {row['normalTriangles']:,}",font=small,fill='#88ddd9')
        draw.text((15,y+90),f"→ {row['farTriangles']:,} тр.",font=small,fill='#88ddd9')
        if key=='hawk':draw.text((15,y+115),'Оба комплекта в числах',font=small,fill='#c5d8e5')
        prefix=key+('-upgraded' if upgraded else '')+('-pose' if pose else '-rest')
        for level in range(3):
            image=Image.open(source/f'{prefix}-lod{level}.png').convert('RGB').resize((222,185),Image.Resampling.LANCZOS)
            canvas.paste(image,(250+level*275,y))
    draw.text((20,canvas.height-20),'Галерея ассетов; не игровой матч, не замер FPS. Пороги камеры ещё подлежат калибровке.',font=small,fill='#c5d8e5')
    canvas.save(output)


def main():
    p=argparse.ArgumentParser();p.add_argument('--frames',type=Path,required=True);p.add_argument('--metrics',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    units=[];buildings=[]
    for row in json.loads(a.metrics.read_text())['assets']:
        target=units if row['key'] in ['tankHybrid','tankHybridUpgraded','explorer','explorerUpgraded','shkval','shkval-guidance-3','hawk'] else buildings
        for upgrade in ([False,True] if row['key']=='hawk' else [False]):target.append((row,upgrade))
    for pose in [False,True]:
        for group,rows in [('units',units),('buildings',buildings)]:build(rows,a.frames,a.output/f'{group}-{"pose" if pose else "rest"}.png','Три уровня детализации: '+('юниты' if group=='units' else 'здания')+(' — повёрнутые узлы' if pose else ''),pose)


if __name__=='__main__':main()
