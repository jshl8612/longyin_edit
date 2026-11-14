# 由 cave.asm 生成 LibKungfuCave.cs：nasm -f bin -o cave.bin cave.asm && python gen.py <输出 .cs>
import capstone,sys
b=open('cave.bin','rb').read()
ext={0x10000000:'Yes1',0x11000000:'No1',0x12000000:'Yes2',0x13000000:'No2',0x14000000:'World',0x15000000:'Player',0x16000000:'GetForce',0x17000000:'Find'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
fix=[(i.address+i.size-4,ext[int(i.op_str,16)]) for i in md.disasm(b,0) if i.mnemonic in('call','jmp') and i.op_str.startswith('0x') and int(i.op_str,16) in ext]
assert set(n for _,n in fix)==set(ext.values()), fix
bb=bytearray(b)
for o,_ in fix: bb[o:o+4]=b'\0\0\0\0'
hexs=', '.join(f'0x{x:02X}' for x in bb)
fx=', '.join(f'new object[] {{ {o}, "{n}" }}' for o,n in fix)
open(sys.argv[1],'w',encoding='utf-8').write(f'''// 由 tools/libkf/cave.asm 生成（nasm -f bin，再运行 gen.py），不要手改
namespace LongYinMod
{{
    static class LibKungfuCave
    {{
        public static readonly byte[] Code = {{ {hexs} }};
        // (rel32 位移字段在 Code 里的偏移, 目标名)；入口：+0 伤害计算，+5 武学战力
        public static readonly object[][] Fixups = {{ {fx} }};
    }}
}}
''')
print(len(bb), fix)
