"""Merge translations/*.json -> th.json and check them against the game's English text.

Checks (errors stop the build):
  - duplicate keys across files
  - {placeholders} and <rich text tags> must match the English text exactly
Warnings:
  - keys that do not exist in the game's English tables (typo / removed by a game update)
  - English keys that have no Thai text yet
"""
import json, re, sys, glob, os, collections

here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
en_path = os.path.join(here, 'reference', 'en.json')
out_path = os.path.join(here, 'th.json')

PLACEHOLDER = re.compile(r'\{[^{}]*\}')
TAG = re.compile(r'<[^<>]*>')


def alternates(key):
    if key.endswith('_PLURAL'):
        stem = key[:-7]
        yield stem
        yield stem + '_SINGULAR'
        if stem.endswith('_F'):
            yield stem[:-2] + '_M_SINGULAR'
    if key.endswith('_F_SINGULAR'):
        yield key[:-11] + '_M_SINGULAR'


en = json.load(open(en_path, encoding='utf8')) if os.path.exists(en_path) else {}
merged = collections.OrderedDict()
errors, warnings = [], []

for path in sorted(glob.glob(os.path.join(here, 'translations', '*.json'))):
    data = json.load(open(path, encoding='utf8'), object_pairs_hook=collections.OrderedDict)
    for table, entries in data.items():
        dst = merged.setdefault(table, collections.OrderedDict())
        for key, text in entries.items():
            if key in dst:
                errors.append(f'{os.path.basename(path)}: {table}.{key} ซ้ำ')
            dst[key] = text

for table, entries in merged.items():
    if table == 'StaticTexts':  # English UI label -> Thai, not a game table
        continue
    en_table = en.get(table, {})
    for key, text in entries.items():
        if key not in en_table:
            warnings.append(f'{table}.{key}: ไม่มีคีย์นี้ในเกม')
            continue
        src = en_table[key]
        if sorted(PLACEHOLDER.findall(src)) != sorted(PLACEHOLDER.findall(text)):
            errors.append(f'{table}.{key}: ตัวแปรไม่ตรง EN={PLACEHOLDER.findall(src)} TH={PLACEHOLDER.findall(text)}')
        if sorted(TAG.findall(src)) != sorted(TAG.findall(text)):
            errors.append(f'{table}.{key}: แท็กไม่ตรง EN={TAG.findall(src)} TH={TAG.findall(text)}')

untranslated = 0
for table, en_table in en.items():
    th = merged.get(table, {})
    for key, src in en_table.items():
        if not src.strip() or key in th or any(a in th for a in alternates(key)):
            continue
        untranslated += 1
        warnings.append(f'{table}.{key}: ยังไม่ได้แปล ({src[:40]!r})')

for w in warnings:
    print('WARN ', w)
for e in errors:
    print('ERROR', e)

if errors:
    sys.exit(1)

doc = collections.OrderedDict()
doc['_info'] = ('คำแปลภาษาไทยสำหรับ Sons Of The Forest — แก้ข้อความด้านขวาได้เลย (ห้ามแก้ชื่อคีย์ด้านซ้าย '
                'และต้องคงตัวแปรอย่าง {0} {label} {BUTTON.Take} และแท็ก <color=...> ไว้เหมือนเดิม) '
                'บันทึกไฟล์เป็น UTF-8 แล้วเปิดเกมใหม่')
doc.update(merged)
with open(out_path, 'w', encoding='utf8', newline='\n') as f:
    json.dump(doc, f, ensure_ascii=False, indent=2)
    f.write('\n')

total = sum(len(v) for v in merged.values())
print(f'OK: {total} ข้อความ ใน {len(merged)} ตาราง, ยังไม่แปล {untranslated}, เตือน {len(warnings)}')
