"""Build comparable review sheets from the Blender candidate renders (Pillow)."""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
LABELS = {
    'tankHybridUpgraded': 'Танк — улучшенный', 'explorer': 'Исследователь',
    'explorerUpgraded': 'Исследователь — улучшенный', 'shkval': 'Шквал',
    'shkval-guidance-3': 'Шквал — улучшенный', 'hawk': 'Ястреб',
    'headquarters-a': 'Штаб', 'factory-armored': 'Фабрика', 'mine-armored': 'Шахта',
    'outpost-b': 'Форпост', 'scientificCenter': 'Научный центр',
    'refinery': 'Перерабатывающий завод', 'refineryUpgraded': 'Завод — улучшенный',
    'sentry-turret': 'Защитная турель',
}


def build(rows, source, output, title):
    font_path = '/System/Library/Fonts/Supplemental/Arial.ttf'
    font = ImageFont.truetype(font_path, 20)
    small = ImageFont.truetype(font_path, 15)
    head = ImageFont.truetype(font_path, 27)
    canvas = Image.new('RGB', (1050, 180 + len(rows) * 200), '#14232e')
    draw = ImageDraw.Draw(canvas)
    draw.text((24, 18), title, font=head, fill='white')
    draw.text((24, 56), 'Blender-концепт. Не игровой кадр. Слева и в центре — один ракурс и масштаб.', font=small, fill='#bdcdd8')
    draw.text((300, 92), 'Исходник', font=font, fill='white')
    draw.text((560, 92), 'Дальний LOD, увеличено', font=font, fill='white')
    draw.text((825, 92), 'Около 32 px*', font=font, fill='white')
    for i, (row, variant) in enumerate(rows):
        y = 135 + 200 * i
        key = row['key']
        draw.line((20, y, 1030, y), fill='#3c505f')
        name = LABELS[key]
        if key == 'hawk':
            name += ' — базовый' if variant == 'base' else ' — улучшенный'
        draw.text((20, y + 22), name, font=small, fill='white')
        before, after = row['before']['triangles'], row['after']['triangles']
        draw.text((20, y + 61), f'{before:,} → {after:,} тр.', font=small, fill='#88ddd9')
        draw.text((20, y + 86), f'−{100 * (1 - after / before):.1f}% геометрии', font=small, fill='#88ddd9')
        if key == 'hawk':
            draw.text((20, y + 112), 'Числа для обоих комплектов', font=small, fill='#bdcdd8')
        for j, detail in enumerate(['full', 'distant']):
            image = Image.open(source / f'{key}-{variant}-{detail}.png').convert('RGB')
            view = image.resize((240, 200), Image.Resampling.LANCZOS)
            canvas.paste(view, (280 + j * 260, y + 1))
            # Equal downsampling of exactly the same source frames. This is a
            # reading aid, not Unity's projection/lighting or a measured threshold.
            tiny = image.resize((48, 40), Image.Resampling.LANCZOS)
            canvas.paste(tiny, (845 + j * 95, y + 66))
        draw.text((837, y + 119), 'исходник    LOD', font=small, fill='#bdcdd8')
    draw.text((24, canvas.height - 25), '* Одинаковое уменьшение кадров; точный порог и внешность в Unity проверяются отдельно.', font=small, fill='#bdcdd8')
    canvas.save(output)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--renders', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rows = json.loads((ROOT / 'design/unit-building-lod/candidates/manifest.json').read_text())['assets']
    units, buildings = [], []
    for i, row in enumerate(rows):
        target = units if i < 6 else buildings
        for variant in (['base', 'upgraded'] if row['key'] == 'hawk' else ['default']):
            target.append((row, variant))
    build(units, args.renders, args.output / 'units.png', 'Юниты: исходная модель → предлагаемый дальний LOD')
    build(buildings, args.renders, args.output / 'buildings.png', 'Здания: исходная модель → предлагаемый дальний LOD')


if __name__ == '__main__':
    main()
