"""Tiny Dalvik disassembler (subset) built on dexdump.Dex."""
import struct, sys, re, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dexdump import Dex, uleb

FMT = {}
def setr(a, b, f):
    for i in range(a, b + 1): FMT[i] = f
for a, b, f in [(0x00,0x00,'10x'),(0x01,0x01,'12x'),(0x02,0x02,'22x'),(0x03,0x03,'32x'),(0x04,0x04,'12x'),(0x05,0x05,'22x'),(0x06,0x06,'32x'),
    (0x07,0x07,'12x'),(0x08,0x08,'22x'),(0x09,0x09,'32x'),(0x0a,0x0d,'11x'),(0x0e,0x0e,'10x'),(0x0f,0x11,'11x'),(0x12,0x12,'11n'),(0x13,0x13,'21s'),
    (0x14,0x14,'31i'),(0x15,0x15,'21h'),(0x16,0x16,'21s'),(0x17,0x17,'31i'),(0x18,0x18,'51l'),(0x19,0x19,'21h'),(0x1a,0x1a,'21c'),(0x1b,0x1b,'31c'),
    (0x1c,0x1c,'21c'),(0x1d,0x1e,'11x'),(0x1f,0x1f,'21c'),(0x20,0x20,'22c'),(0x21,0x21,'12x'),(0x22,0x22,'21c'),(0x23,0x23,'22c'),(0x24,0x24,'35c'),
    (0x25,0x25,'3rc'),(0x26,0x26,'31t'),(0x27,0x27,'11x'),(0x28,0x28,'10t'),(0x29,0x29,'20t'),(0x2a,0x2a,'30t'),(0x2b,0x2c,'31t'),(0x2d,0x31,'23x'),
    (0x32,0x37,'22t'),(0x38,0x3d,'21t'),(0x3e,0x43,'10x'),(0x44,0x51,'23x'),(0x52,0x5f,'22c'),(0x60,0x6d,'21c'),(0x6e,0x72,'35c'),(0x73,0x73,'10x'),
    (0x74,0x78,'3rc'),(0x79,0x7a,'10x'),(0x7b,0x8f,'12x'),(0x90,0xaf,'23x'),(0xb0,0xcf,'12x'),(0xd0,0xd7,'22s'),(0xd8,0xe2,'22b'),(0xe3,0xf9,'10x'),
    (0xfa,0xfa,'45cc'),(0xfb,0xfb,'4rcc'),(0xfc,0xfc,'35c'),(0xfd,0xfd,'3rc'),(0xfe,0xff,'21c')]:
    setr(a, b, f)
SIZE = {'10x':1,'12x':1,'11n':1,'11x':1,'10t':1,'20t':2,'22x':2,'21t':2,'21s':2,'21h':2,'21c':2,'23x':2,'22b':2,'22t':2,'22s':2,'22c':2,
        '30t':3,'32x':3,'31i':3,'31t':3,'31c':3,'35c':3,'3rc':3,'45cc':4,'4rcc':4,'51l':5}
NAMES = {0x12:'const/4',0x13:'const/16',0x14:'const',0x15:'const/high16',0x1a:'const-string',0x22:'new-instance',0x23:'new-array',
         0x26:'fill-array-data',0x4f:'aput-byte',0x4b:'aput',0x6e:'invoke-virtual',0x6f:'invoke-super',0x70:'invoke-direct',0x71:'invoke-static',
         0x72:'invoke-interface',0x74:'invoke-virtual/range',0x76:'invoke-direct/range',0x77:'invoke-static/range',0x60:'sget',0x62:'sget-object',
         0x63:'sget-boolean',0x64:'sget-byte',0x52:'iget',0x59:'iput',0x5b:'iput-object',0x54:'iget-object',0x5d:'iput-byte',0x56:'iget-byte',
         0x0c:'move-result-object',0x0a:'move-result',0x8d:'int-to-byte',0xdd:'and-int/lit8',0xe2:'ushr-int/lit8',0xe0:'shl-int/lit8',0xd5:'and-int/lit16',
         0x2b:'packed-switch',0x2c:'sparse-switch',0x38:'if-eqz',0x39:'if-nez',0x32:'if-eq',0x33:'if-ne',0x0e:'return-void',0x11:'return-object',0x0f:'return',
         0x28:'goto',0x01:'move',0x07:'move-object',0x21:'array-length',0x44:'aget',0x48:'aget-byte',0x5c:'iput-boolean',0x55:'iget-boolean'}

def s16(x): return x - 0x10000 if x & 0x8000 else x

def dis(dx, code_off):
    d = dx.d
    regs, ins, outs, tries, dbg, n = struct.unpack_from('<HHHHII', d, code_off)
    base = code_off + 16; pc = 0; out = []
    u = lambda i: struct.unpack_from('<H', d, base + 2 * i)[0]
    while pc < n:
        w = u(pc); op = w & 0xff
        if op == 0 and w in (0x100, 0x200, 0x300):
            if w == 0x100: pc += u(pc + 1) * 2 + 4
            elif w == 0x200: pc += u(pc + 1) * 4 + 2
            else:
                ew = u(pc + 1); cnt = struct.unpack_from('<I', d, base + 2 * (pc + 2))[0]
                pc += (ew * cnt + 1) // 2 + 4
            continue
        f = FMT[op]; sz = SIZE[f]; nm = NAMES.get(op, 'op%02x' % op); a = w >> 8; txt = nm
        if f == '11n':
            v = a >> 4; v = v - 16 if v > 7 else v; txt = '%s v%d, %#x' % (nm, a & 15, v)
        elif f == '21s': txt = '%s v%d, %#x' % (nm, a, s16(u(pc + 1)))
        elif f == '21h': txt = '%s v%d, %#x' % (nm, a, u(pc + 1) << 16)
        elif f == '31i': txt = '%s v%d, %#x' % (nm, a, struct.unpack_from('<i', d, base + 2 * (pc + 1))[0])
        elif f == '22b':
            lit = u(pc + 1) >> 8; lit = lit - 256 if lit > 127 else lit
            txt = '%s v%d, v%d, %#x' % (nm, a, u(pc + 1) & 0xff, lit)
        elif f == '22s': txt = '%s v%d, v%d, %#x' % (nm, a & 15, a >> 4, s16(u(pc + 1)))
        elif f in ('21c', '31c'):
            idx = u(pc + 1) if f == '21c' else struct.unpack_from('<I', d, base + 2 * (pc + 1))[0]
            if op in (0x1a, 0x1b): ref = repr(dx.s(idx))
            elif op in (0x1c, 0x1f, 0x22): ref = dx.t(idx)
            elif 0x60 <= op <= 0x6d:
                c, ty, nmf = dx.field(idx); ref = c.split('/')[-1] + '.' + nmf
            else: ref = str(idx)
            txt = '%s v%d, %s' % (nm, a, ref)
        elif f == '22c':
            idx = u(pc + 1)
            if op in (0x20, 0x23): ref = dx.t(idx)
            else:
                c, ty, nmf = dx.field(idx); ref = c.split('/')[-1] + '.' + nmf
            txt = '%s v%d, v%d, %s' % (nm, a & 15, a >> 4, ref)
        elif f in ('35c', '3rc'):
            idx = u(pc + 1)
            if op in (0x24, 0x25): ref = dx.t(idx)
            elif op in (0xfc, 0xfd): ref = 'callsite%d' % idx
            else:
                c, mn = dx.meth(idx); ref = c.split('/')[-1] + '->' + mn
            if f == '35c':
                g = u(pc + 2); cnt = a >> 4
                rl = [g & 15, (g >> 4) & 15, (g >> 8) & 15, (g >> 12) & 15, a & 15][:cnt]
                txt = '%s {%s}, %s' % (nm, ','.join('v%d' % r for r in rl), ref)
            else: txt = '%s {v%d..+%d}, %s' % (nm, u(pc + 2), a, ref)
        elif op == 0x26:
            off = struct.unpack_from('<i', d, base + 2 * (pc + 1))[0]; p = pc + off
            ew = u(p + 1); cnt = struct.unpack_from('<I', d, base + 2 * (p + 2))[0]
            data = d[base + 2 * (p + 4): base + 2 * (p + 4) + ew * cnt]
            txt = '%s v%d, [%s] (elem %d)' % (nm, a, data.hex(' '), ew)
        elif f == '23x': txt = '%s v%d, v%d, v%d' % (nm, a, u(pc + 1) & 0xff, u(pc + 1) >> 8)
        elif f == '12x': txt = '%s v%d, v%d' % (nm, a & 15, a >> 4)
        elif f == '11x': txt = '%s v%d' % (nm, a)
        elif f == '21t': txt = '%s v%d, ->%04x' % (nm, a, pc + s16(u(pc + 1)))
        elif f == '22t': txt = '%s v%d, v%d, ->%04x' % (nm, a & 15, a >> 4, pc + s16(u(pc + 1)))
        elif f == '10t': txt = '%s ->%04x' % (nm, pc + (a - 256 if a > 127 else a))
        out.append('    %04x: %s' % (pc, txt))
        pc += sz
    return out

def run(path, pattern, mpat='.'):
    dx = Dex(path); d = dx.d; rx = re.compile(pattern); mrx = re.compile(mpat)
    for (cls, acc, sup, ifs, src, ann, cdata, sval) in dx.classes():
        name = dx.t(cls)
        if not rx.search(name) or not cdata: continue
        print('### %s  extends %s' % (name, dx.t(sup)))
        o = cdata
        sf, o = uleb(d, o); inf, o = uleb(d, o); dm, o = uleb(d, o); vm, o = uleb(d, o)
        for _ in range(sf + inf):
            _, o = uleb(d, o); _, o = uleb(d, o)
        for cnt in (dm, vm):
            mi = 0
            for _ in range(cnt):
                diff, o = uleb(d, o); _, o = uleb(d, o); code, o = uleb(d, o); mi += diff
                if not code: continue
                c, mn = dx.meth(mi)
                if not mrx.search(mn): continue
                print('  == ' + mn)
                print('\n'.join(dis(dx, code)))

if __name__ == '__main__':
    run(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else '.')