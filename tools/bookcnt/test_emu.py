# 用 unicorn 模拟执行 cave.bin，检查秘籍 / 武功两个入口在各种背包、仓库、门派组合下拼出的文字
# pip install unicorn capstone；python test_emu.py cave.bin
import struct,sys,capstone
from unicorn import *
from unicorn.x86_const import *
b=bytearray(open(sys.argv[1],'rb').read())
ext={0x10000000:'Player',0x11000000:'Concat',0x12000000:'StrNew',0x13000000:'Color',0x14000000:'AddTab',0x15000000:'World',0x16000000:'Back',0x17000000:'GetForce',0x18000000:'Back2'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
fix=[(i.address+i.size-4,ext[int(i.op_str,16)]) for i in md.disasm(bytes(b[:b.find(b'\n'+'背包'.encode())]),0)
     if i.mnemonic in('call','jmp') and i.op_str.startswith('0x') and int(i.op_str,16) in ext]
CAVE=0x400000; STUB=0x500000; HEAP=0x600000; STACK=0x800000
stubs={n:STUB+k*16 for k,n in enumerate(ext.values())}
for o,n in fix: struct.pack_into('<i',b,o,stubs[n]-(CAVE+o+4))
KEEP=[UC_X86_REG_R12,UC_X86_REG_R13,UC_X86_REG_R15]
def run(lists,force,skill=77,entry='book'):
    mu=Uc(UC_ARCH_X86,UC_MODE_64)
    for a in (CAVE,STUB,HEAP,STACK): mu.mem_map(a,0x100000)
    mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
    top=[HEAP+0x1000]
    def alloc(n): a=top[0]; top[0]+=(n+15)&~15; return a
    w=lambda a,v: mu.mem_write(a,struct.pack('<Q',v)); w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v))
    wobj=alloc(0x40); world=alloc(16); hero=alloc(0x300); w(wobj+0x20,world)
    def mklist(items):
        if items is None: return 0
        il=alloc(0x40); L=alloc(0x20); arr=alloc(0x20+8*len(items))
        w(il+0x28,L); w(L+0x10,arr); w32(L+0x18,len(items)); w32(arr+0x18,len(items))
        for k,(typ,sk,lv) in enumerate(items):
            it=alloc(0x80); bd=alloc(0x20); w32(it+0x14,typ); w32(it+0x40,lv); w(it+0x70,bd); w32(bd+0x10,sk); w(arr+0x20+8*k,it)
        return il
    w(hero+0x220,mklist(lists[0])); w(hero+0x228,mklist(lists[1]))
    fo=0
    if force: fo=alloc(0x100); w(fo+0xa0,mklist(lists[2])); w(fo+0xb8,mklist(lists[3]))
    qd=alloc(0x40)
    strs={}; out=[]
    def mk(s): a=alloc(16); strs[a]=s; return a
    def cstr(a):
        r=b''
        while (c:=mu.mem_read(a,1))!=b'\0': r+=c; a+=1
        return r.decode()
    inv={v:k for k,v in stubs.items()}
    def hook(mu,addr,size,ud):
        n=inv.get(addr)
        if not n: return
        rcx=mu.reg_read(UC_X86_REG_RCX); rdx=mu.reg_read(UC_X86_REG_RDX)
        if n in('Back','Back2'): out.append(n); mu.emu_stop(); return
        assert mu.reg_read(UC_X86_REG_RSP)%16==8, ('栈未对齐',n)
        r=0
        if n=='World': r=wobj
        elif n=='Player': assert rcx==world; r=hero
        elif n=='GetForce': assert rcx==hero and rdx&0xff==0; r=fo
        elif n=='StrNew': r=mk(cstr(rcx))
        elif n=='Color': r=mk(f'<{"白绿蓝紫黄红"[rdx]}>{strs[rcx]}')   # 颜色标记 <颜色>文字
        elif n=='Concat': r=mk(strs[rcx]+strs[rdx])
        elif n=='AddTab': assert rcx==qd; out.append(strs[rdx])
        for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
        mu.reg_write(UC_X86_REG_RAX,r)
    mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
    rsp=STACK+0x80000; mu.reg_write(UC_X86_REG_RSP,rsp)
    mu.mem_write(rsp+0x30,b'\x11'*16); mu.mem_write(rsp+0x40,b'\x22'*16)
    if entry=='book':
        item=alloc(0x80); bd=alloc(0x20); w(item+0x70,bd); w32(bd+0x10,skill)
        mu.reg_write(UC_X86_REG_R15,item); mu.reg_write(UC_X86_REG_R13,qd); mu.reg_write(UC_X86_REG_R12,0x1212); start=0
    else:
        kf=alloc(0x20); w32(kf+0x10,skill)
        mu.reg_write(UC_X86_REG_R15,qd); mu.reg_write(UC_X86_REG_RDI,kf); mu.reg_write(UC_X86_REG_R12,0x1212); mu.reg_write(UC_X86_REG_R13,0x1313); start=5
    keep={r:mu.reg_read(r) for r in KEEP}
    mu.emu_start(CAVE+start,0,count=300000)
    assert mu.reg_read(UC_X86_REG_RSP)==rsp
    for r,v in keep.items(): assert mu.reg_read(r)==v, ('寄存器被改',r)
    xmm6=mu.reg_read(UC_X86_REG_XMM6)
    want=0x22222222222222222222222222222222 if entry=='book' else 0x11111111111111111111111111111111
    assert xmm6==want, '原指令 movaps 不对'
    assert out[-1]==('Back' if entry=='book' else 'Back2')
    return out[:-1]
B=lambda lv,sk=77:(3,sk,lv)
cases=[
 ('全空无门派',            [[],[],None,None],False, ['背包：无']),
 ('只有背包',              [[B(5),B(5),B(1),(1,77,5),B(4,78)],[],[],[]],True, ['背包：<红>2红<绿>1绿']),
 ('背包没有，藏经阁有',      [[],[],[],[B(4),B(4),B(0)]],True, ['藏经阁：<黄>2黄<白>1白']),
 ('全有',                 [[B(5),B(5),B(1)],[B(4)],[B(3)],[B(0),B(2)]],True, ['背包：<红>2红<绿>1绿\n仓库：<黄>1黄\n宗门仓库：<紫>1紫\n藏经阁：<蓝>1蓝<白>1白']),
 ('仓库 / 藏经阁为空指针',   [[B(2)],None,[B(3)],None],True, ['背包：<蓝>1蓝\n宗门仓库：<紫>1紫']),
 ('只有宗门仓库',           [[],[],[B(4),B(4,1)],[]],True, ['宗门仓库：<黄>1黄']),
]
ok=True
for entry in ('book','skill'):
    for name,l,f,want in cases:
        got=run(l,f,entry=entry); good=got==want; ok&=good
        print('OK ' if good else 'BAD', entry, name, '=>', repr(got))
sys.exit(0 if ok else 1)
