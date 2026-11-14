# 用 unicorn 模拟执行 cave.bin，检查各种研读记录情况下的结果（pip install unicorn capstone；python test_emu.py cave.bin）
import struct,sys,capstone
from unicorn import *
from unicorn.x86_const import *
b=bytearray(open(sys.argv[1],'rb').read())
ext={0x10000000:'ObjNew',0x11000000:'Wb',0x12000000:'Ctor',0x13000000:'AddRec',0x14000000:'Menu',0x15000000:'Panel'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
fix=[(i.address+i.size-4,ext[int(i.op_str,16)]) for i in md.disasm(bytes(b[:-4]),0) if i.mnemonic in('call','jmp') and i.op_str.startswith('0x') and int(i.op_str,16) in ext]
CAVE=0x400000; STUB=0x500000; HEAP=0x600000; STACK=0x800000
stubs={n:STUB+k*16 for k,n in enumerate(ext.values())}
for o,n in fix: struct.pack_into('<i',b,o,stubs[n]-(CAVE+o+4))
f32=lambda v: struct.unpack('<f',struct.pack('<f',v))[0]
def run(hero_recs, cur, rare, skill=77):
    mu=Uc(UC_ARCH_X86,UC_MODE_64)
    for a in (CAVE,STUB,HEAP,STACK): mu.mem_map(a,0x100000)
    mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
    top=[HEAP+0x1000]
    def alloc(n): a=top[0]; top[0]+=(n+15)&~15; return a
    w=lambda a,v: mu.mem_write(a,struct.pack('<Q',v)); w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v))
    rd=lambda a: struct.unpack('<Q',mu.mem_read(a,8))[0]
    KLASS=alloc(0x100)
    def mkrec(sid,vals):
        r=alloc(0x20); L=alloc(0x20); arr=alloc(0x20+4*6)
        w(r,KLASS); w32(r+0x10,sid); w(r+0x18,L); w(L+0x10,arr); w32(L+0x18,6); w32(arr+0x18,6)
        for i,v in enumerate(vals): mu.mem_write(arr+0x20+4*i,struct.pack('<f',v))
        return r
    hero=alloc(0x300); recs=[mkrec(sid,v) for sid,v in hero_recs]
    lst=alloc(0x20); arr=alloc(0x20+8*max(1,len(recs))); w(hero+0x250,lst); w(lst+0x10,arr); w32(lst+0x18,len(recs)); w32(arr+0x18,max(1,len(recs)))
    for i,r in enumerate(recs): w(arr+0x20+8*i,r)
    this=alloc(0x100); item=alloc(0x80); kf=alloc(0x20); w32(kf+0x10,skill); w32(item+0x40,rare)
    w(this+0xa8,hero); w(this+0xb8,kf); w(this+0xc0,item)
    if cur is not None: w(this+0xb0,mkrec(skill,cur))
    log=[]; inv={v:k for k,v in stubs.items()}
    def hook(mu,addr,size,ud):
        n=inv.get(addr)
        if not n: return
        g=lambda r: mu.reg_read(r)
        rcx,rdx,r8=g(UC_X86_REG_RCX),g(UC_X86_REG_RDX),g(UC_X86_REG_R8)
        if n in('Menu','Panel'): log.append(n); mu.emu_stop(); return
        assert g(UC_X86_REG_RSP)%16==8,('align',n)
        r=0
        if n=='ObjNew': assert rcx==KLASS; r=alloc(0x20); w(r,KLASS); log.append('new')
        elif n=='Ctor':  # 构造：skillID + 6 个 0
            w32(rcx+0x10,rdx&0xffffffff); L=alloc(0x20); a=alloc(0x38); w(rcx+0x18,L); w(L+0x10,a); w32(L+0x18,6); w32(a+0x18,6); log.append(f'ctor({rdx&0xffffffff})')
        elif n=='AddRec': assert rcx==hero; log.append('addrec')
        elif n=='Wb': assert rcx==this and rdx==this+0xb0; w(rdx,r8); log.append('wb')
        for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
        mu.reg_write(UC_X86_REG_RAX,r)
    mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
    rsp=STACK+0x80000; mu.reg_write(UC_X86_REG_RSP,rsp)
    keep={UC_X86_REG_RSI:this,UC_X86_REG_R12:0,UC_X86_REG_R13:0x1313,UC_X86_REG_R15:0x1515,UC_X86_REG_RBP:0xbbbb}
    for k,v in keep.items(): mu.reg_write(k,v)
    mu.emu_start(CAVE,0,count=100000)
    assert mu.reg_read(UC_X86_REG_RSP)==rsp
    for k,v in keep.items(): assert mu.reg_read(k)==v,('clobbered',k)
    rec=rd(this+0xb0); val=None
    if rec:
        L=rd(rec+0x18); a=rd(L+0x10); val=struct.unpack('<f',mu.mem_read(a+0x20+4*rare,4))[0]
    return log, (None if val is None else round(val*0.9,1))
cases=[
 ('首次学（无记录），同品级有记录', [(1,[0,0,0,0,0,1000]),(2,[0,0,0,0,0,3880])], None, 5, ('Menu',3104.0)),
 ('已学但本品级为 0',            [(1,[0,0,0,500,0,1000]),(2,[0,0,0,300,0,0])], [0]*6, 3, ('Menu',400.0)),
 ('同品级无记录，用所有品级最高',  [(1,[0,0,0,0,0,1500])], None, 1, ('Menu',1200.0)),
 ('人物没有任何记录',             [], None, 5, ('Panel',None)),
 ('记录都是 0',                  [(1,[0]*6)], [0]*6, 2, ('Panel',0.0)),
]
ok=True
for name,recs,cur,rare,(want,wv) in cases:
    log,v=run(recs,cur,rare)
    good = log[-1]==want and (wv is None or v==wv)
    ok&=good
    print('OK ' if good else 'BAD', name, log, '跳过实得≈', v)
sys.exit(0 if ok else 1)
