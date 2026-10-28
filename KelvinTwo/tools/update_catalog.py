"""Register/refresh the locally built KelvinTwo package in ..\\catalog.json (used by ModPicker)."""
import json, sys, os, hashlib, zipfile, collections

zip_path, version = sys.argv[1], sys.argv[2]
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
catalog_path = os.path.join(root, 'catalog.json')

raw = open(catalog_path, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
catalog = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=collections.OrderedDict)

GROUP = 'เครื่องมือ (สร้างในเครื่องนี้)'
if GROUP not in catalog['groups']:
    after = 'อาวุธ (สร้างในเครื่องนี้)'
    idx = catalog['groups'].index(after) + 1 if after in catalog['groups'] else len(catalog['groups'])
    catalog['groups'].insert(idx, GROUP)

with zipfile.ZipFile(zip_path) as z:
    files = sorted(n for n in z.namelist() if not n.endswith('/'))

item = collections.OrderedDict([
    ('id', 'kelvintwo'),
    ('group', GROUP),
    ('default', False),
    ('mp', True),
    ('local', True),
    ('name', 'Kelvin ตัวที่ 2 (อมตะ ไว ไม่พัก ตีไม่งอน) + เก็บเครื่องร่อนอัตโนมัติ'),
    ('mod', f'KelvinTwo {version} (สร้างในเครื่องนี้ ไม่ต้องดาวน์โหลด)'),
    ('page', 'KelvinTwo'),
    ('desc', 'เรียกเคลวินตัวที่ 2 (สีฟ้า มีชื่อบนหัว) สั่งงานได้เหมือนเคลวิน แต่อมตะ ไม่เหนื่อย/ไม่หิว ไม่กลัว '
             'ตีแล้วไม่งอน เดินและทำงานไวขึ้น มองเห็นไกลขึ้น  และร่อนลงพื้นแล้วถ้าในกระเป๋าไม่มีเครื่องร่อน '
             'จะเก็บอันที่ตกให้เอง  ตั้งค่าที่ Mods\\KelvinTwo\\config.json'),
    ('warn', 'co-op: โฮสต์ต้องลง (เพื่อนลงด้วยเพื่อเห็นสี/ชื่อ และใช้เก็บเครื่องร่อน)  ยังไม่ได้ทดสอบในเกมจริง'),
    ('file', os.path.basename(zip_path)),
    ('sha256', hashlib.sha256(open(zip_path, 'rb').read()).hexdigest()),
    ('size', os.path.getsize(zip_path)),
    ('files', files),
    ('roots', ['Mods/']),
])

items = catalog['items']
for i, it in enumerate(items):
    if it.get('id') == 'kelvintwo':
        items[i] = item
        break
else:
    pos = next((i + 1 for i, it in enumerate(items) if it.get('id') == 'companion'), len(items))
    items.insert(pos, item)

text = json.dumps(catalog, ensure_ascii=False, indent=2) + '\n'
with open(catalog_path, 'wb') as f:
    f.write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
print(f'catalog.json: kelvintwo -> {item["file"]} {item["sha256"][:12]} ({item["size"]} bytes, {len(files)} files)')
