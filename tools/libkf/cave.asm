; 本门武学 = 藏经阁武学（只改主角所在门派，其他门派维持原样）
; 原版：出手者有门派，且 KungFuData.所属势力 == 出手者 belongForceID → 吃「本门武学威力」。
; 补丁：原判定不成立时，若出手者和主角同一门派，且这门武学的秘籍在该门派藏经阁里，也算本门武学。
; 两处挂钩（都是 cmp dword [rax+18h],ecx ; jne 短跳，共 5 字节）：
;   +0 BattleController.CountBaseDamage：rdx = 出手者 HeroData，rbx = KungfuSkillLvData；成立分支要用 rdx
;   +5 KungfuSkillLvData.GetSkillFightScore：r14 = 所属人物 HeroData，rdi = KungfuSkillLvData
; 两处 rsp 都是 16 字节对齐。
bits 64
default rel
org 0

EXT_YES1    equ 0x10000000   ; CountBaseDamage 成立分支（被替换指令之后）
EXT_NO1     equ 0x11000000   ; CountBaseDamage 不成立分支（原 jne 目标）
EXT_YES2    equ 0x12000000   ; GetSkillFightScore 成立分支
EXT_NO2     equ 0x13000000   ; GetSkillFightScore 不成立分支
EXT_WORLD   equ 0x14000000   ; 取 WorldData 所在对象的静态 getter（自带初始化）
EXT_PLAYER  equ 0x15000000   ; WorldData.Player()
EXT_FORCE   equ 0x16000000   ; HeroData.GetForce(includeServant)
EXT_FIND    equ 0x17000000   ; ForceData.BookStorageFindSkill(_skillID) → 藏经阁里的秘籍或 null

%define OFF_KFFORCE  0x18    ; KungFuData.所属势力
%define OFF_HFORCE   0x84    ; HeroData.belongForceID
%define OFF_SKILLID  0x10    ; KungfuSkillLvData.skillID
%define OFF_BSTORE   0xb8    ; ForceData.bookStorage（藏经阁）
%define OFF_ALLITEM  0x28    ; ItemListData.allItem

entry_dmg: jmp strict near hk_dmg       ; +0
entry_fs:  jmp strict near hk_fs        ; +5

hk_dmg:
    cmp dword [rax + OFF_KFFORCE], ecx
    je .yes
    push rdx
    sub rsp, 0x28
    mov rcx, rdx
    mov edx, [rbx + OFF_SKILLID]
    call inbook
    add rsp, 0x28
    pop rdx
    test eax, eax
    jnz .yes
    jmp $$ + EXT_NO1
.yes:
    jmp $$ + EXT_YES1

hk_fs:
    cmp dword [rax + OFF_KFFORCE], ecx
    je .yes
    sub rsp, 0x20
    mov rcx, r14
    mov edx, [rdi + OFF_SKILLID]
    call inbook
    add rsp, 0x20
    test eax, eax
    jnz .yes
    jmp $$ + EXT_NO2
.yes:
    jmp $$ + EXT_YES2

; rcx = 人物，edx = skillID → eax = 1：人物和主角同门派，且这门武学的秘籍在门派藏经阁里
inbook:
    push rsi
    push rdi
    push rbx
    sub rsp, 0x20
    mov rsi, rcx
    mov edi, edx
    test rsi, rsi
    jz .no
    xor ecx, ecx
    call $$ + EXT_WORLD
    test rax, rax
    jz .no
    mov rcx, [rax + 0x20]
    test rcx, rcx
    jz .no
    xor edx, edx
    call $$ + EXT_PLAYER
    test rax, rax
    jz .no
    mov ecx, [rax + OFF_HFORCE]
    test ecx, ecx
    js .no
    cmp ecx, [rsi + OFF_HFORCE]
    jne .no
    mov rcx, rsi
    xor edx, edx
    xor r8d, r8d
    call $$ + EXT_FORCE
    test rax, rax
    jz .no
    mov rcx, [rax + OFF_BSTORE]        ; 藏经阁为空时 BookStorageFindSkill 会抛空引用，先检查
    test rcx, rcx
    jz .no
    cmp qword [rcx + OFF_ALLITEM], 0
    je .no
    mov rcx, rax
    mov edx, edi
    xor r8d, r8d
    call $$ + EXT_FIND
    test rax, rax
    jz .no
    mov eax, 1
    jmp .r
.no:
    xor eax, eax
.r:
    add rsp, 0x20
    pop rbx
    pop rdi
    pop rsi
    ret
