"""Minimal dex reader: dump static final field values and method bytecode for classes matching a prefix."""
import struct, sys, re

def uleb(d, o):
    r = s = 0
    while True:
        b = d[o]; o += 1
        r |= (b & 0x7f) << s; s += 7
        if b < 0x80: return r, o

def sleb(d, o):
    r = s = 0
    while True:
        b = d[o]; o += 1
        r |= (b & 0x7f) << s; s += 7
        if b < 0x80:
            if b & 0x40: r -= 1 << s
            return r, o

class Dex:
    def __init__(self, path):
        d = self.d = open(path, 'rb').read()
        (self.str_n, self.str_o, self.type_n, self.type_o, self.proto_n, self.proto_o,
         self.field_n, self.field_o, self.meth_n, self.meth_o, self.cls_n, self.cls_o) = struct.unpack_from('<12I', d, 0x38)
        self._s = {}
    def s(self, i):
        if i in self._s: return self._s[i]
        o = struct.unpack_from('<I', self.d, self.str_o + 4 * i)[0]
        _, o = uleb(self.d, o)
        e = self.d.index(b'\0', o)
        v = self.d[o:e].decode('utf-8', 'replace'); self._s[i] = v; return v
    def t(self, i): return self.s(struct.unpack_from('<I', self.d, self.type_o + 4 * i)[0])
    def field(self, i):
        c, ty, n = struct.unpack_from('<HHI', self.d, self.field_o + 8 * i)
        return self.t(c), self.t(ty), self.s(n)
    def meth(self, i):
        c, p, n = struct.unpack_from('<HHI', self.d, self.meth_o + 8 * i)
        return self.t(c), self.s(n)
    def enc_value(self, o):
        b = self.d[o]; o += 1
        vt, va = b & 0x1f, b >> 5
        if vt in (0x00, 0x02, 0x03, 0x04, 0x06):
            n = va + 1; raw = self.d[o:o + n]; o += n
            v = int.from_bytes(raw, 'little', signed=(vt != 0x03))
            return v, o
        if vt == 0x17:
            n = va + 1; v = int.from_bytes(self.d[o:o + n], 'little'); o += n
            return repr(self.s(v)), o
        if vt == 0x1f: return bool(va), o
        if vt == 0x1e: return None, o
        if vt == 0x1c:
            sz, o = uleb(self.d, o); arr = []
            for _ in range(sz):
                v, o = self.enc_value(o); arr.append(v)
            return arr, o
        n = va + 1; return f'<vt{vt:#x}>', o + n
    def classes(self):
        for i in range(self.cls_n):
            yield struct.unpack_from('<8I', self.d, self.cls_o + 32 * i)

def dump(path, pattern, show_code):
    dx = Dex(path); d = dx.d; rx = re.compile(pattern)
    for (cls, acc, sup, ifs, src, ann, cdata, sval) in dx.classes():
        name = dx.t(cls)
        if not rx.search(name): continue
        print(f'### {name}')
        if not cdata: continue
        o = cdata
        sf, o = uleb(d, o); inf, o = uleb(d, o); dm, o = uleb(d, o); vm, o = uleb(d, o)
        sfields = []; fi = 0
        for _ in range(sf):
            diff, o = uleb(d, o); _, o = uleb(d, o); fi += diff; sfields.append(fi)
        for _ in range(inf):
            _, o = uleb(d, o); _, o = uleb(d, o)
        vals = []
        if sval:
            sz, p = uleb(d, sval)
            for _ in range(sz):
                v, p = dx.enc_value(p); vals.append(v)
        for k, f in enumerate(sfields):
            c, ty, n = dx.field(f)
            if k < len(vals):
                v = vals[k]
                print(f'  static {ty} {n} = {hex(v) if isinstance(v, int) and not isinstance(v, bool) else v}')
        if not show_code: continue
        mi = 0
        for cnt in (dm, vm):
            mi = 0
            for _ in range(cnt):
                diff, o = uleb(d, o); _, o = uleb(d, o); code, o = uleb(d, o); mi += diff
                if not code: continue
                c, mn = dx.meth(mi)
                regs, ins, outs, tries, dbg, n = struct.unpack_from('<HHHHII', d, code)
                insns = d[code + 16: code + 16 + 2 * n]
                print(f'  -- method {mn} ({n} units)')
                print('     ' + insns.hex())

if __name__ == '__main__':
    dump(sys.argv[1], sys.argv[2], len(sys.argv) > 3)
