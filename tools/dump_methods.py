import struct,sys,pefile
dllp,metap,out=sys.argv[1:4]
d=open(metap,'rb').read(); I=lambda o:struct.unpack_from('<i',d,o)[0]; H=lambda o:struct.unpack_from('<H',d,o)[0]
strOff=I(24); mOff=I(48); pOff=I(88); tOff=I(160); tSize=I(164); imOff=I(168); imSize=I(172)
S=lambda i: d[strOff+i:d.index(b'\0',strOff+i)].decode('utf-8','replace')
pe=pefile.PE(dllp,fast_load=True); D=open(dllp,'rb').read(); base=pe.OPTIONAL_HEADER.ImageBase
secs=[(s.VirtualAddress,s.PointerToRawData,s.SizeOfRawData) for s in pe.sections]
def off2va(o):
    for va,po,sz in secs:
        if po<=o<po+sz: return base+va+o-po
def va2off(v):
    r=v-base
    for va,po,sz in secs:
        if va<=r<va+sz: return po+r-va
def codegen(name):
    nb=name.encode()+b'\0'; pos=0
    while True:
        p=D.find(nb,pos)
        if p<0: return None
        pos=p+1; va=off2va(p)
        if not va: continue
        ndl=struct.pack('<Q',va); q=0
        while True:
            r=D.find(ndl,q)
            if r<0: break
            q=r+1
            if r%8: continue
            cnt=struct.unpack_from('<I',D,r+8)[0]; mp=struct.unpack_from('<Q',D,r+16)[0]
            mpo=va2off(mp) if mp else None
            if 0<cnt<300000 and mpo and mpo+cnt*8<=len(D): return mpo,cnt
w=open(out,'w',encoding='utf-8'); n=0
for im in range(imSize//40):
    b=imOff+im*40; name=S(I(b)); ts=I(b+8); tc=I(b+12)
    cg=codegen(name)
    if not cg: continue
    mpo,cnt=cg
    for t in range(ts,ts+tc):
        tb=tOff+t*88; tn=S(I(tb)); ms=I(tb+36); mc=H(tb+64)
        for k in range(mc):
            mb=mOff+(ms+k)*32; mn=S(I(mb)); ps=I(mb+12); tok=I(mb+20); pc=H(mb+30)
            rid=tok&0xffffff
            if not (0<rid<=cnt): continue
            va=struct.unpack_from('<Q',D,mpo+(rid-1)*8)[0]
            if not va: continue
            pn=[S(I(pOff+(ps+j)*12)) for j in range(pc)]
            w.write(f'{va:x}\t{tn}.{mn}({",".join(pn)})\n'); n+=1
print('methods',n)
