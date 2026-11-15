# 用 unicorn 模拟执行 cave.bin（游戏函数全部用 Python 模拟），检查双修各步骤
# pip install unicorn capstone；python test_emu.py cave.bin
import struct,sys,re,capstone
from unicorn import *
from unicorn.x86_const import *
src=open('cave.asm',encoding='utf-8').read()
EXTS=re.findall(r'^EXT (\w+), *(\d+)',src,re.M)
b=bytearray(open(sys.argv[1],'rb').read())
CAVE=0x400000; STUB=0x500000; SLOTS=0x580000; HEAP=0x600000; STACK=0x900000
ext={0x20000000+int(i)*0x100000:n for n,i in EXTS}
stub={n:STUB+k*16 for k,n in enumerate(ext.values())}
SLOTNAMES=[n for n in ext.values() if n.startswith('X_SLOT')]
for k,n in enumerate(SLOTNAMES): stub[n]=SLOTS+k*8
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
end=bytes(b).find(b'\xcc'*8)
for i in md.disasm(bytes(b[:end]),0):
    t=None
    if i.mnemonic in('call','jmp') and i.op_str.startswith('0x'): t=int(i.op_str,16)
    m=re.search(r'\[rip \+ (0x[0-9a-f]+)\]',i.op_str)
    if m: t=i.address+i.size+int(m.group(1),16)
    if t in ext: struct.pack_into('<i',b,i.address+i.size-4,stub[ext[t]]-(CAVE+i.address+i.size))
sym={}
for i,l in enumerate(open('cave.lst',encoding='utf-8',errors='replace')) if False else []: pass
mu=Uc(UC_ARCH_X86,UC_MODE_64)
for a,sz in ((CAVE,0x10000),(STUB,0x1000),(SLOTS,0x1000),(HEAP,0x200000),(STACK,0x80000)): mu.mem_map(a,sz)
mu.mem_write(CAVE,bytes(b)); mu.mem_write(STUB,b'\xc3'*0x1000)
RESOLVED={n:0x7000+k*0x100 for k,n in enumerate(SLOTNAMES)}   # 初始化后的值
for k,n in enumerate(SLOTNAMES): mu.mem_write(SLOTS+k*8,struct.pack('<Q',(k<<1)|1|(1<<30)))  # 未初始化：编码值
top=[HEAP+0x1000]
def alloc(n): a=top[0]; top[0]+=(n+15)&~15; mu.mem_write(a,b'\0'*n); return a
w64=lambda a,v: mu.mem_write(a,struct.pack('<Q',v&0xffffffffffffffff)); w32=lambda a,v: mu.mem_write(a,struct.pack('<i',v)); w8=lambda a,v: mu.mem_write(a,bytes([v]))
r64=lambda a: struct.unpack('<Q',mu.mem_read(a,8))[0]; r32=lambda a: struct.unpack('<i',mu.mem_read(a,4))[0]
STRS={}
def mstr(s):
    a=alloc(0x18+2*len(s)); w32(a+0x10,len(s)); mu.mem_write(a+0x14,s.encode('utf-16-le')); STRS[a]=s; return a
def cstr(a):
    s=b''
    while True:
        c=bytes(mu.mem_read(a,1))
        if c==b'\0': return s.decode()
        s+=c; a+=1
def mlist(items,cap=None):          # 引用类型 List
    cap=cap or max(len(items),4); arr=alloc(0x20+8*cap); w32(arr+0x18,cap)
    for k,x in enumerate(items): w64(arr+0x20+8*k,x)
    l=alloc(0x20); w64(l+0x10,arr); w32(l+0x18,len(items)); return l
def ilist(vals,cap=64):             # List<int>
    arr=alloc(0x20+4*cap); w32(arr+0x18,cap)
    for k,x in enumerate(vals): w32(arr+0x20+4*k,x)
    l=alloc(0x20); w64(l+0x10,arr); w32(l+0x18,len(vals)); return l
def lget(l): n=r32(l+0x18); arr=r64(l+0x10); return [r64(arr+0x20+8*k) for k in range(n)]
def iget(l): n=r32(l+0x18); arr=r64(l+0x10); return [r32(arr+0x20+4*k) for k in range(n)]
# ---------- 世界 ----------
world=alloc(0x40); wobj=alloc(0x40); w64(wobj+0x20,world)
force=alloc(0x40); pc=alloc(0x100); pcgo=alloc(0x10); cc=alloc(0x100); gc=alloc(0x10); bu=alloc(0x10)
CORLIB=0x3333000; I32K=0x3334000
wk=alloc(0x80); spe=alloc(0x80); GO_T={}; TPOS={}; NAMET={}; RECT={}; T_GO={}; CLONE={}; ATTRI=alloc(0x40); PANEL=alloc(0x10)
def cname(n): a=alloc(0x20); mu.mem_write(a,n.encode()+b'\0'); k=alloc(0x40); w64(k+0x10,a); return k
TRK={True:cname('RectTransform'),False:cname('Transform')}
RATE={1:1.35,11:1.2,12:1.0}
KF={}       # KungFuData 对象 -> (类型, 等级)
SK={}       # KungfuSkillLvData -> (skillID, 名)
HEROES={}
def kf(t,lv): a=alloc(0x40); w32(a+0x30,t); w32(a+0x34,lv); return a
def skill(sid,t,lv,name): a=alloc(0x40); w32(a+0x10,sid); SK[a]=dict(id=sid,kf=kf(t,lv),name=name); return a
def hero(hid,female=True,area=5,inteam=False,leader=0,dead=False,prison=False,skills=(),pit=None):
    h=alloc(0x320); w32(h+0x58,hid); w8(h+0x80,int(female)); w8(h+0x61,int(dead)); w8(h+0xd1,int(prison))
    w32(h+0xc0,area); w8(h+0x2f0,int(inteam)); w32(h+0x2f4,leader)
    w64(h+0x260,mlist(list(skills)))
    for sk in skills: SK[sk]['owner']=h
    if pit is not False:
        p=alloc(0x60); w64(p+0x10,ilist(pit if pit is not None else [1]*30)); w64(h+0x308,p)
    HEROES[hid]=h; return h
P_NG=skill(100,0,3,'玉女心经'); P_EX=skill(200,4,5,'外功')
player=hero(1,female=False,area=5,skills=[P_NG,P_EX,skill(300,0,1,'吐纳法')])
A=hero(11,skills=[skill(100,0,3,'玉女心经'),skill(300,0,1,'吐纳法')])                        # 同区域
B=hero(12,area=9,inteam=True,leader=1,skills=[skill(100,0,3,'玉女心经')])                       # 队伍里
C=hero(13,area=9)                                                                            # 不在
D=hero(14,female=False)                                                                      # 男
E=hero(15,pit=[1]*30+[1]*10+[0])                                                             # 本月已双修
F=hero(16,dead=True); G=hero(17,prison=True)
H=hero(18,area=9,inteam=True,leader=99)                                                      # 别人队伍
own=[player,A,0,B,C,D,E,F,G,H]
state=dict(log=[])
def f32(x): return struct.unpack('<f',struct.pack('<I',x&0xffffffff))[0]
inv_stub={v:k for k,v in stub.items()}
def hook(mu,addr,size,ud):
    n=inv_stub.get(addr)
    if not n: return
    g=mu.reg_read; rcx,rdx,r8,r9=g(UC_X86_REG_RCX),g(UC_X86_REG_RDX),g(UC_X86_REG_R8),g(UC_X86_REG_R9); rsp=g(UC_X86_REG_RSP)
    if n in('X_SMSBACK','X_SFOBACK'): state['log'].append(n); mu.emu_stop(); return
    assert rsp%16==8, ('栈未对齐', n)
    arg=lambda k: r64(rsp+8+8*k)       # 第 k 个参数（k>=4）
    r=0
    if n=='X_WORLD': r=wobj
    elif n=='X_PLAYER': assert rcx==world; r=player
    elif n=='X_GETHERO': assert rcx==world; r=HEROES.get(rdx&0xffffffff,0)
    elif n=='X_GETFORCE': assert rcx==player and rdx&0xff==0; r=force
    elif n=='X_OWNHEROS': assert rcx==force; r=mlist(own)
    elif n=='X_FINDSKILL':
        sid=rdx&0xffffffff; r=next((s for s in lget(r64(rcx+0x260)) if SK[s]['id']==sid),0)
    elif n=='X_DATABASE': r=SK[rcx]['kf']
    elif n=='X_SKNAME': assert rdx&0xff==0; r=mstr(SK[rcx]['name'])
    elif n=='X_ADDEXP':
        state['log'].append(('addexp',rcx,f32(g(UC_X86_REG_XMM1)),r8,r9,arg(4)))
    elif n=='X_BELONG': assert rdx==0; r=SK[rcx].get('owner',0)
    elif n=='X_HIDEINTERACT': assert rcx==pc; state['log'].append('hideinteract')
    elif n=='X_WKINST': assert rcx==0; r=wk
    elif n=='X_STARTWORK':
        assert rcx==wk and r9==0 and arg(4)==0 and arg(7)==0
        state['log'].append(('work',STRS[rdx],r8&0xffffffff,STRS[arg(5)],STRS[arg(6)]))
    elif n=='X_SPEINST': assert rcx==0; r=spe
    elif n=='X_SHOWEXP':
        assert rcx==spe and r9&0xffffffff==1 and arg(5)==0
        go=alloc(0x20); tr=alloc(0x20); w64(tr,TRK[state.get('rect',True)]); GO_T[go]=tr; TPOS[tr]=(5.0,50.0,0.0); w64(spe+0xa8,go)
        nt=alloc(0x20); w64(nt,TRK[state.get('rect',True)]); NAMET[tr]=nt; TPOS[nt]=(20.0,30.0,0.0); RECT[tr]=(-150,-70,300,140); RECT[nt]=(-100,-10,200,40); T_GO[nt]=alloc(0x20)
        state['log'].append(('show',r32(SK[rdx]['owner']+0x58),SK[rdx]['id'],f32(g(UC_X86_REG_XMM2)),STRS[arg(4)]))
    elif n=='X_GETTRANS': assert rdx==0; r=GO_T[rcx]
    elif n=='X_GETRECT': assert r8==0 and r64(rdx)==TRK[True]; mu.mem_write(rcx,struct.pack('<4f',*RECT.get(rdx,(-150,-50,300,100)))); r=rcx
    elif n=='X_FIND': assert STRS[rdx]=='Name' and r8==0; r=NAMET.get(rcx,0)
    elif n=='X_CGETGO': assert rdx==0; r=T_GO[rcx]
    elif n=='X_INST':
        assert r8&0xff==0 and r9==0 and rcx in T_GO.values()
        go=alloc(0x20); tr=alloc(0x20); w64(tr,TRK[True]); GO_T[go]=tr; CLONE[tr]=rdx; r=go
    elif n=='X_CGETCOMP': assert rdx==RESOLVED['X_SLOTTEXT'] and rcx in CLONE; r=rcx+1
    elif n=='X_SETTEXT': assert r8==0; state['log'].append(('label',CLONE[rcx-1],TPOS.get(rcx-1),STRS[rdx]))
    elif n=='X_GETLPOS': assert r8==0; mu.mem_write(rcx,struct.pack('<3f',*TPOS[rdx])); r=rcx
    elif n=='X_SETLPOS': assert r8==0; v=struct.unpack('<3f',mu.mem_read(rdx,12)); TPOS[rcx]=v; state['log'].append(('pos',rcx,v))
    elif n=='X_NEEDS': assert rcx==ATTRI and r8==0; r=mstr('需求%d'%r32(rdx+0x58))
    elif n=='X_ACTIVE': assert rcx==PANEL and rdx==0; r=state.get('active',1)
    elif n=='X_HERONAME': assert rdx&0xff==0; r=mstr('人%d'%r32(rcx+0x58))
    elif n=='X_FIGHTRATE':
        assert SK[rdx]['owner']==rcx; mu.reg_write(UC_X86_REG_XMM0,struct.unpack('<I',struct.pack('<f',RATE[r32(rcx+0x58)]))[0])
    elif n=='X_F2S':
        assert STRS[rdx]=='+0;-0;0'; v=struct.unpack('<f',mu.mem_read(rcx,4))[0]; r=mstr(('+%d' if round(v)>0 else '%d')%round(v))
    elif n=='X_CCINST': assert rcx==0; r=cc
    elif n=='X_PCINST': assert rcx==0; r=pc
    elif n=='X_GCINST': assert rcx==0; r=gc
    elif n=='X_BUINST': assert rcx==0; r=bu
    elif n=='X_GETGO': assert rcx==pc; r=pcgo
    elif n=='X_HIDEBU': assert rcx==bu; state['log'].append('hidebu')
    elif n=='X_TIP': assert rcx==gc; state['log'].append(('tip',STRS[rdx]))
    elif n=='X_SHOWCHOOSE':
        assert rcx==cc and r9==pcgo and arg(7)==0 and arg(8)==0
        arr=r64(r8+0x10); items=[r64(arr+0x20+8*k) for k in range(r32(r8+0x18))]
        state['log'].append(('choose',rdx&0xffffffff,items,STRS[arg(4)],STRS[arg(5)],r32(rsp+8+8*6)))
    elif n=='X_INITMD':
        k=(rcx-SLOTS)//8; assert 0<=k<len(SLOTNAMES); w64(rcx,RESOLVED[SLOTNAMES[k]])
    elif n=='X_GETCOMP':
        assert rcx==state['result']
        assert rdx in (RESOLVED['X_SLOTHERO'],RESOLVED['X_SLOTSKILL']), hex(rdx)
        kind='hero' if rdx==RESOLVED['X_SLOTHERO'] else 'skill'
        assert kind==state['kind'], (kind,state['kind'])
        r=alloc(0x30); w64(r+0x20,state['data'])
    elif n=='X_OBJNEW':
        assert rcx in (RESOLVED['X_SLOTLISTK'],RESOLVED['X_SLOTPLOTK']); r=alloc(0x60); state.setdefault('objk',{})[r]=rcx
    elif n=='X_LISTCTOR': assert rdx==RESOLVED['X_SLOTLISTCTOR'] and state['objk'][rcx]==RESOLVED['X_SLOTLISTK']; state.setdefault('lists',{})[rcx]=[]
    elif n=='X_LISTADD': assert r8==RESOLVED['X_SLOTLISTADD']; state['lists'][rcx].append(STRS[rdx])
    elif n=='X_INTADD':
        assert r8==RESOLVED['X_SLOTINTADD'] and rdx&0xffffffff==1
        arr=r64(rcx+0x10); sz=r32(rcx+0x18); assert sz<r32(arr+0x18); w32(arr+0x20+4*sz,1); w32(rcx+0x18,sz+1)
    elif n=='X_STRNEW': r=mstr(cstr(rcx))
    elif n=='X_I2S': r=mstr(str(r32(rcx)))
    elif n=='X_CONCAT4': assert arg(4)==0; r=mstr(STRS[rcx]+STRS[rdx]+STRS[r8]+STRS[r9])
    elif n=='X_CONCAT2': assert r8==0; r=mstr(STRS[rcx]+STRS[rdx])
    elif n=='X_PLOTCTOR':
        assert state['objk'][rcx]==RESOLVED['X_SLOTPLOTK'] and r9&0xffffffff==3 and arg(5)==0
        state.setdefault('plots',{})[rcx]=(STRS[rdx],state['lists'][r8],STRS[arg(4)])
    elif n=='X_CHANGEPLOT': assert rcx==pc and r8==0 and r9==0; state['log'].append(('plot',)+state['plots'][rdx])
    elif n=='X_WB': assert rdx==rcx+0x70 and rcx==pc; w64(rdx,r8); state['log'].append(('target',r8))
    elif n=='X_CORLIB': r=CORLIB
    elif n=='X_CLASSFROM': assert rcx==CORLIB and cstr(rdx)=='System' and cstr(r8)=='Int32'; r=I32K; state['i32']=state.get('i32',0)+1
    elif n=='X_VALUEBOX': assert rcx==I32K; r=alloc(0x20); w32(r+0x10,r32(rdx)); state.setdefault('box',{})[r]=r32(rdx)
    else: raise Exception(n)
    for reg in (UC_X86_REG_RCX,UC_X86_REG_RDX,UC_X86_REG_R8,UC_X86_REG_R9,UC_X86_REG_R10,UC_X86_REG_R11): mu.reg_write(reg,0xdeadbeefdead)
    mu.reg_write(UC_X86_REG_XMM1,0x7fc00000)
    mu.reg_write(UC_X86_REG_RAX,r)
mu.hook_add(UC_HOOK_CODE,hook,begin=STUB,end=STUB+0x1000)
KEEP=[UC_X86_REG_RBX,UC_X86_REG_RSI,UC_X86_REG_RDI,UC_X86_REG_RBP,UC_X86_REG_R12,UC_X86_REG_R13,UC_X86_REG_R14,UC_X86_REG_R15]
def call(entry,param,thisobj=0,rbx=None):
    state['log']=[]
    rsp=STACK+0x40000-8; w64(rsp,0x77770000)
    for k,reg in enumerate(KEEP): mu.reg_write(reg,0x1111*(k+1))
    if rbx is not None: mu.reg_write(UC_X86_REG_RBX,rbx)
    keep={reg:mu.reg_read(reg) for reg in KEEP}
    mu.reg_write(UC_X86_REG_RSP,rsp); mu.reg_write(UC_X86_REG_RCX,thisobj); mu.reg_write(UC_X86_REG_RDX,param)
    mu.emu_start(CAVE+entry,0x77770000,count=500000)
    out=dict(log=state['log'],rax=mu.reg_read(UC_X86_REG_RAX))
    last=state['log'][-1] if state['log'] else None
    if last in('X_SMSBACK','X_SFOBACK'):   # 已执行 3 个 push
        exp=[keep[UC_X86_REG_RBX],keep[UC_X86_REG_RSI] if last=='X_SMSBACK' else keep[UC_X86_REG_RBP],keep[UC_X86_REG_R13] if last=='X_SMSBACK' else keep[UC_X86_REG_R15]]
        out['ok']=mu.reg_read(UC_X86_REG_RSP)==rsp-24 and [r64(rsp-8),r64(rsp-16),r64(rsp-24)]==exp and mu.reg_read(UC_X86_REG_RDX)==param
    else:
        out['ok']=mu.reg_read(UC_X86_REG_RSP)==rsp+8 and all(mu.reg_read(reg)==keep[reg] for reg in KEEP)
    return out
def sfo(p): return call(5,mstr(p),pc)
def sms(p): return call(0,mstr(p),bu)
def choose(kind,data): state['kind']=kind; state['data']=data; state['result']=alloc(0x10); w64(cc+0x48,state['result'])
ok=True
def check(name,cond,info=''):
    global ok; ok&=bool(cond); print('OK ' if cond else 'BAD', name, '' if cond else info)
ev=lambda r,k: [x for x in r['log'] if isinstance(x,tuple) and x[0]==k]
# 原函数照常
r=sms('0'); check('StudyMaxState("0") 照常执行', r['log']==['X_SMSBACK'] and r['ok'], r)
r=call(0,0,bu); check('StudyMaxState(null) 照常执行', r['log']==['X_SMSBACK'] and r['ok'], r)
r=sfo('123'); check('StudyFightOther("123") 照常执行', r['log']==['X_SFOBACK'] and r['ok'], r)
r=sfo('hhsx9'); check('StudyFightOther("hhsx9") 照常执行', r['log']==['X_SFOBACK'] and r['ok'], r)
r=sfo('hhsx'); check('StudyFightOther("hhsx") 照常执行', r['log']==['X_SFOBACK'] and r['ok'], r)
# 建筑按钮
r=sms('hhsx0'); c=ev(r,'choose')
check('建筑按钮 → 选人面板：只列同区域 / 本队、女、活着、没坐牢、本月没用过的本门弟子', r['ok'] and c==[('choose',2,[A,B],'StudyFightOther','hhsx1',0)], r)
check('双修前 E 的格子保持', iget(r64(r64(E+0x308)+0x10))[40]==0)
choose('hero',A); r=sfo('hhsx1'); p=ev(r,'plot')
check('选第一位 → 关建筑界面，剧情可再唤一位（3 个选项，说话人是她）', r['ok'] and 'hidebu' in r['log'] and len(p)==1 and p[0][2]==['再唤一位;StudyFightOther;hhsx2','就我们二人;StudyFightOther;hhsx3','改日再说;HideInteractUI'] and p[0][3]=='11' and '再唤一位' in p[0][1], r)
r=sfo('hhsx2'); c=ev(r,'choose')
check('再唤一位 → 选人面板排除第一位', r['ok'] and c==[('choose',2,[B],'StudyFightOther','hhsx4',0)], r)
choose('hero',B); r=sfo('hhsx4'); p=ev(r,'plot')
check('选第二位 → 剧情（开始双修 / 改日再说），说话人是第二位', r['ok'] and len(p)==1 and p[0][2]==['开始双修;StudyFightOther;hhsx3','改日再说;HideInteractUI'] and p[0][3]=='12', r)
old=alloc(0x10); w64(pc+0x70,old)
r=sfo('hhsx3'); r=sfo('hhsx3'); c=ev(r,'choose')
box=c[0][2][0] if c else 0
check('开始双修 → 武学面板：第一位的武学、只看内功、筛选 28、剧情目标人物 = 第二位', r['ok'] and len(c)==1 and c[0][1]==0 and state['box'].get(box)==11 and STRS.get(c[0][2][1])=='0' and c[0][3:]==('StudyFightOther','hhsx5',28) and r64(pc+0x70)==B, r)
Ask=lget(r64(A+0x260))[0]
choose('skill',Ask); r=sfo('hhsx5'); w=ev(r,'work')
check('选内功 → 剧情目标人物改回、关对话、开始「双修」进度 2 天（3 级），结束回调 hhsx6', r['ok'] and r64(pc+0x70)==old and 'hideinteract' in r['log'] and w==[('work','双修',2,'StudyFightOther','hhsx6')] and not ev(r,'show'), r)
check('进度中还没用次数', iget(r64(r64(A+0x308)+0x10))[40:41]!=[0])
w8(wk+0x60,1); r=sfo('hhsx6'); sh=ev(r,'show')
def nm(sk):
    r=call(20,0,sk); return r['ok'], STRS.get(r['rax'])
Ask_=lget(r64(A+0x260))[0]
check('经验条名字：框右边已有加成 → 只写人物名', [nm(P_NG),nm(Ask_),nm(lget(r64(B+0x260))[0])]==[(True,'人1'),(True,'人11'),(True,'人12')])
check('经验条名字：用过即恢复原样', nm(Ask_)==(True,'玉女心经'))
check('经验条名字：别的武学照原样', nm(P_EX)==(True,'外功'))
check('进度结束 → 经验条面板三条（主角、第一位、第二位），各 2100，统一标题', r['ok'] and sh==[('show',1,100,2100.0,'双修完成：玉女心经　各得实战经验 2100'),('show',11,100,2100.0,'双修完成：玉女心经　各得实战经验 2100'),('show',12,100,2100.0,'双修完成：玉女心经　各得实战经验 2100')], sh)
la=iget(r64(r64(A+0x308)+0x10)); lb=iget(r64(r64(B+0x308)+0x10))
check('两位弟子本月次数用掉（列表补到 41 格，其余补 1）', len(la)==41 and la[40]==0 and la[30:40]==[1]*10 and lb[40]==0, (la,lb))
r=sms('hhsx0'); t=ev(r,'tip')
check('本月都用过 → 提示没有可双修的人', r['ok'] and len(t)==1 and '没有可以双修' in t[0][1] and not ev(r,'choose'), r)
# 月初重置（模拟 ResetTime 全部设 1）
for h in (A,B):
    l=r64(r64(h+0x308)+0x10); arr=r64(l+0x10)
    for k in range(r32(l+0x18)): w32(arr+0x20+4*k,1)
r=sms('hhsx0'); check('重置后又能选', ev(r,'choose')[0][2]==[A,B])
# 只选一位：A 和主角共有的内功有 100、300
choose('hero',B); r=sfo('hhsx1'); p=ev(r,'plot')
check('只剩... 选 B 后还有 A 可唤', p[0][2][0].startswith('再唤一位'))
r=sfo('hhsx3'); c=ev(r,'choose')
check('只有一位 → 剧情目标人物 = 她自己', c and state['box'][c[0][2][0]]==12 and r64(pc+0x70)==B, r)
Bsk=lget(r64(B+0x260))[0]
choose('skill',Bsk); r=sfo('hhsx5'); w8(wk+0x60,0); r=sfo('hhsx6')
check('中途取消 → 不加经验、不用次数', r['ok'] and not ev(r,'show') and iget(r64(r64(B+0x308)+0x10))[40]==1, r)
r=sfo('hhsx6'); check('取消后再收到结束回调也不做事', not ev(r,'show'))
choose('hero',B); r=sfo('hhsx1'); r=sfo('hhsx3'); choose('skill',Bsk); r=sfo('hhsx5'); w8(wk+0x60,1); r=sfo('hhsx6'); e=ev(r,'show')
check('一位：主角 + 她', r['ok'] and [x[1] for x in e]==[1,12], e)
check('只用掉她的次数', iget(r64(r64(B+0x308)+0x10))[40]==0 and iget(r64(r64(A+0x308)+0x10))[40]==1)
# 没有别人可唤
choose('hero',A); r=sfo('hhsx1'); p=ev(r,'plot')
check('没有别人可唤 → 只有开始双修 / 改日再说', p and p[0][2]==['开始双修;StudyFightOther;hhsx3','改日再说;HideInteractUI'] and '再唤' not in p[0][1], p)
# 没有共同内功
w64(A+0x260,mlist([skill(999,0,2,'别的内功')]))
r=sfo('hhsx3'); p=ev(r,'plot')
check('没有共同内功 → 剧情提示、不开面板', r['ok'] and not ev(r,'choose') and p and p[0][2]==['只好作罢;HideInteractUI'], r)
# 等级上下限
w64(A+0x260,mlist([skill(300,0,1,'吐纳法')])); SK[lget(r64(A+0x260))[0]]['owner']=A
l=r64(r64(A+0x308)+0x10); w32(r64(l+0x10)+0x20+40*4,1)
r=sfo('hhsx3'); choose('skill',lget(r64(A+0x260))[0]); r=sfo('hhsx5'); w=ev(r,'work'); r=sfo('hhsx6'); e=ev(r,'show')
check('1 级内功 → 1 天、900', w and w[0][2]==1 and e and e[0][3]==900.0, (w,e))
for lv,days in ((0,1),(2,2),(4,3),(5,3)):
    w64(A+0x260,mlist([skill(300,0,lv,'吐纳法')])); SK[lget(r64(A+0x260))[0]]['owner']=A
    w64(player+0x260,mlist([skill(300,0,lv,'吐纳法')])); SK[lget(r64(player+0x260))[0]]['owner']=player
    for h in (A,):
        l=r64(r64(h+0x308)+0x10); w32(r64(l+0x10)+0x20+40*4,1)
    choose('hero',A); r=sfo('hhsx1'); r=sfo('hhsx3'); choose('skill',lget(r64(A+0x260))[0]); r=sfo('hhsx5'); w=ev(r,'work')
    r=sfo('hhsx6'); e=ev(r,'show')
    check('等级 %d → %d 天、%d 经验'%(lv,days,[400,900,1500,2100,2600,3100][lv]), w and w[0][2]==days and e and e[0][3]==[400,900,1500,2100,2600,3100][lv], (w,e))
check('Int32 类只查一次', state['i32']==1)
# 选人面板拿不到人物 → 什么都不做
choose('hero',0); r=sfo('hhsx1'); check('没选到人 → 不做事', r['ok'] and r['log']==[], r)
# 没有 playerInteractionTimeData 的人当作可用，用时不出错
X=hero(20,pit=False); own.append(X)
r=sms('hhsx0'); check('没有互动数据的人也列出', X in ev(r,'choose')[0][2])
# 经验条面板转接：主角.AddSkillFightExp(…, 弟子的武学) → 弟子.AddSkillFightExp(…)
def thunk(rcx,skill_,num):
    state['log']=[]
    rsp=STACK+0x40000-8; w64(rsp,0x77770000); w64(rsp+0x28,0xABCD)
    for k,reg in enumerate(KEEP): mu.reg_write(reg,0x1111*(k+1))
    keep={reg:mu.reg_read(reg) for reg in KEEP}
    mu.reg_write(UC_X86_REG_RSP,rsp); mu.reg_write(UC_X86_REG_RCX,rcx); mu.reg_write(UC_X86_REG_R8,skill_); mu.reg_write(UC_X86_REG_R9,0)
    mu.reg_write(UC_X86_REG_XMM1,struct.unpack('<I',struct.pack('<f',num))[0])
    mu.emu_start(CAVE+10,0x77770000,count=10000)
    return state['log'], mu.reg_read(UC_X86_REG_RSP)==rsp+8 and all(mu.reg_read(reg)==keep[reg] for reg in KEEP)
lg,okk=thunk(player,Ask,123.5)
check('面板加经验：弟子的武学由弟子加（经验、参数不变）', okk and lg==[('addexp',A,123.5,Ask,0,0xABCD)], lg)
lg,okk=thunk(player,P_NG,7.0)
check('面板加经验：主角的武学照旧由主角加', okk and lg==[('addexp',player,7.0,P_NG,0,0xABCD)], lg)
orphan=skill(5,0,0,'无主'); lg,okk=thunk(player,orphan,1.0)
check('找不到主人 → 照旧由主角加', okk and lg==[('addexp',player,1.0,orphan,0,0xABCD)], lg)
# 经验条排开（3 条，RectTransform 高 100 → 行距 110；第一条原位置 y=50）
w64(cc+0x18,PANEL)
def full(d2=True,rect=True):
    state['rect']=rect
    for h in (A,B):
        l=r64(r64(h+0x308)+0x10); w32(r64(l+0x10)+0x20+40*4,1)
    w64(A+0x260,mlist([skill(100,0,3,'玉女心经')])); SK[lget(r64(A+0x260))[0]]['owner']=A
    w64(player+0x260,mlist([P_NG])); SK[P_NG]['owner']=player
    w32(lget(r64(A+0x260))[0]+0x14,7); w32(P_NG+0x14,10); w32(lget(r64(B+0x260))[0]+0x14,3)
    choose('hero',A); sfo('hhsx1')
    if d2: sfo('hhsx2'); choose('hero',B); sfo('hhsx4')
    r=sfo('hhsx3'); return r
r=full(); w64(cc+0x60,mstr('hhsx5'))
nd=call(15,A,ATTRI,rbx=lget(r64(A+0x260))[0])
check('武学面板里第一位的武学：修炼需求列出主角、第一位、第二位', nd['ok'] and STRS.get(nd['rax'])=='人1（10级）：需求1\n人11（7级）：需求11\n人12（3级）：需求12', STRS.get(nd['rax']))
nd=call(15,B,ATTRI,rbx=lget(r64(B+0x260))[0]); check('别人的武学 → 照原样', nd['ok'] and STRS.get(nd['rax'])=='需求12', STRS.get(nd['rax']))
state['active']=0; nd=call(15,A,ATTRI,rbx=lget(r64(A+0x260))[0]); check('面板已关 → 照原样', STRS.get(nd['rax'])=='需求11'); state['active']=1
w64(cc+0x60,mstr('hhsx1')); nd=call(15,A,ATTRI,rbx=lget(r64(A+0x260))[0]); check('不是双修武学面板 → 照原样', STRS.get(nd['rax'])=='需求11'); w64(cc+0x60,mstr('hhsx5'))
choose('skill',lget(r64(A+0x260))[0]); r=sfo('hhsx5')
nd=call(15,A,ATTRI,rbx=lget(r64(A+0x260))[0]); check('选好武学后 → 照原样', STRS.get(nd['rax'])=='需求11')
w8(wk+0x60,1); r=sfo('hhsx6'); ps=ev(r,'pos')
lb=ev(r,'label')
check('每条框右边加「加成±P%」（左边对齐框右 +50，纵向与框居中）', [(x[2],x[3]) for x in lb]==[((300.0,-10.0,0.0),'加成+35%'),((300.0,-10.0,0.0),'加成+20%'),((300.0,-10.0,0.0),'加成0%')], lb)
ps=[p for p in ps if p[1] in GO_T.values() and p[1] not in CLONE]
check('结束 → 三条经验条上下排开（行距 150，主角在上）', r['ok'] and [p[2] for p in ps]==[(5.0,200.0,0.0),(5.0,50.0,0.0),(5.0,-100.0,0.0)], ps)
r=full(d2=False); w64(cc+0x60,mstr('hhsx5'))
nd=call(15,A,ATTRI,rbx=lget(r64(A+0x260))[0]); check('只有一位 → 修炼需求列出主角、她', STRS.get(nd['rax'])=='人1（10级）：需求1\n人11（7级）：需求11', STRS.get(nd['rax']))
choose('skill',lget(r64(A+0x260))[0]); r=sfo('hhsx5'); r=sfo('hhsx6'); ps=ev(r,'pos')
check('一位 → 两条（行距 150）', [p[2] for p in ps if p[1] not in CLONE]==[(5.0,125.0,0.0),(5.0,-25.0,0.0)], ps)
r=full(d2=False,rect=False); choose('skill',lget(r64(A+0x260))[0]); r=sfo('hhsx5'); r=sfo('hhsx6'); ps=ev(r,'pos')
check('非 RectTransform → 默认行距 150', [p[2] for p in ps]==[(5.0,125.0,0.0),(5.0,-25.0,0.0)], ps)
check('非 RectTransform → 不加框外文字，名字处写「人物名 ±P%」', not ev(r,'label') and [nm(P_NG),nm(lget(r64(A+0x260))[0])]==[(True,'人1 +35%'),(True,'人11 +20%')], (ev(r,'label'),nm(P_NG)))
sys.exit(0 if ok else 1)
