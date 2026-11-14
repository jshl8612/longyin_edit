; 首次研读也可跳过：ReadBookController.RealStartReadBook 里，没有本品级研读记录时原本直接进研读面板；
; 这里改为当作已经研读过：在主角所有研读记录里，找「武学等级」（KungFuData 等级）与这本秘籍的武学相同的记录，
;   M = 这些记录在本秘籍品级上的最高值（都没有就取这些记录所有品级的最高值），把 M 写成这门武学这个品级的研读记录；
;   同等级一条记录都没有时，记录 = 默认值 / 0.9（按武学等级 0~5，默认值是跳过实得）。
; 然后走原版「是否跳过」流程：跳过实得 = 记录 × 0.9（原版比例），即同等级最高研读经验的 90%。
; 没有记录对象就新建并加到人物身上。
; 进入：rsi = ReadBookController(this)，r12 = 0，rsp 16 字节对齐。
; 可用：rax rcx rdx r8-r11 rbx rdi r14（原函数已保存）；不能动 rsi r12 r13 r15 rbp。
bits 64
default rel
org 0

EXT_OBJNEW  equ 0x10000000   ; il2cpp_object_new(klass)
EXT_WB      equ 0x11000000   ; il2cpp_gc_wbarrier_set_field(obj, addr, value)
EXT_CTOR    equ 0x12000000   ; SkillMaxPracticeExpData..ctor(_skillID)
EXT_ADDREC  equ 0x13000000   ; HeroData.AddSkillMaxPracticeExp(target)
EXT_MENU    equ 0x14000000   ; 原版「是否跳过」分支起点
EXT_PANEL   equ 0x15000000   ; 原版进入研读面板
EXT_DB      equ 0x16000000   ; KungfuSkillLvData.DataBase()（只读 this+10h 的 skillID）

%define OFF_HERO     0xa8    ; ReadBookController.targetHero
%define OFF_REC      0xb0    ; ReadBookController 本门武学的研读记录 SkillMaxPracticeExpData
%define OFF_SKILL    0xb8    ; ReadBookController KungfuSkillLvData
%define OFF_ITEM     0xc0    ; ReadBookController 秘籍 ItemData
%define OFF_RARE     0x40    ; ItemData.rareLv
%define OFF_RECLIST  0x250   ; HeroData.skillMaxPracticeExpData
%define OFF_RECSKILL 0x10    ; SkillMaxPracticeExpData.skillID
%define OFF_READEXP  0x18    ; SkillMaxPracticeExpData.maxReadExp (List<float>)
%define OFF_SKILLID  0x10    ; KungfuSkillLvData.skillID
%define OFF_KFLV     0x34    ; KungFuData 等级

; 栈：20h 下标  24h 个数  28h 目标等级  2Ch 同品级最高  30h 任意品级最高  34h 结果
;     38h 类指针  40h..5Fh 假 KungfuSkillLvData（只用 +10h）
%define S_IDX   0x20
%define S_CNT   0x24
%define S_LV    0x28
%define S_MSAME 0x2c
%define S_MANY  0x30
%define S_RES   0x34
%define S_KLASS 0x38
%define S_FAKE  0x40

start:
    sub rsp, 0x60
    mov rax, [rsi + OFF_ITEM]
    test rax, rax
    jz panel
    mov edi, [rax + OFF_RARE]
    cmp edi, 5
    ja panel
    mov rbx, [rsi + OFF_HERO]
    test rbx, rbx
    jz panel
    mov rcx, [rsi + OFF_SKILL]
    test rcx, rcx
    jz panel
    mov eax, [rcx + OFF_SKILLID]
    call kflv
    cmp eax, 5
    ja panel
    mov [rsp + S_LV], eax

    xor eax, eax
    mov [rsp + S_IDX], eax
    mov [rsp + S_MSAME], eax
    mov [rsp + S_MANY], eax
    mov [rsp + S_KLASS], rax
    mov rcx, [rbx + OFF_RECLIST]
    test rcx, rcx
    jz panel
    mov eax, [rcx + 0x18]
    mov [rsp + S_CNT], eax

.k:
    mov eax, [rsp + S_IDX]
    cmp eax, [rsp + S_CNT]
    jge .kend
    inc dword [rsp + S_IDX]
    mov rcx, [rbx + OFF_RECLIST]       ; 每次重新取，调用过游戏函数
    test rcx, rcx
    jz .kend
    cmp eax, [rcx + 0x18]
    jge .kend
    mov rcx, [rcx + 0x10]
    test rcx, rcx
    jz .kend
    cmp eax, [rcx + 0x18]
    jae .kend
    mov r14, [rcx + rax * 8 + 0x20]
    test r14, r14
    jz .k
    mov rax, [r14]
    mov [rsp + S_KLASS], rax           ; 类指针（任意一条记录都行）
    mov eax, [r14 + OFF_RECSKILL]
    call kflv
    cmp eax, [rsp + S_LV]
    jne .k
    mov rax, [r14 + OFF_READEXP]
    test rax, rax
    jz .k
    mov rdx, [rax + 0x10]
    mov ecx, [rax + 0x18]
    test rdx, rdx
    jz .k
    cmp ecx, [rdx + 0x18]
    jbe .j0
    mov ecx, [rdx + 0x18]
.j0:
    movss xmm0, [rsp + S_MSAME]
    movss xmm1, [rsp + S_MANY]
    cmp edi, ecx
    jae .j1
    maxss xmm0, [rdx + rdi * 4 + 0x20]
.j1:
    dec ecx
    js .j2
    maxss xmm1, [rdx + rcx * 4 + 0x20]
    jmp .j1
.j2:
    movss [rsp + S_MSAME], xmm0
    movss [rsp + S_MANY], xmm1
    jmp .k

.kend:
    xorps xmm2, xmm2
    movss xmm0, [rsp + S_MSAME]
    comiss xmm0, xmm2
    ja .scale
    movss xmm0, [rsp + S_MANY]
    comiss xmm0, xmm2
    ja .scale
    mov eax, [rsp + S_LV]              ; 同等级没有记录：默认值（已是实得）
    lea rdx, [defaults]
    movss xmm0, [rdx + rax * 4]
    divss xmm0, [k_90]
.scale:                                ; 有同等级记录：记录 = M（跳过时原版再 × 0.9）
    movss [rsp + S_RES], xmm0

    ; 本门武学的记录对象
    mov rax, [rsi + OFF_REC]
    test rax, rax
    jnz .rec
    mov r14, [rsp + S_KLASS]
    test r14, r14
    jz panel
    mov rcx, r14
    call $$ + EXT_OBJNEW
    test rax, rax
    jz panel
    mov r14, rax
    mov rax, [rsi + OFF_SKILL]
    mov edx, [rax + OFF_SKILLID]
    mov rcx, r14
    xor r8d, r8d
    call $$ + EXT_CTOR
    mov rcx, rbx
    mov rdx, r14
    xor r8d, r8d
    call $$ + EXT_ADDREC
    mov rcx, rsi                       ; this.记录 = 新对象（带写屏障）
    lea rdx, [rsi + OFF_REC]
    mov r8, r14
    call $$ + EXT_WB
.rec:
    mov rax, [rsi + OFF_REC]
    test rax, rax
    jz panel
    mov rax, [rax + OFF_READEXP]
    test rax, rax
    jz panel
    mov rdx, [rax + 0x10]
    test rdx, rdx
    jz panel
    cmp edi, [rax + 0x18]
    jae panel
    cmp edi, [rdx + 0x18]
    jae panel
    movss xmm0, [rsp + S_RES]
    movss [rdx + rdi * 4 + 0x20], xmm0
    add rsp, 0x60
    jmp $$ + EXT_MENU
panel:
    add rsp, 0x60
    jmp $$ + EXT_PANEL

; eax = skillID → eax = 武学等级（查不到返回 -1）。在 start 的栈帧里调用（用 S_FAKE）
kflv:
    sub rsp, 0x28
    lea rcx, [rsp + 0x28 + 8 + S_FAKE] ; 调用者栈帧里的假对象
    mov [rcx + OFF_SKILLID], eax
    xor edx, edx
    call $$ + EXT_DB
    test rax, rax
    jz .none
    mov eax, [rax + OFF_KFLV]
    add rsp, 0x28
    ret
.none:
    mov eax, -1
    add rsp, 0x28
    ret

align 4
k_80:     dd 0.8
k_90:     dd 0.9
; 同等级没有任何研读记录时的跳过经验（实得），武学等级 0~5
defaults: dd 400.0, 900.0, 1500.0, 2100.0, 2600.0, 3100.0
