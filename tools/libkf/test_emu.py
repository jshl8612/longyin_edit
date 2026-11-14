# 用 unicorn 模拟执行 cave.bin，检查两处「本门武学」判定在各种情况下走哪个分支
# pip install unicorn capstone；python test_emu.py cave.bin
import struct,sys,capstone
from unicorn import *
from unicorn.x86_const import *
b=bytearray(open(sys.argv[1],'rb').read())
ext={0x10000000:'Yes1',0x11000000:'No1',0x12000000:'Yes2',0x13000000:'No2',0x14000000:'World',0x15000000:'Player',0x16000000:'GetForce',0x17000000:'Find'}
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
CAVE=0x400000; STUB=0x500000; HEAP=0x600000; STACK=0x800000
stubs={n:STUB+k*16 for k,n in enumerate(ext.values())}
for i in md.disasm(bytes(b),0):
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x') and int(i.op_str,16) in ext:
        struct.pack_into('<i',b,i.address+i.size-4,stubs[ext[int(i.op_str,16)]]-(CAVE+i.address+i.size))
KEEP=[UC_X86_REG_RBX,UC_X86_REG_RSI,UC_X86_REG_RDI,UC_X86_REG_RBP,UC_X86_REG_R12,UC_X86_REG_R13,UC_X86_REG_R14,UC_X86_REG_R15]
def run(entry, kf_force, hero_force, player_force, lib, skill=77, has_force=True, bstore=True):
    mu=Uc(UC_ARCH_X86,UC_MODE_64)
    for a in (CAVE,STUB,HEAP,STACK): mu.mem_map(a,0x100000)
    mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
    top=[HEAP+0x1000]
    def alloc(n): a=top[0]; top[0]+=(n+15)&~15; return a
    w=lambda a,v: mu.mem_write(a,struct.pack('<Q',v)); w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v))
    wobj=alloc(0x40); world=alloc(0x40); player=alloc(0x300); hero=alloc(0x300); kfd=alloc(0x40); kls=alloc(0x40); force=alloc(0x100)
    if bstore: st=alloc(0x40); w(force+0xb8,st); w(st+0x28,alloc(0x20))
    w(wobj+0x20,world); w32(player+0x84,player_force); w32(hero+0x84,hero_force); w32(kfd+0x18,kf_force); w32(kls+0x10,skill)
    log=[]; inv={v:k for k,v in stubs.items()}
    def hook(mu,addr,size,ud):
        n=inv.get(addr)
        if not n: return
        g=mu.reg_read; rcx,rdx=g(UC_X86_REG_RCX),g(UC_X86_REG_RDX)
        if n in('Yes1','No1','Yes2','No2'):
            log.append(n); log.append(('rdx',rdx)); mu.emu_stop(); return
        assert g(UC_X86_REG_RSP)%16==8, ('栈未对齐',n)
        r=0
        if n=='World': r=wobj
        elif n=='Player': assert rcx==world; r=player
        elif n=='GetForce': assert rcx==hero and rdx&0xff==0; r=force if has_force else 0
        elif n=='Find': assert rcx==force; r=alloc(0x10) if (rdx&0xffffffff) in lib else 0
        for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
        mu.reg_write(UC_X86_REG_RAX,r)
    mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
    rsp=STACK+0x80000; mu.reg_write(UC_X86_REG_RSP,rsp)
    for k,reg in enumerate(KEEP): mu.reg_write(reg,0x1111*(k+1))
    mu.reg_write(UC_X86_REG_RAX,kfd); mu.reg_write(UC_X86_REG_RCX,hero_force)
    if entry=='dmg': mu.reg_write(UC_X86_REG_RDX,hero); mu.reg_write(UC_X86_REG_RBX,kls); start=0
    else: mu.reg_write(UC_X86_REG_R14,hero); mu.reg_write(UC_X86_REG_RDI,kls); start=5
    keep={reg:mu.reg_read(reg) for reg in KEEP}
    mu.emu_start(CAVE+start,0,count=100000)
    assert mu.reg_read(UC_X86_REG_RSP)==rsp, '栈没恢复'
    for reg,v in keep.items(): assert mu.reg_read(reg)==v, ('寄存器被改',reg)
    branch=log[0]
    if entry=='dmg' and branch=='Yes1': assert log[1][1]==hero, '成立分支 rdx 必须是出手者'
    return branch.startswith('Yes')
cases=[
 # (说明, 武学所属势力, 出手者门派, 主角门派, 藏经阁里的武学, 期望是否本门)
 ('原判定成立（其他门派照旧）',            18,18,28,set(),  True),
 ('其他门派：藏经阁有也不算',              5, 18,28,{77},   False),
 ('主角门派：武学在藏经阁 → 算本门',        5, 28,28,{77},   True),
 ('主角门派：武学不在藏经阁 → 不算',        5, 28,28,{12},   False),
 ('主角没有门派 → 维持原判定',             5, 28,-1,{77},   False),
]
ok=True
for entry in ('dmg','fs'):
    for name,kf,hf,pf,lib,want in cases:
        got=run(entry,kf,hf,pf,lib); good=got==want; ok&=good
        print('OK ' if good else 'BAD', entry, name, got)
    got=run(entry,5,28,28,{77},has_force=False); ok&=(got==False); print('OK ' if got==False else 'BAD', entry, '取不到门派 → 不算')
    got=run(entry,5,28,28,{77},bstore=False); ok&=(got==False); print('OK ' if got==False else 'BAD', entry, '门派没有藏经阁列表 → 不算（不调用会抛异常的函数）')
sys.exit(0 if ok else 1)
