; 拍卖自动出价
; 1) Button.Press 开头：按钮名为 hhAutoStart → 记下「本件自动出价」，照常执行（它的点击事件是开拍）；
;                        按钮名为 hhAutoBid  → 切换自动出价开关，不执行原来的点击（原来是手动出价）。
; 2) AuctionController.StartAuctionRoundButtonClicked 开头：自动出价开关 = 记下的值（普通开拍就是关）。
; 3) AuctionController.Update 开头（取完 this 之后）：
;    · 开拍按钮下面没有 hhAutoStart 就复制开拍按钮做一个，放在右边，文字「自动出价」；
;    · 出价按钮（playerAuctionUI/OfferButton）下面没有 hhAutoBid 就复制一个，放在右边；文字随开关显示「自动出价 / 停止自动」；
;    · 开关打开、正在竞价、主角参拍、没有点跳过、当前出价人不是主角：
;      下一口价 ≤ 背包钱数 → 以主角名义出价（与手动出价同一套 RefreshOfferMoney + StartCoroutine）；
;      否则关闭开关。
; 复制出来的按钮是原按钮的子物体，原按钮隐藏时一起隐藏。
bits 64
default rel
org 0

%macro EXT 2
%1 equ 0x20000000 + %2 * 0x100000
%endmacro
EXT X_PRESSBACK, 0     ; Button.Press 被替换指令之后
EXT X_STARTBACK, 1     ; StartAuctionRoundButtonClicked 被替换指令之后
EXT X_UPDBACK,   2     ; Update 被替换指令之后（je）
EXT X_GETNAME,   3     ; Object.get_name()
EXT X_GETTRANS,  4     ; GameObject.get_transform()
EXT X_FIND,      5     ; Transform.Find(n)
EXT X_INST,      6     ; Object.Instantiate(original,parent,instantiateInWorldSpace)
EXT X_SETNAME,   7     ; Object.set_name(value)
EXT X_SIZE,      8     ; RectTransform.get_sizeDelta()
EXT X_SETLPOS,   9     ; Transform.set_localPosition(value)
EXT X_CHILDN,    10    ; Transform.get_childCount()
EXT X_CHILD,     11    ; Transform.GetChild(index)
EXT X_GETCOMP,   12    ; Component.GetComponent<T>()（共享代码，T 由 MethodInfo 决定）
EXT X_SLOTTEXT,  13    ; 游戏里 GetComponent<Text> 的 MethodInfo 槽（rip 相对读取）
EXT X_SLOTBTN,   14    ; 游戏里 GetComponent<Button> 的 MethodInfo 槽
EXT X_SETTEXT,   15    ; LTLocalization.SetText(targetText,targetValue)
EXT X_INTERACT,  16    ; Selectable.set_interactable(value)
EXT X_STRNEW,    17    ; il2cpp_string_new
EXT X_NEXT,      18    ; AuctionController.GetNextOfferMoney()
EXT X_REFRESH,   19    ; AuctionController.RefreshOfferMoney(newOfferMoney,newOfferHero)
EXT X_STARTCO,   20    ; MonoBehaviour.StartCoroutine(routine)
EXT X_PLAYER,    21    ; WorldData.Player()
EXT X_WORLD,     22    ; 取 WorldData 所在对象的静态 getter（Update 里同样调用）

%define A_UI        0x20   ; playerAuctionUI (GameObject)
%define A_STARTBTN  0x28   ; startAuctionButton (GameObject)
%define A_STEP      0x48
%define A_ITEMS     0x58   ; auctionItemList（[0] 是当前拍品）   ; auctionStep（2 = 竞价中）
%define A_HAVEPL    0x4c   ; havePlayer
%define A_LEFT      0x94   ; offerRoundLeftTime
%define A_NOWHERO   0x98   ; nowOfferHero
%define A_SKIP      0xb4   ; skipping
%define H_ITEMS     0x220  ; HeroData.itemListData
%define L_MONEY     0x18   ; ItemListData.money

; 入口表（C# 按偏移挂钩）
entry_press:  jmp strict near press_hook      ; +0
entry_start:  jmp strict near start_hook      ; +5
entry_update: jmp strict near update_hook     ; +10

; ---------- Button.Press ----------
press_hook:                     ; rcx = Button
    push rcx
    sub rsp, 0x30
    xor edx, edx
    call $$ + X_GETNAME
    mov [rsp + 0x20], rax
    mov rcx, rax
    lea rdx, [w_start]
    call wstreq
    test eax, eax
    jz .notstart
    mov byte [g_pending], 1
    jmp .orig
.notstart:
    mov rcx, [rsp + 0x20]
    lea rdx, [w_bid]
    call wstreq
    test eax, eax
    jz .orig
    xor byte [g_auto], 1
    add rsp, 0x30
    pop rcx
    ret
.orig:
    add rsp, 0x30
    pop rcx
    push rbx
    sub rsp, 0x20
    jmp $$ + X_PRESSBACK

; ---------- StartAuctionRoundButtonClicked ----------
start_hook:                     ; rcx = this
    mov al, [g_pending]
    mov [g_auto], al
    mov byte [g_pending], 0
    call cur_item               ; 记下这一件拍品：只对这一件自动出价
    mov [g_item], rax
    push rbx
    sub rsp, 0x20
    jmp $$ + X_STARTBACK

; ---------- AuctionController.Update ----------
update_hook:                    ; rbx = this，rsp 16 对齐
    push rsi
    push rdi
    push r12
    push r13
    sub rsp, 0x40
    mov r12, rbx
    call ensure
    call autobid
    add rsp, 0x40
    pop r13
    pop r12
    pop rdi
    pop rsi
    mov dword [rsp + 0x50], 0   ; 被替换的两条原指令
    cmp dword [rbx + 0x48], 0
    jmp $$ + X_UPDBACK

; 确保两个按钮存在，并刷新出价旁按钮的文字 / 可点状态（r12 = this）
ensure:
    sub rsp, 0x28
    mov rcx, [r12 + A_STARTBTN]
    test rcx, rcx
    jz .bid
    xor edx, edx
    call $$ + X_GETTRANS
    test rax, rax
    jz .bid
    mov rsi, rax
    lea rcx, [s_start]
    call find_child
    test rax, rax
    jnz .bid
    mov rcx, rsi
    lea rdx, [s_start]
    lea r8, [s_auto]
    call makeclone
.bid:
    mov rcx, [r12 + A_UI]
    test rcx, rcx
    jz .done
    xor edx, edx
    call $$ + X_GETTRANS
    test rax, rax
    jz .done
    mov rsi, rax
    lea rcx, [s_offer]
    call find_child
    test rax, rax
    jz .done
    mov rsi, rax                ; OfferButton
    lea rcx, [s_bid]
    call find_child
    mov rdi, rax
    test rdi, rdi
    jnz .have
    mov rcx, rsi
    lea rdx, [s_bid]
    lea r8, [s_auto]
    call makeclone
    mov rdi, rax
    test rdi, rdi
    jz .done
.have:
    lea rdx, [s_auto]
    cmp byte [g_auto], 0
    je .lab
    lea rdx, [s_stop]
.lab:
    mov rcx, rdi
    call setlabel
    mov rcx, rdi
    mov rdx, [rel $$ + X_SLOTBTN]
    call $$ + X_GETCOMP
    test rax, rax
    jz .done
    mov rcx, rax
    mov dl, 1
    xor r8d, r8d
    call $$ + X_INTERACT
.done:
    add rsp, 0x28
    ret

; rsi = 父 Transform，rcx = 子物体名（UTF-8）→ rax = 子 Transform 或 0
find_child:
    sub rsp, 0x28
    call $$ + X_STRNEW
    mov rdx, rax
    mov rcx, rsi
    xor r8d, r8d
    call $$ + X_FIND
    add rsp, 0x28
    ret

; rcx = 原按钮 Transform，rdx = 新名字，r8 = 文字 → rax = 新按钮 Transform（失败 0）
makeclone:
    push rsi
    push rdi
    push rbx
    sub rsp, 0x40
    mov rsi, rcx
    mov rdi, rdx
    mov rbx, r8
    mov rcx, rsi
    mov rdx, rsi
    xor r8d, r8d
    xor r9d, r9d
    call $$ + X_INST
    test rax, rax
    jz .fail
    mov [rsp + 0x20], rax
    mov rcx, rdi
    call $$ + X_STRNEW
    mov rdx, rax
    mov rcx, [rsp + 0x20]
    xor r8d, r8d
    call $$ + X_SETNAME
    mov rcx, rsi
    xor edx, edx
    call $$ + X_SIZE            ; rax = (宽, 高)
    movd xmm0, eax
    addss xmm0, [k_gap]
    movss [rsp + 0x28], xmm0
    mov dword [rsp + 0x2c], 0
    mov dword [rsp + 0x30], 0
    mov rcx, [rsp + 0x20]
    lea rdx, [rsp + 0x28]
    xor r8d, r8d
    call $$ + X_SETLPOS
    mov rcx, [rsp + 0x20]
    mov rdx, rbx
    call setlabel
    mov rax, [rsp + 0x20]
.ret:
    add rsp, 0x40
    pop rbx
    pop rdi
    pop rsi
    ret
.fail:
    xor eax, eax
    jmp .ret

; rcx = 按钮 Transform，rdx = 文字（UTF-8）：设置它自己或第一层子物体上的 Text
setlabel:
    push rsi
    push rdi
    push rbx
    sub rsp, 0x30
    mov rsi, rcx
    mov rbx, rdx
    mov rcx, rsi
    mov rdx, [rel $$ + X_SLOTTEXT]
    call $$ + X_GETCOMP
    test rax, rax
    jnz .set
    mov rcx, rsi
    xor edx, edx
    call $$ + X_CHILDN
    mov edi, eax
    mov dword [rsp + 0x20], 0
.lp:
    mov eax, [rsp + 0x20]
    cmp eax, edi
    jge .out
    inc dword [rsp + 0x20]
    mov rcx, rsi
    mov edx, eax
    xor r8d, r8d
    call $$ + X_CHILD
    test rax, rax
    jz .lp
    mov rcx, rax
    mov rdx, [rel $$ + X_SLOTTEXT]
    call $$ + X_GETCOMP
    test rax, rax
    jz .lp
.set:
    mov [rsp + 0x28], rax
    mov rcx, rbx
    call $$ + X_STRNEW
    mov rdx, rax
    mov rcx, [rsp + 0x28]
    xor r8d, r8d
    call $$ + X_SETTEXT
.out:
    add rsp, 0x30
    pop rbx
    pop rdi
    pop rsi
    ret

; 自动出价（r12 = this）
autobid:
    sub rsp, 0x38
    cmp byte [g_auto], 0
    je .r
    cmp dword [r12 + A_STEP], 2
    jne .r
    mov rcx, r12
    call cur_item
    test rax, rax
    jz .r
    cmp rax, [g_item]
    jne .r
    cmp byte [r12 + A_HAVEPL], 0
    je .r
    cmp byte [r12 + A_SKIP], 0
    jne .r
    xorps xmm0, xmm0
    comiss xmm0, [r12 + A_LEFT]
    jae .r
    xor ecx, ecx
    call $$ + X_WORLD
    test rax, rax
    jz .r
    mov rcx, [rax + 0x20]
    test rcx, rcx
    jz .r
    xor edx, edx
    call $$ + X_PLAYER
    test rax, rax
    jz .r
    mov rsi, rax
    cmp rsi, [r12 + A_NOWHERO]
    je .r
    mov rcx, r12
    xor edx, edx
    call $$ + X_NEXT
    xorps xmm1, xmm1
    comiss xmm0, xmm1
    jbe .r
    movss [rsp + 0x20], xmm0
    mov rax, [rsi + H_ITEMS]
    test rax, rax
    jz .r
    cvtsi2ss xmm1, dword [rax + L_MONEY]
    comiss xmm0, xmm1
    jbe .bid
    mov byte [g_auto], 0        ; 钱不够了：停止自动出价
    jmp .r
.bid:
    movss xmm1, [rsp + 0x20]
    mov rcx, r12
    mov r8, rsi
    xor r9d, r9d
    call $$ + X_REFRESH
    test rax, rax
    jz .r
    mov rdx, rax
    mov rcx, r12
    xor r8d, r8d
    call $$ + X_STARTCO
.r:
    add rsp, 0x38
    ret

; rcx = this → rax = 当前拍品 auctionItemList[0]（没有为 0；叶子函数，不改 rcx 以外的非易失寄存器）
cur_item:
    xor eax, eax
    mov rdx, [rcx + A_ITEMS]
    test rdx, rdx
    jz .x
    cmp dword [rdx + 0x18], 0
    jle .x
    mov rdx, [rdx + 0x10]
    test rdx, rdx
    jz .x
    cmp dword [rdx + 0x18], 0
    jle .x
    mov rax, [rdx + 0x20]
.x:
    ret

; rcx = 托管字符串（可为空），rdx = {dd 长度, dw 字符…} → eax = 1 相等
wstreq:
    xor eax, eax
    test rcx, rcx
    jz .x
    mov r8d, [rcx + 0x10]
    cmp r8d, [rdx]
    jne .x
    xor r9d, r9d
.c:
    cmp r9d, r8d
    jge .eq
    mov r10w, [rcx + 0x14 + r9 * 2]
    cmp r10w, [rdx + 4 + r9 * 2]
    jne .x
    inc r9d
    jmp .c
.eq:
    mov eax, 1
.x:
    ret

times 8 db 0xCC                 ; 代码结束标记（gen.py 用）
align 4
k_gap:   dd 8.0
w_start: dd 11
         dw __utf16__('hhAutoStart')
align 4
w_bid:   dd 9
         dw __utf16__('hhAutoBid')
s_start: db "hhAutoStart", 0
s_bid:   db "hhAutoBid", 0
s_offer: db "OfferButton", 0
s_auto:  db "自动出价", 0
s_stop:  db "停止自动", 0
align 8
g_item:    dq 0
g_auto:    db 0
g_pending: db 0
