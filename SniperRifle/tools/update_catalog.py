"""Register/refresh the locally built sniper package in ..\\catalog.json (used by ModPicker)."""
import json, sys, os, hashlib, zipfile, collections

zip_path, version = sys.argv[1], sys.argv[2]
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
catalog_path = os.path.join(root, 'catalog.json')

raw = open(catalog_path, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
catalog = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=collections.OrderedDict)

GROUP = 'อาวุธ (สร้างในเครื่องนี้)'
if GROUP not in catalog['groups']:
    # right after the language group if present
    idx = 1 if catalog['groups'] and catalog['groups'][0] == 'ภาษา' else 0
    catalog['groups'].insert(idx, GROUP)

with zipfile.ZipFile(zip_path) as z:
    files = sorted(n for n in z.namelist() if not n.endswith('/'))

item = collections.OrderedDict([
    ('id', 'sniper'),
    ('group', GROUP),
    ('default', False),
    ('mp', False),
    ('local', True),
    ('name', 'ปืนสไนเปอร์ไม้ (คราฟจากกิ่งไม้ 10 + หินเล็ก 10)'),
    ('mod', f'SniperRifle {version} (สร้างในเครื่องนี้ ไม่ต้องดาวน์โหลด)'),
    ('page', 'SniperRifle'),
    ('desc', 'ปืนสไนเปอร์คราฟเอง คลิกขวาส่องกล้องซูม ~6 เท่า ใช้กระสุน 9 มม. ดาเมจเท่าปืนพก แม็กละ 5 นัด '
             '+ คราฟกระสุน 9 มม. เต็มกระเป๋า (สูงสุด 1000 นัด) จากก้อนหิน 1 ก้อน หรือใบไม้ 3 ใบ  กด F8 ถ้าตัวละครค้าง  '
             'ตั้งค่าได้ที่ Mods\\SniperRifle\\config.json'),
    ('warn', 'เล่นออนไลน์ทุกคนต้องติดตั้งด้วย / ถ้าถอน mod ปืนที่อยู่ในเซฟจะหายไป'),
    ('file', os.path.basename(zip_path)),
    ('sha256', hashlib.sha256(open(zip_path, 'rb').read()).hexdigest()),
    ('size', os.path.getsize(zip_path)),
    ('files', files),
    ('roots', ['Mods/']),
])

items = catalog['items']
for i, it in enumerate(items):
    if it.get('id') == 'sniper':
        items[i] = item
        break
else:
    pos = next((i + 1 for i, it in enumerate(items) if it.get('id') == 'thai'), 0)
    items.insert(pos, item)

text = json.dumps(catalog, ensure_ascii=False, indent=2) + '\n'
with open(catalog_path, 'wb') as f:
    f.write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
print(f'catalog.json: sniper -> {item["file"]} {item["sha256"][:12]} ({item["size"]} bytes, {len(files)} files)')
