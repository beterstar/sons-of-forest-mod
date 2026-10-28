"""Register/refresh the locally built PickaxeStone package in ..\\catalog.json (used by ModPicker)."""
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
    ('id', 'pickaxestone'),
    ('group', GROUP),
    ('default', False),
    ('mp', True),
    ('local', True),
    ('name', 'ตีหินด้วยอีเต้อ ได้หินก้อนใหญ่'),
    ('mod', f'PickaxeStone {version} (สร้างในเครื่องนี้ ไม่ต้องดาวน์โหลด)'),
    ('page', 'PickaxeStone'),
    ('desc', 'ถืออีเต้อตีหินธรรมชาติในแมพ (ก้อนหินใหญ่ หน้าผา ผนังถ้ำ) จะมีหินก้อนใหญ่หล่นออกมาให้เก็บ  '
             'ไม่นับพื้นดินและสิ่งก่อสร้างของผู้เล่น  ตั้งค่าที่ Mods\\PickaxeStone\\config.json'),
    ('warn', 'co-op: คนที่อยากได้ต้องลงเอง (นับเฉพาะการตีของตัวเอง)  ต้องมีอีเต้อ  ยังไม่ได้ทดสอบในเกมจริง'),
    ('file', os.path.basename(zip_path)),
    ('sha256', hashlib.sha256(open(zip_path, 'rb').read()).hexdigest()),
    ('size', os.path.getsize(zip_path)),
    ('files', files),
    ('roots', ['Mods/']),
])

items = catalog['items']
for i, it in enumerate(items):
    if it.get('id') == 'pickaxestone':
        items[i] = item
        break
else:
    pos = next((i + 1 for i, it in enumerate(items) if it.get('id') == 'kelvintwo'), len(items))
    items.insert(pos, item)

text = json.dumps(catalog, ensure_ascii=False, indent=2) + '\n'
with open(catalog_path, 'wb') as f:
    f.write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
print(f'catalog.json: pickaxestone -> {item["file"]} {item["sha256"][:12]} ({item["size"]} bytes, {len(files)} files)')
