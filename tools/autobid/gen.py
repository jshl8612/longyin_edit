# 由 cave.asm 生成 AutoBidCave.cs：nasm -f bin -o cave.bin cave.asm && python gen.py <输出 .cs>
import capstone,sys,re
b=open('cave.bin','rb').read()
names=[l.split()[0] for l in re.findall(r'^EXT (\w+), *\d+',open('cave.asm',encoding='utf-8').read(),re.M)]
idx={0x20000000+i*0x100000:n[2:].title().replace('_','') for i,n in enumerate(re.findall(r'^EXT (\w+),',open('cave.asm',encoding='utf-8').read(),re.M))}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
code_end=b.find(b'\xcc'*8)   # 代码结束标记
assert code_end>0
fix=[]
for i in md.disasm(b[:code_end],0):
    tgt=None
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x'): tgt=int(i.op_str,16)
    m=re.search(r'\[rip \+ (0x[0-9a-f]+)\]',i.op_str)
    if m: tgt=i.address+i.size+int(m.group(1),16)
    if tgt in idx: fix.append((i.address+i.size-4, idx[tgt]))
print(len(b), code_end, fix)
assert set(n for _,n in fix)==set(idx.values()), set(idx.values())-set(n for _,n in fix)
bb=bytearray(b)
for o,_ in fix: bb[o:o+4]=b'\0\0\0\0'
hexs=', '.join(f'0x{x:02X}' for x in bb)
fx=', '.join(f'new object[] {{ {o}, "{n}" }}' for o,n in fix)
open(sys.argv[1],'w',encoding='utf-8').write(f'''// 由 tools/autobid/cave.asm 生成（nasm -f bin，再运行 gen.py），不要手改
namespace LongYinMod
{{
    static class AutoBidCave
    {{
        public static readonly byte[] Code = {{ {hexs} }};
        // (rel32 位移字段在 Code 里的偏移, 目标名)；入口：+0 Button.Press，+5 开拍，+10 Update
        public static readonly object[][] Fixups = {{ {fx} }};
    }}
}}
''')
