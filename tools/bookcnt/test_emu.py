# 用 unicorn 模拟执行 cave.bin，检查各种背包/仓库/门派组合下拼出的文字（pip install unicorn capstone；python test_emu.py cave.bin）
import struct,sys,capstone
from unicorn import *
from unicorn.x86_const import *
b=bytearray(open(sys.argv[1],'rb').read())
ext={0x10000000:'Player',0x11000000:'Concat',0x12000000:'StrNew',0x13000000:'Color',0x14000000:'AddTab',0x15000000:'ClassSlot',0x16000000:'Back',0x17000000:'GetForce'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
fix=[]
for i in md.disasm(bytes(b[:b.find('背包'.encode())]),0):
    tgt=None
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x'): tgt=int(i.op_str,16)
    elif 'rip' in i.op_str:
        import re; m=re.search(r'rip \+ (0x[0-9a-f]+)',i.op_str)
        if m: tgt=i.address+i.size+int(m.group(1),16)
    if tgt in ext: fix.append((i.address+i.size-4,ext[tgt]))
CAVE=0x400000; STUB=0x500000; HEAP=0x600000; STACK=0x800000
stubs={n:STUB+k*16 for k,n in enumerate(ext.values())}
stubs['ClassSlot']=HEAP  # data slot
for o,n in fix: struct.pack_into('<i',b,o,stubs[n]-(CAVE+o+4))
def run(lists,force,skill=77):
    mu=Uc(UC_ARCH_X86,UC_MODE_64)
    for a in (CAVE,STUB,HEAP,STACK): mu.mem_map(a,0x100000)
    mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
    top=[HEAP+0x1000]
    def alloc(n):
        a=top[0]; top[0]+=(n+15)&~15; return a
    w=lambda a,v: mu.mem_write(a,struct.pack('<Q',v))
    w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v))
    cls=alloc(0x100); stat=alloc(16); wo=alloc(0x40); world=alloc(16); hero=alloc(0x300)
    w(HEAP,cls); w(cls+0xb8,stat); w(stat,wo); w(wo+0x20,world)
    def mklist(items):
        if items is None: return 0
        il=alloc(0x40); L=alloc(0x20); arr=alloc(0x20+8*len(items))
        w(il+0x28,L); w(L+0x10,arr); w32(L+0x18,len(items)); w32(arr+0x18,len(items))
        for k,(typ,sk,lv) in enumerate(items):
            it=alloc(0x80); bd=alloc(0x20); w32(it+0x14,typ); w32(it+0x40,lv); w(it+0x70,bd); w32(bd+0x10,sk); w(arr+0x20+8*k,it)
        return il
    w(hero+0x220,mklist(lists[0])); w(hero+0x228,mklist(lists[1]))
    fo=0
    if force:
        fo=alloc(0x100); w(fo+0xa0,mklist(lists[2])); w(fo+0xb8,mklist(lists[3]))
    item=alloc(0x80); bd=alloc(0x20); w(item+0x70,bd); w32(bd+0x10,skill)
    strs={}; out=[]
    def mk(s):
        a=alloc(16); strs[a]=s; return a
    def cstr(a):
        r=b''
        while True:
            c=mu.mem_read(a,1)
            if c==b'\0': return r.decode()
            r+=c; a+=1
    inv={v:k for k,v in stubs.items()}
    def hook(mu,addr,size,ud):
        n=inv.get(addr)
        if not n: return
        rcx=mu.reg_read(UC_X86_REG_RCX); rdx=mu.reg_read(UC_X86_REG_RDX)
        assert n=='Back' or mu.reg_read(UC_X86_REG_RSP)%16==8, ('misaligned call',n)
        r=0
        if n=='Player': assert rcx==world; r=hero
        elif n=='GetForce': assert rcx==hero and rdx&0xff==0; r=fo
        elif n=='StrNew': r=mk(cstr(rcx))
        elif n=='Color': r=mk(f'[{"白绿蓝紫黄红"[rdx]}]{strs[rcx]}')
        elif n=='Concat': r=mk(strs[rcx]+strs[rdx])
        elif n=='AddTab': out.append(strs[rdx])
        elif n=='Back': mu.emu_stop(); return
        for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
        mu.reg_write(UC_X86_REG_RAX,r)
    mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
    rsp=STACK+0x80000; mu.reg_write(UC_X86_REG_RSP,rsp)
    mu.reg_write(UC_X86_REG_R15,item); mu.reg_write(UC_X86_REG_R13,0x1234)
    mu.emu_start(CAVE,0,count=200000)
    assert mu.reg_read(UC_X86_REG_RSP)==rsp
    return out
B=lambda lv,sk=77:(3,sk,lv)
cases=[
 ('全空无门派',[[],[],None,None],False),
 ('背包空+藏经阁有',[[B(5),(1,77,5),B(4,78)],[],[],[B(4),B(4),B(0)]],True),
 ('全有',[[B(5),B(5),B(1)],[B(4)],[B(3)],[B(0),B(2)]],True),
 ('仓库null',[[B(2)],None,[B(3)],None],True),
 ('只有宗门仓库',[[],[],[B(4),B(4,1)],[]],True),
]
for name,l,f in cases: print(name,'=>',repr(run(l,f)))
