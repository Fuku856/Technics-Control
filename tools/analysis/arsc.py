"""Minimal resources.arsc reader: map drawable/mipmap entry names to file paths."""
import struct, sys, re

def read_pool(d, off):
    typ, hsz, size, cnt, scnt, flags, sstart, stystart = struct.unpack_from('<HHIIIIII', d, off)
    utf8 = flags & 0x100
    offs = struct.unpack_from('<%dI' % cnt, d, off + hsz)
    base = off + sstart; out = []
    for o in offs:
        p = base + o
        if utf8:
            n = d[p]; p += 2 if n & 0x80 else 1
            n = d[p]; 
            if n & 0x80: n = ((n & 0x7f) << 8) | d[p + 1]; p += 2
            else: p += 1
            out.append(d[p:p + n].decode('utf-8', 'replace'))
        else:
            n = struct.unpack_from('<H', d, p)[0]; p += 2
            if n & 0x8000: n = ((n & 0x7fff) << 16) | struct.unpack_from('<H', d, p)[0]; p += 2
            out.append(d[p:p + 2 * n].decode('utf-16-le', 'replace'))
    return out, off + size

TYPES = ('drawable', 'mipmap')
def main(path, pattern):
    d = open(path, 'rb').read(); rx = re.compile(pattern)
    _, hsz, total, npk = struct.unpack_from('<HHII', d, 0)
    off = hsz
    gstr, off = read_pool(d, off)
    while off < len(d):
        typ, hsz, size = struct.unpack_from('<HHI', d, off)
        if typ != 0x200: off += size; continue
        pkg_end = off + size
        typestr_off, _, keystr_off = struct.unpack_from('<III', d, off + 8 + 4 + 256)
        types, _ = read_pool(d, off + typestr_off)
        keys, _ = read_pool(d, off + keystr_off)
        p = off + hsz
        while p < pkg_end:
            t, h, s = struct.unpack_from('<HHI', d, p)
            if t == 0x201:
                tid = d[p + 8]; cnt = struct.unpack_from('<I', d, p + 12)[0]; est = struct.unpack_from('<I', d, p + 16)[0]
                flags = d[p + 9]
                tname = types[tid - 1]
                cfg_size = struct.unpack_from('<I', d, p + 20)[0]
                cfg = d[p + 20: p + 20 + cfg_size]
                density = struct.unpack_from('<H', cfg, 14)[0] if cfg_size >= 16 else 0
                night = (cfg[0x17] & 0x30) if cfg_size > 0x17 else 0
                if tname in TYPES:
                    sparse = flags & 0x01
                    for i in range(cnt):
                        if sparse:
                            idx, eo = struct.unpack_from('<HH', d, p + h + 4 * i); eo *= 4
                        else:
                            eo = struct.unpack_from('<I', d, p + h + 4 * i)[0]; idx = i
                            if eo == 0xFFFFFFFF: continue
                        e = p + est + eo
                        esz, eflags, kidx = struct.unpack_from('<HHI', d, e)
                        name = keys[kidx]
                        if not rx.search(name) or eflags & 1: continue
                        vsz, _, vtype, vdata = struct.unpack_from('<HBBI', d, e + esz)
                        if vtype == 3:
                            print('%s\t%s\tdensity=%d night=%d' % (name, gstr[vdata], density, night))
            p += s
        off = pkg_end

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
def main_types(path, pattern, types):
    global TYPES
    TYPES = tuple(types.split(','))
    main(path, pattern)
