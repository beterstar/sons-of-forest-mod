import sys, os, json, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unitybundle import read_bundle, read_serialized, parse_obj

AA = sys.argv[1]
OUT = sys.argv[2]


def objects(path):
    rev, files = read_bundle(path)
    for name, data in files.items():
        if name.endswith('.resS') or name.endswith('.resource'):
            continue
        unity, objs = read_serialized(data)
        for o in objs:
            if o['type']['cid'] != 114 or not o['type']['tt']:
                continue
            yield unity, o, parse_obj(data, o)


shared = {}
for unity, o, v in objects(os.path.join(AA, 'localization-assets-shared_assets_all.bundle')):
    if 'm_TableCollectionName' in v:
        shared[v['m_TableCollectionName']] = {e['m_Id']: e['m_Key'] for e in v['m_Entries']}
        print('shared', unity, v['m_TableCollectionName'], len(v['m_Entries']))

result = {}
lang = sys.argv[3] if len(sys.argv) > 3 else 'english(en)'
for unity, o, v in objects(os.path.join(AA, 'localization-string-tables-%s_assets_all.bundle' % lang)):
    if 'm_TableData' not in v:
        print('skip', v.get('m_Name'))
        continue
    name = v['m_Name']
    coll = name.rsplit('_', 1)[0]
    keys = shared.get(coll, {})
    tbl = {}
    for e in v['m_TableData']:
        tbl[keys.get(e['m_Id'], '#%d' % e['m_Id'])] = e['m_Localized']
    result[coll] = tbl
    print('table', name, len(tbl))

with open(OUT, 'w', encoding='utf8') as f:
    json.dump(result, f, ensure_ascii=False, indent=1)
print('total strings', sum(len(t) for t in result.values()))
