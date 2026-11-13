# 反汇编辅助：python -i ndis.py GameAssembly.dll methods.tsv
#   dis(va)            反汇编一个函数（call 会标注方法名）
#   va_of('类.方法')   按名字找地址
# 依赖：pip install pefile capstone
import capstone, pefile, bisect, sys
P = sys.argv[1] if len(sys.argv) > 1 else 'GameAssembly.dll'
TSV = sys.argv[2] if len(sys.argv) > 2 else 'methods.tsv'
pe = pefile.PE(P, fast_load=True); data = open(P, 'rb').read(); base = pe.OPTIONAL_HEADER.ImageBase
names = {}
for l in open(TSV, encoding='utf-8'):
    va, n = l.rstrip('\n').split('\t', 1); va = int(va, 16)
    if va: names.setdefault(va, []).append(n)
starts = sorted(names)
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
def off(va): return pe.get_offset_from_rva(va - base)
def dis(va, maxlen=0x3000, show=True):
    i = bisect.bisect_right(starts, va); e = min(starts[i] if i < len(starts) else va + maxlen, va + maxlen)
    out = []
    for ins in md.disasm(data[off(va):off(va) + (e - va)], va):
        s = f'{ins.address:x} {ins.mnemonic} {ins.op_str}'
        if ins.mnemonic in ('call', 'jmp'):
            try:
                t = int(ins.op_str, 16)
                if t in names: s += '   ; ' + ' | '.join(names[t][:3])
            except ValueError: pass
        out.append(s)
    if show: print('\n'.join(x for x in out if 'int3' not in x))
    return out
def va_of(n): return [hex(k) for k, v in names.items() if any(x.startswith(n) for x in v)]
