"""Minimal UnityFS bundle + SerializedFile (typetree) reader, enough to dump
Unity Localization StringTable / SharedTableData objects. Pure Python, no deps."""
import struct, io, sys, json


def lz4_block_decompress(src, usize):
    dst = bytearray()
    i, n = 0, len(src)
    while i < n:
        tok = src[i]; i += 1
        lit = tok >> 4
        if lit == 15:
            while True:
                b = src[i]; i += 1; lit += b
                if b != 255: break
        dst += src[i:i + lit]; i += lit
        if i >= n: break
        off = src[i] | (src[i + 1] << 8); i += 2
        ml = tok & 15
        if ml == 15:
            while True:
                b = src[i]; i += 1; ml += b
                if b != 255: break
        ml += 4
        start = len(dst) - off
        for k in range(ml):
            dst.append(dst[start + k])
    return bytes(dst)


class R:
    def __init__(self, data, le=False):
        self.d = data; self.p = 0; self.le = le
    def _f(self, fmt, size):
        v = struct.unpack_from(('<' if self.le else '>') + fmt, self.d, self.p)[0]; self.p += size; return v
    def u8(self): return self._f('B', 1)
    def b(self): return self._f('?', 1)
    def i16(self): return self._f('h', 2)
    def u16(self): return self._f('H', 2)
    def i32(self): return self._f('i', 4)
    def u32(self): return self._f('I', 4)
    def i64(self): return self._f('q', 8)
    def u64(self): return self._f('Q', 8)
    def f32(self): return self._f('f', 4)
    def f64(self): return self._f('d', 8)
    def raw(self, n): v = self.d[self.p:self.p + n]; self.p += n; return v
    def cstr(self):
        e = self.d.index(b'\0', self.p); v = self.d[self.p:e].decode('utf8', 'replace'); self.p = e + 1; return v
    def align(self, a=4): self.p = (self.p + a - 1) // a * a


def read_bundle(path):
    with open(path, 'rb') as f: data = f.read()
    r = R(data)
    sig = r.cstr(); assert sig == 'UnityFS', sig
    ver = r.u32(); r.cstr(); unity_rev = r.cstr()
    size = r.i64(); csz = r.u32(); usz = r.u32(); flags = r.u32()
    if ver >= 7: r.align(16)
    if flags & 0x80:
        bi = data[len(data) - csz:]
    else:
        bi = r.raw(csz)
    comp = flags & 0x3F
    if comp in (2, 3): bi = lz4_block_decompress(bi, usz)
    elif comp != 0: raise ValueError('blocksinfo compression %d' % comp)
    br = R(bi); br.raw(16)
    blocks = [(br.u32(), br.u32(), br.u16()) for _ in range(br.i32())]
    nodes = []
    for _ in range(br.i32()):
        nodes.append((br.i64(), br.i64(), br.u32(), br.cstr()))
    if flags & 0x200: r.align(16)
    out = bytearray()
    for us, cs, fl in blocks:
        chunk = r.raw(cs)
        c = fl & 0x3F
        if c == 0: out += chunk
        elif c in (2, 3): out += lz4_block_decompress(chunk, us)
        else: raise ValueError('block compression %d' % c)
    return unity_rev, {name: bytes(out[off:off + sz]) for off, sz, fl, name in nodes}


class TypeTree:
    def __init__(self, nodes): self.nodes = nodes  # list of dicts (level,type,name,size,meta)


def read_serialized(data):
    r = R(data)
    msize = r.u32(); fsize = r.u32(); ver = r.u32(); doff = r.u32()
    le = True
    if ver >= 9:
        le = r.u8() == 0; r.raw(3)
    if ver >= 22:
        msize = r.u32(); fsize = r.i64(); doff = r.i64(); r.i64()
    r.le = le
    unity = r.cstr(); plat = r.i32(); has_tt = r.b()
    types = []
    for _ in range(r.i32()):
        cid = r.i32(); r.b(); sidx = r.i16()
        if (ver >= 16 and cid == 114) or (ver < 16 and cid < 0): r.raw(16)
        r.raw(16)
        tt = None
        if has_tt:
            nn = r.i32(); sb = r.i32()
            raw = [(r.u16(), r.u8(), r.u8(), r.u32(), r.u32(), r.i32(), r.i32(), r.i32(), r.u64() if ver >= 19 else 0) for _ in range(nn)]
            strbuf = r.raw(sb)
            def s(off):
                if off & 0x80000000: return COMMON.get(off & 0x7FFFFFFF, '?%d' % (off & 0x7FFFFFFF))
                e = strbuf.index(b'\0', off); return strbuf[off:e].decode()
            tt = [dict(level=lv, type=s(to), name=s(no), size=bs, meta=mf) for (_, lv, tf, to, no, bs, idx, mf, _) in raw]
            if ver >= 21:
                for _ in range(r.i32()): r.i32()
        types.append(dict(cid=cid, tt=tt))
    objs = []
    for _ in range(r.i32()):
        r.align(4)
        pid = r.i64()
        start = r.i64() if ver >= 22 else r.u32()
        bsz = r.u32(); tid = r.i32()
        objs.append(dict(pid=pid, start=doff + start, size=bsz, type=types[tid]))
    return unity, objs


COMMON_STR = b"AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0m_ScriptingClassIdentifier\0Gradient\0Type*\0int2_storage\0int3_storage\0BoundsInt\0m_CorrespondingSourceObject\0m_PrefabInstance\0m_PrefabAsset\0FileSize\0Hash128\0RenderingLayerMask\0"
COMMON = {}
_o = 0
for _s in COMMON_STR.split(b'\0'):
    COMMON[_o] = _s.decode(); _o += len(_s) + 1


class StopParse(Exception):
    pass


def parse_obj(data, obj, stop_types=('ManagedReferencesRegistry',)):
    tt = obj['type']['tt']
    r = R(data, le=True); r.p = obj['start']
    PRIM = {'SInt8': 'b', 'UInt8': 'B', 'char': 'B', 'bool': '?', 'SInt16': 'h', 'short': 'h', 'UInt16': 'H', 'unsigned short': 'H',
            'SInt32': 'i', 'int': 'i', 'UInt32': 'I', 'unsigned int': 'I', 'Type*': 'I', 'SInt64': 'q', 'long long': 'q',
            'UInt64': 'Q', 'unsigned long long': 'Q', 'FileSize': 'Q', 'float': 'f', 'double': 'd'}

    def children(i):
        lv = tt[i]['level']; out = []; j = i + 1
        while j < len(tt) and tt[j]['level'] > lv:
            if tt[j]['level'] == lv + 1: out.append(j)
            j += 1
        return out, j

    def read(i):
        n = tt[i]
        if n['type'] in stop_types: raise StopParse()
        ch, end = children(i)
        t = n['type']
        if t == 'string':
            ln = r.i32(); v = r.raw(ln).decode('utf8', 'replace'); r.align(4)
        elif t in PRIM and not ch:
            v = struct.unpack_from('<' + PRIM[t], r.d, r.p)[0]; r.p += struct.calcsize(PRIM[t])
        elif ch and tt[ch[0]]['type'] == 'Array':
            arr = ch[0]; ach, _ = children(arr)
            cnt = r.i32(); elem = ach[1]
            if tt[elem]['type'] in ('UInt8', 'char', 'SInt8') and len(children(elem)[0]) == 0:
                v = r.raw(cnt)
            else:
                v = [read(elem) for _ in range(cnt)]
            if tt[arr]['meta'] & 0x4000: r.align(4)
        elif t == 'TypelessData':
            cnt = r.i32(); v = r.raw(cnt)
        else:
            v = {}
            for c in ch:
                v[tt[c]['name']] = read(c)
        if n['meta'] & 0x4000: r.align(4)
        return v

    result = {}
    try:
        for c in children(0)[0]:
            result[tt[c]['name']] = read(c)
    except StopParse:
        pass
    return result
