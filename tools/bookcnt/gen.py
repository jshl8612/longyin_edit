import capstone,sys
b=open('cave.bin','rb').read()
ext={0x10000000:'Player',0x11000000:'Concat',0x12000000:'StrNew',0x13000000:'Color',0x14000000:'AddTab',0x15000000:'World',0x16000000:'Back',0x17000000:'GetForce',0x18000000:'Back2'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=True
code_end=b.find(b'\n'+'背包'.encode())
fix=[]
for i in md.disasm(b[:code_end],0):
    tgt=None
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x'): tgt=int(i.op_str,16)
    elif 'rip' in i.op_str: tgt=i.address+i.size+i.disp
    if tgt in ext: fix.append((i.address+i.size-4, ext[tgt]))
assert len(fix)==13 and len(set(n for _,n in fix))==9, fix
bb=bytearray(b)
for o,_ in fix: bb[o:o+4]=b'\0\0\0\0'
hexs=', '.join(f'0x{x:02X}' for x in bb)
fx=', '.join(f'new object[] {{ {o}, "{n}" }}' for o,n in fix)
cs=f'''// 由 tools/bookcnt/cave.asm 生成（nasm -f bin，再运行 gen.py），不要手改
namespace LongYinMod
{{
    static class BookCountCave
    {{
        public static readonly byte[] Code = {{ {hexs} }};
        // (rel32 位移字段在 Code 里的偏移, 目标名)
        public static readonly object[][] Fixups = {{ {fx} }};
    }}
}}
'''
open(sys.argv[1],'w',encoding='utf-8').write(cs)
print(len(bb), fix)
