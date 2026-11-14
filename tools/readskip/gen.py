# 由 cave.asm 生成 ReadSkipCave.cs：nasm -f bin -o cave.bin cave.asm && python gen.py <输出 .cs>
import capstone,sys
b=open('cave.bin','rb').read()
ext={0x10000000:'ObjNew',0x11000000:'Wb',0x12000000:'Ctor',0x13000000:'AddRec',0x14000000:'Menu',0x15000000:'Panel'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
code_end=len(b)-4   # 末尾 4 字节是常数 k_rate
fix=[]
for i in md.disasm(b[:code_end],0):
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x') and int(i.op_str,16) in ext:
        fix.append((i.address+i.size-4, ext[int(i.op_str,16)]))
assert len(fix)==6 and len(set(n for _,n in fix))==6, fix
bb=bytearray(b)
for o,_ in fix: bb[o:o+4]=b'\0\0\0\0'
hexs=', '.join(f'0x{x:02X}' for x in bb)
fx=', '.join(f'new object[] {{ {o}, "{n}" }}' for o,n in fix)
open(sys.argv[1],'w',encoding='utf-8').write(f'''// 由 tools/readskip/cave.asm 生成（nasm -f bin，再运行 gen.py），不要手改
namespace LongYinMod
{{
    static class ReadSkipCave
    {{
        public static readonly byte[] Code = {{ {hexs} }};
        // (rel32 位移字段在 Code 里的偏移, 目标名)
        public static readonly object[][] Fixups = {{ {fx} }};
    }}
}}
''')
print(len(bb), fix)
