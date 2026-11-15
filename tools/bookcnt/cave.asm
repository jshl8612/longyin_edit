; 秘籍持有数量提示：鼠标放在秘籍或武功上时，在说明框最后追加一个框，显示同一门武学的秘籍
;   在主角背包 / 仓库 / 个人展厅（私宅）/ 宗门仓库 / 藏经阁 / 门派展厅 / 星辰阁（大隐阁特殊建筑星辰楼）里的数量
;   （按品质颜色）。数量为 0 的行不显示；
;   全都没有时显示「背包：无」。
; 两个入口（C# 按偏移挂钩）：
;   +0 QuickDetail.ShowBookQuickDetail 末尾：r13 = QuickDetail(this)，r15 = 悬停的 ItemData
;   +5 QuickDetail.ShowSkillQuickDetail 末尾：r15 = QuickDetail(this)，rdi = KungfuSkillLvData
;   两处 rsp 都是 16 字节对齐，被替换的原指令都是 movaps xmm6,[rsp+N]。
; 原函数尾声会从栈恢复 rbx rbp rsi rdi r14（以及各自 push 的），r12 / r13 由 bookinfo 自己保存。
bits 64
default rel
org 0

; ---- 外部地址（占位，补丁时按实际地址重定位）----
EXT_PLAYER  equ 0x10000000   ; WorldData.Player()
EXT_CONCAT  equ 0x11000000   ; String.Concat(str0,str1)
EXT_STRNEW  equ 0x12000000   ; il2cpp_string_new(const char*)
EXT_COLOR   equ 0x13000000   ; GlobalData.GenerateRareLvColorText(text,rareLv)
EXT_ADDTAB  equ 0x14000000   ; QuickDetail.AddDescribeTab(_text)
EXT_WORLD   equ 0x15000000   ; 取 WorldData 所在对象的静态 getter（自带初始化）
EXT_BACK    equ 0x16000000   ; 返回 ShowBookQuickDetail（被替换指令的下一条）
EXT_FORCE   equ 0x17000000   ; HeroData.GetForce(includeServant)
EXT_BACK2   equ 0x18000000   ; 返回 ShowSkillQuickDetail（被替换指令的下一条）

; 偏移（游戏 1.1.3）
%define OFF_ITEMLIST   0x220   ; HeroData.itemListData
%define OFF_STORAGE    0x228   ; HeroData.selfStorage
%define OFF_ALLITEM    0x28    ; ItemListData.allItem
%define OFF_TYPE       0x14    ; ItemData.type
%define OFF_RARE       0x40    ; ItemData.rareLv
%define OFF_BOOK       0x70    ; ItemData.bookData
%define OFF_SKILL      0x10    ; BookData.skillID
%define OFF_KFSKILL    0x10    ; KungfuSkillLvData.skillID
%define OFF_FSTORE     0xa0    ; ForceData.forceStorage（宗门仓库）
%define OFF_BSTORE     0xb8    ; ForceData.bookStorage（藏经阁）
%define OFF_FSHOW      0x150   ; ForceData.showRoomItems（门派展厅，List<ItemData>）
%define OFF_WSHOW      0x160   ; WorldData.showRoomItems（个人展厅，List<ItemData>）
%define OFF_WSPEBOOK   0x218   ; WorldData.speBookStorage（星辰阁，ItemListData）

entry_book:  jmp strict near book_entry       ; +0
entry_skill: jmp strict near skill_entry      ; +5

book_entry:
    test r15, r15
    jz .b
    mov rax, [r15 + OFF_BOOK]
    test rax, rax
    jz .b
    mov edx, [rax + OFF_SKILL]
    mov rcx, r13
    call bookinfo
.b:
    movaps xmm6, [rsp + 0x40]          ; 被替换的原指令
    jmp $$ + EXT_BACK

skill_entry:
    test rdi, rdi
    jz .s
    mov edx, [rdi + OFF_KFSKILL]
    mov rcx, r15
    call bookinfo
.s:
    movaps xmm6, [rsp + 0x30]          ; 被替换的原指令
    jmp $$ + EXT_BACK2

; rcx = QuickDetail，edx = skillID
; 栈：计数 int[6] ×7（每组 18h）—— 20h 背包、38h 仓库、50h 个人展厅、68h 宗门仓库、80h 藏经阁、98h 门派展厅、B0h 星辰阁
;     C8h WorldData
%define C_BAG    0x20
%define C_STORE  0x38
%define C_HSHOW  0x50
%define C_FSTORE 0x68
%define C_BOOKS  0x80
%define C_FSHOW  0x98
%define C_SPE    0xb0
%define S_WORLD  0xc8
bookinfo:
    push r12
    push r13
    push rbx
    push rbp
    sub rsp, 0xd8
    mov r13, rcx
    mov r12d, edx

    xor ecx, ecx
    call $$ + EXT_WORLD
    test rax, rax
    jz .done
    mov rcx, [rax + 0x20]
    test rcx, rcx
    jz .done
    mov [rsp + S_WORLD], rcx
    xor edx, edx
    call $$ + EXT_PLAYER
    test rax, rax
    jz .done
    mov rbp, rax

    xorps xmm0, xmm0                   ; 计数清零 [rsp+20h..C8h)
    lea rax, [rsp + 0x20]
    mov ecx, 0xa8 / 8
.z:
    mov qword [rax], 0
    add rax, 8
    dec ecx
    jnz .z

    mov rcx, [rbp + OFF_ITEMLIST]
    lea rdx, [rsp + C_BAG]
    call count
    mov rcx, [rbp + OFF_STORAGE]
    lea rdx, [rsp + C_STORE]
    call count
    mov rax, [rsp + S_WORLD]
    mov rcx, [rax + OFF_WSHOW]
    lea rdx, [rsp + C_HSHOW]
    call count_list
    mov rax, [rsp + S_WORLD]
    mov rcx, [rax + OFF_WSPEBOOK]
    lea rdx, [rsp + C_SPE]
    call count

    mov rcx, rbp                       ; 主角所属门派（不含附属）
    xor edx, edx
    xor r8d, r8d
    call $$ + EXT_FORCE
    test rax, rax
    jz .built
    mov rsi, rax
    mov rcx, [rsi + OFF_FSTORE]
    lea rdx, [rsp + C_FSTORE]
    call count
    mov rcx, [rsi + OFF_BSTORE]
    lea rdx, [rsp + C_BOOKS]
    call count
    mov rcx, [rsi + OFF_FSHOW]
    lea rdx, [rsp + C_FSHOW]
    call count_list
.built:
    xor ebx, ebx                       ; rbx = 拼好的文字
    lea rcx, [s_bag]
    lea rdx, [rsp + C_BAG]
    call addrow
    lea rcx, [s_store]
    lea rdx, [rsp + C_STORE]
    call addrow
    lea rcx, [s_hshow]
    lea rdx, [rsp + C_HSHOW]
    call addrow
    lea rcx, [s_force]
    lea rdx, [rsp + C_FSTORE]
    call addrow
    lea rcx, [s_books]
    lea rdx, [rsp + C_BOOKS]
    call addrow
    lea rcx, [s_fshow]
    lea rdx, [rsp + C_FSHOW]
    call addrow
    lea rcx, [s_spe]
    lea rdx, [rsp + C_SPE]
    call addrow
    test rbx, rbx
    jnz .add
    lea rcx, [s_bag + 1]               ; 全都没有：「背包：无」
    lea rdx, [rsp + C_BAG]
    call build
    test rax, rax
    jz .done
    mov rbx, rax
.add:
    mov rcx, r13
    mov rdx, rbx
    xor r8d, r8d
    call $$ + EXT_ADDTAB
.done:
    add rsp, 0xd8
    pop rbp
    pop rbx
    pop r13
    pop r12
    ret

; rcx = ItemListData, rdx = int[6]，r12d = skillID（叶子函数，只用易失寄存器）
count:
    test rcx, rcx
    jz count_list.r
    mov rcx, [rcx + OFF_ALLITEM]
; rcx = List<ItemData>（展厅直接是列表）, rdx = int[6]
count_list:
    test rcx, rcx
    jz .r
    mov r9d, [rcx + 0x18]              ; List._size
    mov r10, [rcx + 0x10]              ; List._items
    test r10, r10
    jz .r
    cmp r9d, [r10 + 0x18]              ; 不超过数组长度
    jbe .ok
    mov r9d, [r10 + 0x18]
.ok:
    xor r8d, r8d
.l:
    cmp r8d, r9d
    jge .r
    mov r11, [r10 + r8 * 8 + 0x20]
    inc r8d
    test r11, r11
    jz .l
    cmp dword [r11 + OFF_TYPE], 3      ; 3 = 秘籍
    jne .l
    mov rax, [r11 + OFF_BOOK]
    test rax, rax
    jz .l
    cmp [rax + OFF_SKILL], r12d
    jne .l
    mov eax, [r11 + OFF_RARE]
    cmp eax, 5
    ja .l
    inc dword [rdx + rax * 4]
    jmp .l
.r:
    ret

; rcx = 前缀（以换行开头），rdx = int[6]：全为 0 就跳过；
; 否则第一行 rbx = build(前缀去掉换行)，之后 rbx = rbx + build(前缀)
addrow:
    sub rsp, 0x28
    mov eax, [rdx]
    or eax, [rdx + 4]
    or eax, [rdx + 8]
    or eax, [rdx + 12]
    or eax, [rdx + 16]
    or eax, [rdx + 20]
    jz .r
    test rbx, rbx
    jnz .more
    inc rcx                            ; 跳过开头的换行
    call build
    mov rbx, rax
    jmp .r
.more:
    call build
    test rax, rax
    jz .r
    mov rcx, rbx
    mov rdx, rax
    xor r8d, r8d
    call $$ + EXT_CONCAT
    test rax, rax
    jz .r
    mov rbx, rax
.r:
    add rsp, 0x28
    ret

; rcx = 前缀（UTF-8），rdx = int[6] → rax = 托管字符串（不改 rbx）
build:
    sub rsp, 0x48
    mov rsi, rdx
    call $$ + EXT_STRNEW
    mov rdi, rax
    mov dword [rsp + 0x44], 0
    mov r14d, 5
.lv:
    mov eax, [rsi + r14 * 4]
    test eax, eax
    jz .next
    mov dword [rsp + 0x44], 1
    lea r8, [rsp + 0x30]               ; 数字倒着写到 0x30 之前
    mov ecx, 10
.d:
    xor edx, edx
    div ecx
    add dl, '0'
    dec r8
    mov [r8], dl
    test eax, eax
    jnz .d
    lea rax, [colors]
    lea rdx, [r14 + r14 * 2]
    movzx ecx, word [rax + rdx]
    mov [rsp + 0x30], cx
    mov cl, [rax + rdx + 2]
    mov [rsp + 0x32], cl
    mov byte [rsp + 0x33], 0
    mov rcx, r8
    call $$ + EXT_STRNEW
    mov rcx, rax
    mov edx, r14d
    xor r8d, r8d
    call $$ + EXT_COLOR
    mov rcx, rdi
    mov rdx, rax
    xor r8d, r8d
    call $$ + EXT_CONCAT
    mov rdi, rax
.next:
    dec r14d
    jns .lv
    cmp dword [rsp + 0x44], 0
    jne .out
    lea rcx, [s_none]
    call $$ + EXT_STRNEW
    mov rcx, rdi
    mov rdx, rax
    xor r8d, r8d
    call $$ + EXT_CONCAT
    mov rdi, rax
.out:
    mov rax, rdi
    add rsp, 0x48
    ret

s_bag:   db 10, "背包：", 0
s_store: db 10, "仓库：", 0
s_force: db 10, "宗门仓库：", 0
s_books: db 10, "藏经阁：", 0
s_hshow: db 10, "个人展厅：", 0
s_fshow: db 10, "门派展厅：", 0
s_spe:   db 10, "星辰阁：", 0
s_none:  db "无", 0
colors:  db "白绿蓝紫黄红"
