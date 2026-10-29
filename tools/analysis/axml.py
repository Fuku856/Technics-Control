"""Dump an Android binary XML from an APK, resolving @string refs (ja preferred) and @id/@type names via resources.arsc."""
import struct, sys, zipfile, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arsc import read_pool

def load_arsc(d):
    _, hsz, total, npk = struct.unpack_from('<HHII', d, 0)
    off = hsz
    gstr, off = read_pool(d, off)
    names = {}; strings = {}
    while off < len(d):
        typ, hsz2, size = struct.unpack_from('<HHI', d, off)
        if typ != 0x200: off += size; continue
        pkg_id = struct.unpack_from('<I', d, off + 8)[0]
        pkg_end = off + size
        typestr_off, _, keystr_off = struct.unpack_from('<III', d, off + 8 + 4 + 256)
        types, _ = read_pool(d, off + typestr_off)
        keys, _ = read_pool(d, off + keystr_off)
        p = off + hsz2
        while p < pkg_end:
            t, h, s = struct.unpack_from('<HHI', d, p)
            if t == 0x201:
                tid = d[p + 8]; flags = d[p + 9]; cnt = struct.unpack_from('<I', d, p + 12)[0]; est = struct.unpack_from('<I', d, p + 16)[0]
                cfg_size = struct.unpack_from('<I', d, p + 20)[0]; cfg = d[p + 20: p + 20 + cfg_size]
                lang = cfg[8:10].decode('ascii', 'ignore').strip('\0') if cfg_size >= 12 else ''
                country = cfg[10:12].decode('ascii', 'ignore').strip('\0') if cfg_size >= 12 else ''
                tname = types[tid - 1]
                for i in range(cnt):
                    if flags & 1:
                        idx, eo = struct.unpack_from('<HH', d, p + h + 4 * i); eo *= 4
                    else:
                        eo = struct.unpack_from('<I', d, p + h + 4 * i)[0]; idx = i
                        if eo == 0xFFFFFFFF: continue
                    e = p + est + eo
                    esz, eflags, kidx = struct.unpack_from('<HHI', d, e)
                    rid = (pkg_id << 24) | (tid << 16) | idx
                    names[rid] = '%s/%s' % (tname, keys[kidx])
                    if tname == 'string' and not (eflags & 1):
                        vsz, _, vtype, vdata = struct.unpack_from('<HBBI', d, e + esz)
                        if vtype == 3:
                            strings.setdefault(rid, {})[lang + ('-' + country if country else '')] = gstr[vdata]
                        elif vtype == 1:
                            strings.setdefault(rid, {})[lang + ('-' + country if country else '')] = ('@ref', vdata)
            p += s
        off = pkg_end
    return names, strings

def pick(strs, rid, depth=0):
    m = strs.get(rid, {})
    v = None
    for k in ('ja', 'ja-JP', ''):
        if k in m: v = m[k]; break
    if v is None: v = next(iter(m.values()), None)
    if isinstance(v, tuple) and depth < 5: return pick(strs, v[1], depth + 1)
    return v

def dump_axml(x, names, strs):
    off = 8; pool = []; resmap = []; depth = 0
    while off < len(x):
        t, h, s = struct.unpack_from('<HHI', x, off)
        if t == 0x0001: pool, _ = read_pool(x, off)
        elif t == 0x0180: resmap = list(struct.unpack_from('<%dI' % ((s - h) // 4), x, off + h))
        elif t == 0x0102:
            ns, name, ast, asz, acnt = struct.unpack_from('<IIHHH', x, off + 16)
            attrs = []
            for i in range(acnt):
                a = off + 16 + ast + i * asz
                ans, aname, raw, vsz, _, dtype, data = struct.unpack_from('<IIIHBBI', x, a)
                an = pool[aname] if aname < len(pool) and pool[aname] else (names.get(resmap[aname], hex(resmap[aname])) if aname < len(resmap) else '?')
                if dtype == 1:
                    v = names.get(data, hex(data)); sv = pick(strs, data)
                    if sv: v = '%s "%s"' % (v, sv.replace('\n', ' '))
                elif dtype == 3: v = '"%s"' % pool[raw] if raw != 0xFFFFFFFF else '?'
                elif dtype == 0x12: v = 'true' if data else 'false'
                else: v = hex(data)
                if an in ('title', 'key', 'summary', 'fragment', 'id', 'layout', 'text', 'src', 'icon', 'visibility', 'menu', 'label', 'name') or 'title' in an or 'key' in an:
                    attrs.append('%s=%s' % (an, v))
            print('  ' * depth + '<%s %s>' % (pool[name].split('.')[-1], ' '.join(attrs)))
            depth += 1
        elif t == 0x0103: depth -= 1
        off += s

if __name__ == '__main__':
    apk, arsc_path, entry = sys.argv[1], sys.argv[2], sys.argv[3]
    names, strs = load_arsc(open(arsc_path, 'rb').read())
    with zipfile.ZipFile(apk) as z:
        dump_axml(z.read(entry), names, strs)