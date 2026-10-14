"""Register/refresh the locally built Thai mod package in ..\\catalog.json (used by ModPicker)."""
import json, sys, os, hashlib, zipfile, collections

zip_path, version = sys.argv[1], sys.argv[2]
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
catalog_path = os.path.join(root, 'catalog.json')

raw = open(catalog_path, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
catalog = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=collections.OrderedDict)

GROUP = 'ภาษา'
if GROUP not in catalog['groups']:
    catalog['groups'].insert(0, GROUP)

with zipfile.ZipFile(zip_path) as z:
    files = sorted(n for n in z.namelist() if not n.endswith('/'))

item = collections.OrderedDict([
    ('id', 'thai'),
    ('group', GROUP),
    ('default', True),
    ('mp', True),
    ('local', True),
    ('name', 'แปลเกมเป็นภาษาไทย (เมนู ไอเทม คำแนะนำ คำบรรยาย)'),
    ('mod', f'ThaiLanguage {version} (สร้างในเครื่องนี้ ไม่ต้องดาวน์โหลด)'),
    ('page', 'ThaiLanguage'),
    ('desc', 'แปลข้อความที่เกมรองรับการแปลทั้งหมด ~1,300 ข้อความ + ใช้ฟอนต์ไทยของ Windows พร้อมจัดวรรณยุกต์และตัดคำ '
             'ในเกมให้ตั้งภาษาเป็น English (จะแสดงเป็น "ภาษาไทย")  แก้คำแปลเองได้ที่ Mods\\ThaiLanguage\\th.json'),
    ('warn', 'โน้ต/อีเมล/ป้ายในเกมเป็นรูปภาพ จึงยังเป็นภาษาอังกฤษ'),
    ('file', os.path.basename(zip_path)),
    ('sha256', hashlib.sha256(open(zip_path, 'rb').read()).hexdigest()),
    ('size', os.path.getsize(zip_path)),
    ('files', files),
    ('roots', ['Mods/']),
])

items = catalog['items']
for i, it in enumerate(items):
    if it.get('id') == 'thai':
        items[i] = item
        break
else:
    items.insert(0, item)

text = json.dumps(catalog, ensure_ascii=False, indent=2) + '\n'
with open(catalog_path, 'wb') as f:
    f.write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
print(f'catalog.json: thai -> {item["file"]} {item["sha256"][:12]}… ({item["size"]} bytes, {len(files)} files)')
