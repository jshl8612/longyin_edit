; 首次研读也可跳过：ReadBookController.RealStartReadBook 里，没有本品级研读记录时原本直接进研读面板；
; 这里改为：取主角所有武学在同一品级的最高研读经验（没有就取所有品级的最高）× 0.8，
; 作为本门武学本品级的研读记录（没有记录对象就新建并加到人物身上），再走原版「是否跳过」流程。
; 原版跳过给「记录 × 0.9」，所以记录写入 最高值 × 0.8 / 0.9，跳过后实得 = 最高值 × 80%。
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

%define OFF_HERO     0xa8    ; ReadBookController.targetHero
%define OFF_REC      0xb0    ; ReadBookController 本门武学的研读记录 SkillMaxPracticeExpData
%define OFF_SKILL    0xb8    ; ReadBookController KungfuSkillLvData
%define OFF_ITEM     0xc0    ; ReadBookController 秘籍 ItemData
%define OFF_RARE     0x40    ; ItemData.rareLv
%define OFF_RECLIST  0x250   ; HeroData.skillMaxPracticeExpData
%define OFF_READEXP  0x18    ; SkillMaxPracticeExpData.maxReadExp (List<float>)
%define OFF_SKILLID  0x10    ; KungfuSkillLvData.skillID

start:
    sub rsp, 0x30
    mov rax, [rsi + OFF_ITEM]
    test rax, rax
    jz panel
    mov edi, [rax + OFF_RARE]
    cmp edi, 5
    ja panel
    mov rbx, [rsi + OFF_HERO]
    test rbx, rbx
    jz panel

    ; 扫描人物所有研读记录：xmm0 = 同品级最高，xmm1 = 所有品级最高，r14 = 记录对象的类
    xorps xmm0, xmm0
    xorps xmm1, xmm1
    xor r14d, r14d
    mov rcx, [rbx + OFF_RECLIST]
    test rcx, rcx
    jz panel
    mov r8, [rcx + 0x10]               ; _items
    mov r9d, [rcx + 0x18]              ; _size
    test r8, r8
    jz panel
    cmp r9d, [r8 + 0x18]
    jbe .k0
    mov r9d, [r8 + 0x18]
.k0:
    xor r10d, r10d
.k:
    cmp r10d, r9d
    jge .kend
    mov r11, [r8 + r10 * 8 + 0x20]
    inc r10d
    test r11, r11
    jz .k
    mov r14, [r11]                     ; 类指针（任意一条记录都行）
    mov rax, [r11 + OFF_READEXP]
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
    cmp edi, ecx
    jae .j1
    maxss xmm0, [rdx + rdi * 4 + 0x20]
.j1:
    dec ecx
    js .k
    maxss xmm1, [rdx + rcx * 4 + 0x20]
    jmp .j1
.kend:
    xorps xmm2, xmm2
    comiss xmm0, xmm2
    ja .have
    movaps xmm0, xmm1
    comiss xmm0, xmm2
    jbe panel
.have:
    mulss xmm0, [k_rate]
    movss [rsp + 0x28], xmm0

    ; 本门武学的记录对象
    mov rax, [rsi + OFF_REC]
    test rax, rax
    jnz .rec
    test r14, r14
    jz panel
    mov rax, [rsi + OFF_SKILL]
    test rax, rax
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
    movss xmm0, [rsp + 0x28]
    movss [rdx + rdi * 4 + 0x20], xmm0
    add rsp, 0x30
    jmp $$ + EXT_MENU
panel:
    add rsp, 0x30
    jmp $$ + EXT_PANEL

align 4
k_rate: dd 0x3F638E39                  ; 0.8 / 0.9
