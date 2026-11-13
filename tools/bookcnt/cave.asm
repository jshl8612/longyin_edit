; 秘籍持有数量提示：在 QuickDetail.ShowBookQuickDetail 末尾（所有特效框之后）追加一个说明框
; 进入时：r13 = QuickDetail(this)，r15 = 悬停的 ItemData；rsp 16 字节对齐。
; 所有寄存器在原函数尾声都会从栈恢复，这里可以随意使用（r13/r15 只读）。
bits 64
default rel
org 0

; ---- 外部地址（占位，补丁时按实际地址重定位）----
EXT_PLAYER  equ 0x10000000   ; WorldData.Player()
EXT_CONCAT  equ 0x11000000   ; String.Concat(str0,str1)
EXT_STRNEW  equ 0x12000000   ; il2cpp_string_new(const char*)
EXT_COLOR   equ 0x13000000   ; GlobalData.GenerateRareLvColorText(text,rareLv)
EXT_ADDTAB  equ 0x14000000   ; QuickDetail.AddDescribeTab(_text)
EXT_CLASS   equ 0x15000000   ; 原函数取 WorldData 用的类指针槽（rip 相对）
EXT_BACK    equ 0x16000000   ; 返回原函数（被替换指令的下一条）

; 偏移（游戏 1.1.3）
%define OFF_ITEMLIST   0x220   ; HeroData.itemListData
%define OFF_STORAGE    0x228   ; HeroData.selfStorage
%define OFF_ALLITEM    0x28    ; ItemListData.allItem
%define OFF_TYPE       0x14    ; ItemData.type
%define OFF_RARE       0x40    ; ItemData.rareLv
%define OFF_BOOK       0x70    ; ItemData.bookData
%define OFF_SKILL      0x10    ; BookData.skillID

start:
    sub rsp, 0x60
    test r15, r15
    jz .done
    mov rax, [r15 + OFF_BOOK]
    test rax, rax
    jz .done
    mov r12d, [rax + OFF_SKILL]

    mov rax, [rel $$ + EXT_CLASS]
    test rax, rax
    jz .done
    mov rax, [rax + 0xb8]
    test rax, rax
    jz .done
    mov rcx, [rax]
    test rcx, rcx
    jz .done
    mov rcx, [rcx + 0x20]
    test rcx, rcx
    jz .done
    xor edx, edx
    call $$ + EXT_PLAYER
    test rax, rax
    jz .done
    mov rbp, rax

    xorps xmm0, xmm0                   ; 计数清零：背包 [rsp+20h..37h]，仓库 [rsp+38h..4Fh]
    movups [rsp + 0x20], xmm0
    movups [rsp + 0x30], xmm0
    movups [rsp + 0x40], xmm0

    mov rcx, [rbp + OFF_ITEMLIST]
    lea rdx, [rsp + 0x20]
    call count
    mov rcx, [rbp + OFF_STORAGE]
    lea rdx, [rsp + 0x38]
    call count

    lea rcx, [s_bag]
    lea rdx, [rsp + 0x20]
    call build
    mov rbx, rax
    lea rcx, [s_store]
    lea rdx, [rsp + 0x38]
    call build
    mov rcx, rbx
    mov rdx, rax
    xor r8d, r8d
    call $$ + EXT_CONCAT
    test rax, rax
    jz .done
    mov rcx, r13
    mov rdx, rax
    xor r8d, r8d
    call $$ + EXT_ADDTAB
.done:
    add rsp, 0x60
    movaps xmm6, [rsp + 0x40]          ; 被替换的原指令
    jmp $$ + EXT_BACK

; rcx = ItemListData, rdx = int[6]，r12d = skillID（叶子函数，只用易失寄存器）
count:
    test rcx, rcx
    jz .r
    mov rcx, [rcx + OFF_ALLITEM]
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

s_bag:   db "背包：", 0
s_store: db 10, "仓库：", 0
s_none:  db "无", 0
colors:  db "白绿蓝紫黄红"
