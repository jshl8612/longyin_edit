# 用 unicorn 模拟执行 cave.bin（Unity / 游戏函数全部用 Python 模拟），检查建按钮、点按钮、自动出价的逻辑
# pip install unicorn capstone；python test_emu.py cave.bin
import struct,sys,re,capstone
from unicorn import *
from unicorn.x86_const import *
src=open('cave.asm',encoding='utf-8').read()
EXTS=re.findall(r'^EXT (\w+), *(\d+)',src,re.M)
b=bytearray(open(sys.argv[1],'rb').read())
CAVE=0x400000; STUB=0x500000; HEAP=0x600000; STACK=0x900000; SLOTS=0x580000
ext={0x20000000+int(i)*0x100000:n for n,i in EXTS}
stub={n:STUB+k*16 for k,n in enumerate(ext.values())}
stub['X_SLOTTEXT']=SLOTS; stub['X_SLOTBTN']=SLOTS+8
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
end=bytes(b).find(b'\xcc'*8)
for i in md.disasm(bytes(b[:end]),0):
    t=None
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x'): t=int(i.op_str,16)
    m=re.search(r'\[rip \+ (0x[0-9a-f]+)\]',i.op_str)
    if m: t=i.address+i.size+int(m.group(1),16)
    if t in ext: struct.pack_into('<i',b,i.address+i.size-4,stub[ext[t]]-(CAVE+i.address+i.size))
TEXT_MI=0x111000; BTN_MI=0x222000
mu=Uc(UC_ARCH_X86,UC_MODE_64)
for a in (CAVE,STUB,HEAP,STACK,SLOTS): mu.mem_map(a,0x80000 if a!=HEAP else 0x200000)
mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
mu.mem_write(SLOTS,struct.pack('<QQ',TEXT_MI,BTN_MI))
top=[HEAP+0x1000]
def alloc(n): a=top[0]; top[0]+=(n+15)&~15; return a
w64=lambda a,v: mu.mem_write(a,struct.pack('<Q',v)); w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v))
r64=lambda a: struct.unpack('<Q',mu.mem_read(a,8))[0]
def mstr(s):
    a=alloc(0x18+2*len(s)); w32(a+0x10,len(s)); mu.mem_write(a+0x14,s.encode('utf-16-le')); STRS[a]=s; return a
STRS={}
OBJ={}   # 模拟的 Transform：id -> dict
def mk(name,w=100.0,text=None,children=()):
    a=alloc(0x40); OBJ[a]=dict(name=name,w=w,text=text,btn=alloc(0x10),children=list(children),pos=None,inter=None); return a
def clone(t,parent):
    o=OBJ[t]; c=mk(o['name'],o['w'],o['text'] and alloc(0x10),[clone(x,None) for x in o['children']])
    if o['text']: TEXTS[OBJ[c]['text']]=TEXTS.get(o['text'],'')
    if parent: OBJ[parent]['children'].append(c)
    return c
TEXTS={}
# 游戏对象
def mktext(): a=alloc(0x10); TEXTS[a]='?'; return a
startBtn=mk('StartAuctionButton',160.0,None,[mk('Text',10,mktext())])
offerBtn=mk('OfferButton',120.0,None,[mk('Text',10,mktext())])
ui=mk('PlayerAuctionUI',500,None,[mk('AddButton'),mk('MinusButton'),offerBtn])
this=alloc(0x100); player=alloc(0x300); ai=alloc(0x300); world=alloc(0x40); wobj=alloc(0x40)
items=alloc(0x30); arr=alloc(0x30); it1=alloc(0x40); it2=alloc(0x40)
w64(this+0x20,ui); w64(this+0x28,startBtn); w64(this+0x58,items); w64(items+0x10,arr); w32(items+0x18,1); w32(arr+0x18,1); w64(arr+0x20,it1)
inv=alloc(0x40); w64(player+0x220,inv); w32(inv+0x18,5000)
w64(wobj+0x20,world)
state=dict(next=1000.0, log=[])
def setf(a,v): mu.mem_write(a,struct.pack('<f',v))
inv_stub={v:k for k,v in stub.items()}
def hook(mu,addr,size,ud):
    n=inv_stub.get(addr)
    if not n: return
    g=mu.reg_read; rcx,rdx,r8=g(UC_X86_REG_RCX),g(UC_X86_REG_RDX),g(UC_X86_REG_R8)
    if n in('X_PRESSBACK','X_STARTBACK','X_UPDBACK'): state['log'].append(n); mu.emu_stop(); return
    assert g(UC_X86_REG_RSP)%16==8, ('栈未对齐', n)
    r=0
    if n=='X_GETNAME': r=mstr(OBJ[rcx]['name'])
    elif n=='X_GETTRANS': r=rcx
    elif n=='X_STRNEW':
        s=b''; a=rcx
        while True:
            c=mu.mem_read(a,1)
            if c==b'\0': break
            s+=bytes(c); a+=1
        r=mstr(s.decode())
    elif n=='X_FIND': r=next((c for c in OBJ[rcx]['children'] if OBJ[c]['name']==STRS[rdx]),0)
    elif n=='X_INST': r=clone(rcx,rdx); state['log'].append('clone '+OBJ[rcx]['name'])
    elif n=='X_SETNAME': OBJ[rcx]['name']=STRS[rdx]
    elif n=='X_SIZE': r=struct.unpack('<Q',struct.pack('<ff',OBJ[rcx]['w'],30.0))[0]
    elif n=='X_SETLPOS': OBJ[rcx]['pos']=struct.unpack('<3f',mu.mem_read(rdx,12))
    elif n=='X_CHILDN': r=len(OBJ[rcx]['children'])
    elif n=='X_CHILD': r=OBJ[rcx]['children'][rdx&0xffffffff]
    elif n=='X_GETCOMP':
        o=OBJ.get(rcx); assert o is not None
        r=(o['text'] or 0) if rdx==TEXT_MI else o['btn'] if rdx==BTN_MI else 1/0
    elif n=='X_SETTEXT': TEXTS[rcx]=STRS[rdx]
    elif n=='X_INTERACT': state['inter']=rdx&0xff
    elif n=='X_WORLD': r=wobj
    elif n=='X_PLAYER': assert rcx==world; r=player
    elif n=='X_NEXT': assert rcx==this; mu.reg_write(UC_X86_REG_XMM0,struct.unpack('<I',struct.pack('<f',state['next']))[0])
    elif n=='X_REFRESH':
        assert rcx==this and r8==player
        state['log'].append('出价 %g'%struct.unpack('<f',struct.pack('<I',g(UC_X86_REG_XMM1)&0xffffffff))[0]); r=alloc(0x10)
    elif n=='X_STARTCO': assert rcx==this; state['log'].append('协程')
    else: raise Exception(n)
    for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
    mu.reg_write(UC_X86_REG_RAX,r)
mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
KEEP=[UC_X86_REG_RBX,UC_X86_REG_RSI,UC_X86_REG_RDI,UC_X86_REG_RBP,UC_X86_REG_R12,UC_X86_REG_R13,UC_X86_REG_R14,UC_X86_REG_R15]
def call(entry,rcx=0,rbx=0,aligned_entry=False):
    state['log']=[]
    rsp=STACK+0x40000
    if not aligned_entry: rsp-=8; w64(rsp,0x77770000)   # 被 call 进入：返回地址
    for k,reg in enumerate(KEEP): mu.reg_write(reg,0x1111*(k+1))
    if rbx: mu.reg_write(UC_X86_REG_RBX,rbx)
    keep={reg:mu.reg_read(reg) for reg in KEEP}
    mu.reg_write(UC_X86_REG_RSP,rsp); mu.reg_write(UC_X86_REG_RCX,rcx)
    mu.mem_write(rsp+0x50,b'\xff'*4)
    mu.emu_start(CAVE+entry,0x77770000,count=200000)
    out=dict(log=state['log'],rsp_ok=None)
    last=state['log'][-1] if state['log'] else 'ret'
    if last=='X_PRESSBACK' or last=='X_STARTBACK':   # 已执行 push rbx; sub rsp,20h
        out['rsp_ok']=mu.reg_read(UC_X86_REG_RSP)==rsp-0x28 and r64(rsp-8)==keep[UC_X86_REG_RBX]
    elif last=='X_UPDBACK':
        out['rsp_ok']=mu.reg_read(UC_X86_REG_RSP)==rsp and struct.unpack('<i',mu.mem_read(rsp+0x50,4))[0]==0
    else: out['rsp_ok']=mu.reg_read(UC_X86_REG_RSP)==rsp+8
    for reg in KEEP:
        if mu.reg_read(reg)!=keep[reg]: out['clobber']=reg
    return out
def ga(): return mu.mem_read(CAVE+end+8,0x40)
def gflag(name):
    off=dict(re.findall(r'^(g_\w+):',src,re.M) and [])
def flags():
    # g_item / g_auto / g_pending 在文件末尾：dq, db, db
    raw=bytes(mu.mem_read(CAVE+len(b)-10,10)); return struct.unpack('<QBB',raw)
def update(step=2,hero=None,left=5.0,skip=0,haveP=1):
    w32(this+0x48,step); mu.mem_write(this+0x4c,bytes([haveP])); setf(this+0x94,left); w64(this+0x98,hero or ai); mu.mem_write(this+0xb4,bytes([skip]))
    return call(10,rbx=this,aligned_entry=True)
def bid_clone(): return next(c for c in OBJ[offerBtn]['children'] if OBJ[c]['name']=='hhAutoBid')
def start_clone(): return next(c for c in OBJ[startBtn]['children'] if OBJ[c]['name']=='hhAutoStart')
def label(t): return TEXTS[OBJ[OBJ[t]['children'][0]]['text']]
ok=True
def check(name,cond,info=''):
    global ok; ok&=bool(cond); print('OK ' if cond else 'BAD', name, info)
r=update(step=1)
check('首帧：建两个按钮', r['log'].count('clone StartAuctionButton')==1 and r['log'].count('clone OfferButton')==1 and r['rsp_ok'] and 'clobber' not in r, r)
check('按钮位置（原宽+8）', OBJ[start_clone()]['pos']==(168.0,0.0,0.0) and OBJ[bid_clone()]['pos']==(128.0,0.0,0.0))
check('按钮文字', label(start_clone())=='自动出价' and label(bid_clone())=='自动出价' and label(startBtn)=='?' )
r=update(step=1); check('第二帧不再复制', not any(x.startswith('clone') for x in r['log']), r['log'])
r=call(0,rcx=start_clone()); check('点「自动出价」开拍按钮 → 照常执行', r['log']==['X_PRESSBACK'] and r['rsp_ok'] and flags()[2]==1, (r,flags()))
r=call(5,rcx=this); check('开拍：开关打开、记下拍品', r['log']==['X_STARTBACK'] and r['rsp_ok'] and flags()==(it1,1,0), flags())
r=update(); check('别人领先 → 自动出价', r['log'][:2]==['出价 1000','协程'] and r['rsp_ok'] and 'clobber' not in r, r)
check('出价按钮文字变「停止自动」', label(bid_clone())=='停止自动')
r=update(hero=player); check('自己领先 → 不出价', '协程' not in r['log'], r['log'])
r=update(skip=1); check('点了跳过 → 不出价', '协程' not in r['log'])
r=update(left=0.0); check('时间到 → 不出价', '协程' not in r['log'])
r=update(haveP=0); check('主角不参拍 → 不出价', '协程' not in r['log'])
r=update(step=1); check('还没开始竞价 → 不出价', '协程' not in r['log'])
w64(arr+0x20,it2); r=update(); check('换了一件拍品 → 不出价', '协程' not in r['log']); w64(arr+0x20,it1)
state['next']=6000.0; r=update(); check('钱不够 → 停止自动', '协程' not in r['log'] and flags()[1]==0); state['next']=1000.0
r=update(); check('停止后文字变回「自动出价」', label(bid_clone())=='自动出价' and '协程' not in r['log'])
r=call(0,rcx=bid_clone()); check('点出价旁的按钮 → 打开（不执行原来的手动出价）', r['log']==[] and r['rsp_ok'] and flags()[1]==1, r)
r=update(); check('打开后自动出价', '协程' in r['log'])
r=call(0,rcx=bid_clone()); check('再点 → 关闭', flags()[1]==0)
r=call(0,rcx=offerBtn); check('点普通出价按钮 → 照常执行', r['log']==['X_PRESSBACK'] and flags()[1]==0)
r=call(5,rcx=this); check('普通开拍 → 开关关闭', flags()[1:]==(0,0))
sys.exit(0 if ok else 1)
