; 合欢殿「双修」
; 入口：合欢殿互动选项「双修…-我-StudyMaxState-hhsx0」→ BuildingUIController.StudyMaxState("hhsx0")。
; 之后的每一步都用 PlotController.StudyFightOther("hhsxN") 串起来（选人面板的回调、剧情选项都是按名字调用这个函数）：
;   hhsx0 建筑按钮：列出可双修的女弟子，打开选人面板（选中后回调 hhsx1）；没有就在鼠标处提示
;   hhsx1 选好第一位：关闭建筑界面，弹出剧情（「再唤一位 hhsx2 / 就我们二人 hhsx3 / 改日再说」，没有别人可选时只有「开始双修 / 改日再说」）
;   hhsx2 再唤一位：打开选人面板（排除第一位，选中后回调 hhsx4）
;   hhsx4 选好第二位：剧情（「开始双修 hhsx3 / 改日再说」）
;   hhsx3 开始双修：没有共同内功就提示；否则打开武学面板 —— 列第一位的武学，只看内功（类型 "0"），
;            筛选 28 = 剧情目标人物和主角也会（剧情目标人物临时设为第二位，没有第二位就是第一位）
;   hhsx5 选好内功：关对话，开始「双修」进度（WorkingUIController，天数 = 内功等级 / 2 + 1，同研读秘籍），
;            结束回调 hhsx6
;   hhsx6 进度结束（中途取消则什么都不给）：主角和参与的弟子各在经验条面板（同研读秘籍后的面板）加一条，
;            经验按内功等级 400/900/1500/2100/2600/3100，由面板动画按各人自己的实战经验加成加上；弟子本月次数用掉
; 经验条面板原来固定给主角加（SkillExpShowPrefab.Update 里 主角.AddSkillFightExp），改为给武学的主人加（入口 +10）。
; 可双修的女弟子：本门（主角门派 GetOwnHeros），女性，没死、没坐牢，不是主角；
;   和主角在同一区域（门派里），或在主角队伍里；本月还没双修过。
; 每月一次：记在人物 playerInteractionTimeData.playerInteractTimeList 第 40 格（原版只有 30 种互动，
;   每月 GameController.ManageMonthTask 会把所有格子重置为 1；第一次用时把列表补长到 41 格，0 = 本月已用）。
; 参数不是 "hhsxN" 时两个函数照常执行。
bits 64
default rel
org 0

%macro EXT 2
%1 equ 0x20000000 + %2 * 0x100000
%endmacro
EXT X_SMSBACK,     0   ; StudyMaxState 开头被替换的 3 个 push 之后
EXT X_SFOBACK,     1   ; StudyFightOther 开头被替换的 3 个 push 之后
EXT X_WORLD,       2   ; 取 WorldData 所在对象的静态 getter（结果 +20h = WorldData）
EXT X_PLAYER,      3   ; WorldData.Player()
EXT X_GETHERO,     4   ; WorldData.GetHero(heroID)
EXT X_GETFORCE,    5   ; HeroData.GetForce(includeServant)
EXT X_OWNHEROS,    6   ; ForceData.GetOwnHeros()（新建的 List<HeroData>）
EXT X_FINDSKILL,   7   ; HeroData.FindSkill(targetSkillID)
EXT X_DATABASE,    8   ; KungfuSkillLvData.DataBase()
EXT X_SKNAME,      9   ; KungfuSkillLvData.Name(colored)
EXT X_ADDEXP,      10  ; HeroData.AddSkillFightExp(num,targetSkill,showInfo)
EXT X_CCINST,      11  ; ChooseController.get_Instance()
EXT X_SHOWCHOOSE,  12  ; ChooseController.ShowChoosePanel(_chooseType,param,_sendResultFucTarget,_sendResultFuc,_sendResultParam,_filterType,_cancelFuc)
EXT X_PCINST,      13  ; PlotController.get_Instance()
EXT X_GETGO,       14  ; Component.get_gameObject()
EXT X_BUINST,      15  ; BuildingUIController.get_Instance()
EXT X_HIDEBU,      16  ; BuildingUIController.HideBuildingUI()
EXT X_GCINST,      17  ; GameController.get_Instance()
EXT X_TIP,         18  ; GameController.ShowTextOnMouse(text)
EXT X_PLOTCTOR,    19  ; SinglePlotData..ctor(targetPlotText,choiceDataTexts,targetHeroType,targetHeroName)
EXT X_CHANGEPLOT,  20  ; PlotController.ChangePlot(plotData,heroID)
EXT X_GETCOMP,     21  ; GameObject.GetComponent<T>()（共享代码，T 由 MethodInfo 决定）
EXT X_INITMD,      22  ; 初始化 metadata usage 槽（游戏函数开头都这样调用，已初始化的直接返回）
EXT X_OBJNEW,      23  ; il2cpp_object_new
EXT X_LISTCTOR,    24  ; List<T>..ctor()（引用类型共享代码）
EXT X_LISTADD,     25  ; List<T>.Add(item)（引用类型共享代码）
EXT X_INTADD,      26  ; List<int>.Add(item)
EXT X_STRNEW,      27  ; il2cpp_string_new
EXT X_VALUEBOX,    28  ; il2cpp_value_box
EXT X_CORLIB,      29  ; il2cpp_get_corlib
EXT X_CLASSFROM,   30  ; il2cpp_class_from_name
EXT X_WB,          31  ; il2cpp_gc_wbarrier_set_field（写字段并做写屏障）
EXT X_I2S,         32  ; Int32.ToString()（this = int*）
EXT X_CONCAT4,     33  ; String.Concat(str0,str1,str2,str3)
EXT X_CONCAT2,     34  ; String.Concat(str0,str1)
EXT X_SLOTHERO,    35  ; 游戏里 GetComponent<HeroIconController> 的 MethodInfo 槽（rip 相对读取）
EXT X_SLOTSKILL,   36  ; 游戏里 GetComponent<武学图标> 的 MethodInfo 槽
EXT X_SLOTLISTK,   37  ; List<string> 类的槽
EXT X_SLOTLISTCTOR,38  ; List<string>..ctor 的 MethodInfo 槽
EXT X_SLOTLISTADD, 39  ; List<string>.Add 的 MethodInfo 槽
EXT X_SLOTPLOTK,   40  ; SinglePlotData 类的槽
EXT X_SLOTINTADD,  41  ; List<int>.Add 的 MethodInfo 槽
EXT X_BELONG,      42  ; KungfuSkillLvData.GetBelongHero()
EXT X_HIDEINTERACT,43  ; PlotController.HideInteractUI()
EXT X_WKINST,      44  ; WorkingUIController.get_Instance()
EXT X_STARTWORK,   45  ; WorkingUIController.StartWorking(_workName,dayNum,callFuc,callFucParam,finishCallFuc,finishCallFucParam)
EXT X_SPEINST,     46  ; SpeShowController.get_Instance()
EXT X_SHOWEXP,     47  ; SpeShowController.ShowGetSkillExp(_targetSkill,_totalExp,_expType,showText)
EXT X_HERONAME,    48  ; HeroData.HeroName(useSetName)
EXT X_FIGHTRATE,   49  ; HeroData.GetFightExpRate(targetSkill)
EXT X_F2S,         50  ; Single.ToString(format)（this = float*）

%define H_ID       0x58   ; HeroData.heroID
%define H_FEMALE   0x80   ; HeroData.isFemale
%define H_DEAD     0x61   ; HeroData.dead
%define H_AREA     0xC0   ; HeroData.atAreaID
%define H_PRISON   0xD1   ; HeroData.inPrison
%define H_SKILLS   0x260  ; HeroData.kungfuSkills
%define H_INTEAM   0x2F0  ; HeroData.inTeam
%define H_LEADER   0x2F4  ; HeroData.teamLeader（队长 heroID）
%define H_PIT      0x308  ; HeroData.playerInteractionTimeData
%define PIT_LIST   0x10   ; PlayerInteractionTimeData.playerInteractTimeList（List<int>）
%define SLOT_IDX   40     ; 本功能用的格子
%define PC_TARGET  0x70   ; PlotController.targetInteractHero
%define CC_RESULT  0x48   ; ChooseController.chooseResult（GameObject）
%define ICON_DATA  0x20   ; HeroIconController.heroData / 武学图标.skill
%define SK_ID      0x10   ; KungfuSkillLvData.skillID
%define KF_TYPE    0x30   ; KungFuData.类别（0 = 内功）
%define KF_LV      0x34   ; KungFuData.等级
%define MAXN       1000   ; 选人面板最多列出的人数
%define WK_FINISHED 0x60  ; WorkingUIController.workFinished（自然结束 1，中途取消 0）

; 入口表（C# 按偏移挂钩）
entry_sms: jmp strict near sms_hook       ; +0  BuildingUIController.StudyMaxState(param)
entry_sfo: jmp strict near sfo_hook       ; +5  PlotController.StudyFightOther(param)
entry_exp: jmp strict near exp_thunk      ; +10 SkillExpShowPrefab.Update 里的 call AddSkillFightExp 改为 call 这里

; rdx = 参数字符串 → eax = "hhsxN" 的 N，不是则 -1；只改 rax、r10
marker:
    mov eax, -1
    test rdx, rdx
    jz .r
    cmp dword [rdx + 0x10], 5
    jne .r
    mov r10, 0x0078007300680068     ; "hhsx"（UTF-16）
    cmp [rdx + 0x14], r10
    jne .r
    movzx r10d, word [rdx + 0x1C]
    sub r10d, '0'
    cmp r10d, 9
    ja .r
    mov eax, r10d
.r: ret

sms_hook:
    call marker
    test eax, eax
    jz h0
    push rbx                        ; 原函数开头
    push rsi
    push r13
    jmp X_SMSBACK

sfo_hook:
    call marker
    cmp eax, 1
    je h1
    cmp eax, 2
    je h2
    cmp eax, 3
    je h3
    cmp eax, 4
    je h4
    cmp eax, 5
    je h5
    cmp eax, 6
    je h6
    push rbx                        ; 原函数开头
    push rbp
    push r15
    jmp X_SFOBACK

; ---------- 小工具（都是普通函数：进入时 rsp ≡ 8 mod 16） ----------
get_world:                          ; → rax = WorldData 或 0
    sub rsp, 0x28
    xor ecx, ecx
    call X_WORLD
    test rax, rax
    jz .z
    mov rax, [rax + 0x20]
.z: add rsp, 0x28
    ret

get_player:                         ; → rax = 主角或 0
    sub rsp, 0x28
    call get_world
    test rax, rax
    jz .z
    mov rcx, rax
    xor edx, edx
    call X_PLAYER
.z: add rsp, 0x28
    ret

get_hero:                           ; ecx = heroID（负数 = 无）→ rax
    push rbx
    sub rsp, 0x20
    mov ebx, ecx
    xor eax, eax
    test ebx, ebx
    js .z
    call get_world
    test rax, rax
    jz .z
    mov rcx, rax
    mov edx, ebx
    xor r8d, r8d
    call X_GETHERO
.z: add rsp, 0x20
    pop rbx
    ret

slot_avail:                         ; rcx = 人物 → eax 非 0 = 本月还能双修
    mov eax, 1
    mov rcx, [rcx + H_PIT]
    test rcx, rcx
    jz .r
    mov rcx, [rcx + PIT_LIST]
    test rcx, rcx
    jz .r
    cmp dword [rcx + 0x18], SLOT_IDX
    jle .r
    mov rcx, [rcx + 0x10]
    test rcx, rcx
    jz .r
    cmp dword [rcx + 0x18], SLOT_IDX
    jle .r
    mov eax, [rcx + 0x20 + SLOT_IDX * 4]
.r: ret

slot_use:                           ; rcx = 人物：本月次数记为已用
    push rbx
    sub rsp, 0x20
    mov rbx, [rcx + H_PIT]
    test rbx, rbx
    jz .r
    mov rbx, [rbx + PIT_LIST]
    test rbx, rbx
    jz .r
    lea rcx, [rel $$ + X_SLOTINTADD]
    call X_INITMD
.add:
    cmp dword [rbx + 0x18], SLOT_IDX
    jg .set
    mov rcx, rbx
    mov edx, 1
    mov r8, [rel $$ + X_SLOTINTADD]
    call X_INTADD
    jmp .add
.set:
    mov rax, [rbx + 0x10]
    mov dword [rax + 0x20 + SLOT_IDX * 4], 0
.r: add rsp, 0x20
    pop rbx
    ret

eligible:                           ; rcx = 人物, rdx = 主角, r8d = 排除的 heroID → eax
    xor eax, eax
    test rcx, rcx
    jz .r
    cmp rcx, rdx
    je .r
    cmp byte [rcx + H_FEMALE], 0
    je .r
    cmp byte [rcx + H_DEAD], 0
    jne .r
    cmp byte [rcx + H_PRISON], 0
    jne .r
    cmp [rcx + H_ID], r8d
    je .r
    mov r9d, [rdx + H_AREA]
    cmp [rcx + H_AREA], r9d
    je .ok
    cmp byte [rcx + H_INTEAM], 0
    je .r
    mov r9d, [rdx + H_ID]
    cmp [rcx + H_LEADER], r9d
    jne .r
.ok:
    jmp slot_avail
.r: ret

build_list:                         ; ecx = 排除的 heroID → eax = 人数，名单放进 FL / FA
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    sub rsp, 0x20
    mov r12d, ecx
    xor r13d, r13d
    call get_player
    test rax, rax
    jz .done
    mov rdi, rax
    mov rcx, rax
    xor edx, edx
    xor r8d, r8d
    call X_GETFORCE
    test rax, rax
    jz .done
    mov rcx, rax
    xor edx, edx
    call X_OWNHEROS
    test rax, rax
    jz .done
    mov rbx, rax
    xor esi, esi
.loop:
    cmp esi, [rbx + 0x18]
    jge .done
    cmp r13d, MAXN
    jge .done
    mov rax, [rbx + 0x10]
    mov rcx, [rax + 0x20 + rsi * 8]
    mov rdx, rdi
    mov r8d, r12d
    call eligible
    test eax, eax
    jz .next
    mov rax, [rbx + 0x10]
    mov rcx, [rax + 0x20 + rsi * 8]
    lea rax, [FA]
    mov [rax + 0x20 + r13 * 8], rcx
    inc r13d
.next:
    inc esi
    jmp .loop
.done:
    lea rax, [FL]
    lea rcx, [FA]
    mov [rax + 0x10], rcx
    mov [rax + 0x18], r13d
    mov eax, r13d
    add rsp, 0x20
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

pc_go:                              ; → rax = PlotController 的 GameObject 或 0
    sub rsp, 0x28
    xor ecx, ecx
    call X_PCINST
    test rax, rax
    jz .r
    mov rcx, rax
    xor edx, edx
    call X_GETGO
.r: add rsp, 0x28
    ret

; rcx = 参数列表（假 List：+10h 数组，+18h 个数）, edx = 面板类型, r8 = 回调标记（C 字符串）, r9d = 筛选
show_choose:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    sub rsp, 0x58
    mov rbx, rcx
    mov esi, edx
    mov rdi, r8
    mov r12d, r9d
    lea rcx, [S_FUNC]
    call X_STRNEW
    mov r13, rax
    mov rcx, rdi
    call X_STRNEW
    mov r14, rax
    call pc_go
    test rax, rax
    jz .r
    mov rdi, rax
    xor ecx, ecx
    call X_CCINST
    test rax, rax
    jz .r
    mov rcx, rax
    mov edx, esi
    mov r8, rbx
    mov r9, rdi
    mov [rsp + 0x20], r13
    mov [rsp + 0x28], r14
    mov eax, r12d
    mov [rsp + 0x30], rax
    mov qword [rsp + 0x38], 0
    mov qword [rsp + 0x40], 0
    call X_SHOWCHOOSE
.r: add rsp, 0x58
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

tip:                                ; rcx = C 字符串，在鼠标处提示
    push rbx
    sub rsp, 0x20
    call X_STRNEW
    mov rbx, rax
    xor ecx, ecx
    call X_GCINST
    test rax, rax
    jz .r
    mov rcx, rax
    mov rdx, rbx
    xor r8d, r8d
    call X_TIP
.r: add rsp, 0x20
    pop rbx
    ret

; rcx = 剧情文字（托管字符串）, rdx = 说话的人物, r8 = 连续存放的选项 C 字符串, r9d = 选项个数
plot:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 0x40
    mov rbx, rcx
    mov rsi, rdx
    mov rdi, r8
    mov r12d, r9d
    lea rcx, [rel $$ + X_SLOTLISTK]
    call X_INITMD
    lea rcx, [rel $$ + X_SLOTLISTCTOR]
    call X_INITMD
    lea rcx, [rel $$ + X_SLOTLISTADD]
    call X_INITMD
    lea rcx, [rel $$ + X_SLOTPLOTK]
    call X_INITMD
    mov rcx, [rel $$ + X_SLOTLISTK]
    call X_OBJNEW
    test rax, rax
    jz .r
    mov r13, rax
    mov rcx, rax
    mov rdx, [rel $$ + X_SLOTLISTCTOR]
    call X_LISTCTOR
.ch:
    test r12d, r12d
    jz .chd
    mov rcx, rdi
    call X_STRNEW
    mov rcx, r13
    mov rdx, rax
    mov r8, [rel $$ + X_SLOTLISTADD]
    call X_LISTADD
.sk:
    mov al, [rdi]
    inc rdi
    test al, al
    jnz .sk
    dec r12d
    jmp .ch
.chd:
    lea rcx, [rsi + H_ID]
    xor edx, edx
    call X_I2S
    mov r14, rax
    mov rcx, [rel $$ + X_SLOTPLOTK]
    call X_OBJNEW
    test rax, rax
    jz .r
    mov r15, rax
    mov rcx, rax
    mov rdx, rbx
    mov r8, r13
    mov r9d, 3                      ; targetHeroType = 3（按 heroID 显示人物）
    mov [rsp + 0x20], r14
    mov qword [rsp + 0x28], 0
    call X_PLOTCTOR
    xor ecx, ecx
    call X_PCINST
    test rax, rax
    jz .r
    mov rcx, rax
    mov rdx, r15
    xor r8d, r8d
    xor r9d, r9d
    call X_CHANGEPLOT
.r: add rsp, 0x40
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

chosen:                             ; rcx = GetComponent 的 MethodInfo 槽地址 → rax = 选中项的数据（+20h）或 0
    push rbx
    push rsi
    sub rsp, 0x28
    mov rsi, rcx
    call X_INITMD
    xor ecx, ecx
    call X_CCINST
    test rax, rax
    jz .z
    mov rcx, [rax + CC_RESULT]
    test rcx, rcx
    jz .z
    mov rdx, [rsi]
    call X_GETCOMP
    test rax, rax
    jz .z
    mov rax, [rax + ICON_DATA]
    jmp .r
.z: xor eax, eax
.r: add rsp, 0x28
    pop rsi
    pop rbx
    ret

int32_class:                        ; → rax = System.Int32 的类
    mov rax, [G_I32]
    test rax, rax
    jnz .r
    sub rsp, 0x28
    call X_CORLIB
    mov rcx, rax
    lea rdx, [S_SYSTEM]
    lea r8, [S_INT32]
    call X_CLASSFROM
    mov [G_I32], rax
    add rsp, 0x28
.r: ret

has_common:                         ; rcx = 第一位, rdx = 第二位或 0 → eax = 有没有三人（两人）都会的内功
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    sub rsp, 0x20
    mov rbx, rcx
    mov rsi, rdx
    call get_player
    test rax, rax
    jz .no
    mov rdi, [rax + H_SKILLS]
    test rdi, rdi
    jz .no
    xor r12d, r12d
.lp:
    cmp r12d, [rdi + 0x18]
    jge .no
    mov rax, [rdi + 0x10]
    mov r13, [rax + 0x20 + r12 * 8]
    inc r12d
    test r13, r13
    jz .lp
    mov rcx, r13
    xor edx, edx
    call X_DATABASE
    test rax, rax
    jz .lp
    cmp dword [rax + KF_TYPE], 0
    jne .lp
    mov rcx, rbx
    mov edx, [r13 + SK_ID]
    xor r8d, r8d
    call X_FINDSKILL
    test rax, rax
    jz .lp
    test rsi, rsi
    jz .yes
    mov rcx, rsi
    mov edx, [r13 + SK_ID]
    xor r8d, r8d
    call X_FINDSKILL
    test rax, rax
    jz .lp
.yes:
    mov eax, 1
    jmp .r
.no:
    xor eax, eax
.r: add rsp, 0x20
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

; ---------- 各步骤（由挂钩跳入，相当于原函数本身：结束时 ret 回游戏） ----------
h0:                                 ; 建筑按钮
    sub rsp, 0x28
    mov dword [G_D1], -1
    mov dword [G_D2], -1
    mov ecx, -1
    call build_list
    test eax, eax
    jz .none
    lea rcx, [FL]
    mov edx, 2                      ; ChooseType.Hero
    lea r8, [M1]
    xor r9d, r9d
    call show_choose
    jmp .r
.none:
    lea rcx, [T_NONE]
    call tip
.r: add rsp, 0x28
    ret

h1:                                 ; 选好第一位
    push rbx
    sub rsp, 0x20
    lea rcx, [rel $$ + X_SLOTHERO]
    call chosen
    test rax, rax
    jz .r
    mov rbx, rax
    mov eax, [rbx + H_ID]
    mov [G_D1], eax
    mov dword [G_D2], -1
    xor ecx, ecx
    call X_BUINST
    test rax, rax
    jz .nb
    mov rcx, rax
    xor edx, edx
    call X_HIDEBU
.nb:
    mov ecx, [G_D1]
    call build_list
    test eax, eax
    jz .one
    lea rcx, [P1]
    call X_STRNEW
    mov rcx, rax
    mov rdx, rbx
    lea r8, [C_MORE]
    mov r9d, 3
    call plot
    jmp .r
.one:
    lea rcx, [P1B]
    call X_STRNEW
    mov rcx, rax
    mov rdx, rbx
    lea r8, [C_START]
    mov r9d, 2
    call plot
.r: add rsp, 0x20
    pop rbx
    ret

h2:                                 ; 再唤一位
    sub rsp, 0x28
    mov ecx, [G_D1]
    test ecx, ecx
    js .r
    call build_list
    test eax, eax
    jz .none
    lea rcx, [FL]
    mov edx, 2
    lea r8, [M4]
    xor r9d, r9d
    call show_choose
    jmp .r
.none:
    lea rcx, [T_NONE]
    call tip
.r: add rsp, 0x28
    ret

h4:                                 ; 选好第二位
    push rbx
    sub rsp, 0x20
    cmp dword [G_D1], 0
    jl .r
    lea rcx, [rel $$ + X_SLOTHERO]
    call chosen
    test rax, rax
    jz .r
    mov rbx, rax
    mov eax, [rbx + H_ID]
    cmp eax, [G_D1]
    je .r
    mov [G_D2], eax
    lea rcx, [P2]
    call X_STRNEW
    mov rcx, rax
    mov rdx, rbx
    lea r8, [C_START]
    mov r9d, 2
    call plot
.r: add rsp, 0x20
    pop rbx
    ret

h3:                                 ; 开始双修：选内功
    push rbx
    push rsi
    push rdi
    push r12
    sub rsp, 0x48
    mov ecx, [G_D1]
    call get_hero
    test rax, rax
    jz .r
    mov rbx, rax
    xor esi, esi
    mov ecx, [G_D2]
    test ecx, ecx
    js .nod2
    call get_hero
    mov rsi, rax
.nod2:
    mov rcx, rbx
    mov rdx, rsi
    call has_common
    test eax, eax
    jnz .go
    lea rcx, [P3]
    call X_STRNEW
    mov rcx, rax
    mov rdx, rbx
    lea r8, [C_GIVEUP]
    mov r9d, 1
    call plot
    jmp .r
.go:
    xor ecx, ecx
    call X_PCINST
    test rax, rax
    jz .r
    mov rdi, rax
    mov r8, rsi                     ; 目标：第二位，没有就第一位
    test r8, r8
    jnz .t
    mov r8, rbx
.t:
    mov rax, [rdi + PC_TARGET]
    cmp rax, r8                     ; 取消武学面板后再点「开始双修」时不覆盖原值
    je .t2
    mov [G_OLD], rax
.t2:
    mov rcx, rdi
    lea rdx, [rdi + PC_TARGET]
    call X_WB
    call int32_class
    test rax, rax
    jz .r
    mov rcx, rax
    lea rdx, [G_D1]
    call X_VALUEBOX
    test rax, rax
    jz .r
    mov r12, rax
    mov [rsp + 0x30], rax
    lea rcx, [S_TYPE0]
    call X_STRNEW
    mov [rsp + 0x38], rax
    lea rcx, [FA2]
    mov [rcx + 0x20], r12
    mov [rcx + 0x28], rax
    lea rdx, [FL2]
    mov [rdx + 0x10], rcx
    mov dword [rdx + 0x18], 2
    mov rcx, rdx
    xor edx, edx                    ; ChooseType.HeroSkill
    lea r8, [M5]
    mov r9d, 28                     ; 筛选：剧情目标人物和主角也会
    call show_choose
.r: add rsp, 0x48
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

h5:                                 ; 选好内功：记下武学，关对话，开始「双修」进度（按等级过几天）
    push rbx
    sub rsp, 0x40
    lea rcx, [rel $$ + X_SLOTSKILL]
    call chosen
    test rax, rax
    jz .r
    mov rbx, rax
    mov eax, [rbx + SK_ID]
    mov [G_SID], eax
    mov rcx, rbx
    xor edx, edx
    call X_DATABASE
    test rax, rax
    jz .r
    mov eax, [rax + KF_LV]
    test eax, eax
    jns .p
    xor eax, eax
.p: cmp eax, 5
    jle .q
    mov eax, 5
.q: mov [G_LV], eax
    xor ecx, ecx
    call X_PCINST                   ; 剧情目标人物改回原来的，关对话
    test rax, rax
    jz .r
    mov rbx, rax
    mov rcx, rax
    lea rdx, [rax + PC_TARGET]
    mov r8, [G_OLD]
    call X_WB
    mov rcx, rbx
    xor edx, edx
    call X_HIDEINTERACT
    xor ecx, ecx
    call X_WKINST
    test rax, rax
    jz .r
    mov rbx, rax
    lea rcx, [S_WORK]
    call X_STRNEW
    mov [rsp + 0x38], rax
    lea rcx, [S_FUNC]
    call X_STRNEW
    mov [rsp + 0x30], rax
    lea rcx, [M6]
    call X_STRNEW
    mov rcx, rbx
    mov rdx, [rsp + 0x38]           ; 名字「双修」
    mov r8d, [G_LV]                 ; 天数 = 等级 / 2 + 1（同研读秘籍 BookData.ReadDayCost）
    shr r8d, 1
    inc r8d
    xor r9d, r9d                    ; 每天回调：无
    mov qword [rsp + 0x20], 0
    mov r10, [rsp + 0x30]
    mov [rsp + 0x28], r10           ; 结束回调 StudyFightOther("hhsx6")
    mov [rsp + 0x30], rax
    mov qword [rsp + 0x38], 0
    call X_STARTWORK
.r: add rsp, 0x40
    pop rbx
    ret

h6:                                 ; 进度结束：加经验（经验条面板），弟子本月次数用掉
    push rbx
    push rsi
    push rdi
    push r12
    sub rsp, 0x28
    xor ecx, ecx
    call X_WKINST
    test rax, rax
    jz .end
    cmp byte [rax + WK_FINISHED], 0 ; 中途取消：什么都不给
    je .end
    cmp dword [G_D1], 0
    jl .end
    mov ecx, [G_D1]
    call get_hero
    mov rsi, rax
    xor edi, edi
    mov ecx, [G_D2]
    test ecx, ecx
    js .n2
    call get_hero
    mov rdi, rax
.n2:
    call get_player
    test rax, rax
    jz .end
    mov rcx, rax
    call show_exp                   ; 主角
    test rsi, rsi
    jz .d2
    mov rcx, rsi
    call show_exp
    mov rcx, rsi
    call slot_use
.d2:
    test rdi, rdi
    jz .end
    mov rcx, rdi
    call show_exp
    mov rcx, rdi
    call slot_use
.end:
    mov dword [G_D1], -1
    mov dword [G_D2], -1
    add rsp, 0x28
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

; rcx = 人物：找到该内功，在经验条面板里加一条（标题「名字：武学增加实战经验 N (+P%)」，经验由面板动画按此人的加成加上）
show_exp:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    sub rsp, 0x40
    mov rbx, rcx
    mov edx, [G_SID]
    xor r8d, r8d
    call X_FINDSKILL
    test rax, rax
    jz .r
    mov rsi, rax                    ; 此人的这门内功
    mov eax, [G_LV]
    lea rcx, [EXPTBL]
    mov eax, [rcx + rax * 4]
    mov [rsp + 0x30], eax           ; 经验（整数）
    mov rcx, rbx
    xor edx, edx
    xor r8d, r8d
    call X_HERONAME
    mov rdi, rax
    lea rcx, [S_COLON]
    call X_STRNEW
    mov r12, rax
    mov rcx, rsi
    xor edx, edx
    xor r8d, r8d
    call X_SKNAME
    mov r13, rax
    lea rcx, [S_ADD]
    call X_STRNEW
    mov rcx, rdi
    mov rdx, r12
    mov r8, r13
    mov r9, rax
    mov qword [rsp + 0x20], 0
    call X_CONCAT4
    mov rdi, rax                    ; 名字：武学增加实战经验␣
    lea rcx, [rsp + 0x30]
    xor edx, edx
    call X_I2S
    mov r12, rax                    ; N
    mov rcx, rbx
    mov rdx, rsi
    xor r8d, r8d
    call X_FIGHTRATE                ; 此人的实战经验倍率
    subss xmm0, [F_ONE]
    mulss xmm0, [F_100]
    movss [rsp + 0x34], xmm0
    lea rcx, [S_PCTFMT]
    call X_STRNEW
    lea rcx, [rsp + 0x34]
    mov rdx, rax
    xor r8d, r8d
    call X_F2S
    mov r13, rax                    ; +P
    lea rcx, [S_LP]
    call X_STRNEW
    mov rcx, rdi
    mov rdx, r12
    mov r8, rax
    mov r9, r13
    mov qword [rsp + 0x20], 0
    call X_CONCAT4
    mov rdi, rax
    lea rcx, [S_RP]
    call X_STRNEW
    mov rcx, rdi
    mov rdx, rax
    xor r8d, r8d
    call X_CONCAT2
    mov rdi, rax                    ; 标题
    xor ecx, ecx
    call X_SPEINST
    test rax, rax
    jz .r
    mov rcx, rax
    mov rdx, rsi
    cvtsi2ss xmm2, dword [rsp + 0x30]
    mov r9d, 1                      ; 实战经验
    mov [rsp + 0x20], rdi
    mov qword [rsp + 0x28], 0
    call X_SHOWEXP
.r: add rsp, 0x40
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

; 经验条面板（SkillExpShowPrefab.Update）里原来是 主角.AddSkillFightExp(…)：改为由武学的主人加，按主人的加成
exp_thunk:                          ; rcx = 主角, xmm1 = 经验, r8 = 武学, r9 = showInfo, [rsp+28h] = MethodInfo
    push rcx
    sub rsp, 0x40
    mov [rsp + 0x20], r8
    mov [rsp + 0x28], r9
    movss [rsp + 0x30], xmm1
    mov rcx, r8
    xor edx, edx
    call X_BELONG
    mov rcx, rax
    test rcx, rcx
    jnz .h
    mov rcx, [rsp + 0x40]
.h: mov r8, [rsp + 0x20]
    mov r9, [rsp + 0x28]
    movss xmm1, [rsp + 0x30]
    add rsp, 0x48
    jmp X_ADDEXP

    times 8 db 0xCC                 ; 代码结束标记（gen.py 用）

; ---------- 常量 ----------
EXPTBL:   dd 400, 900, 1500, 2100, 2600, 3100
S_FUNC:   db "StudyFightOther", 0
M1:       db "hhsx1", 0
M4:       db "hhsx4", 0
M5:       db "hhsx5", 0
S_TYPE0:  db "0", 0
S_SYSTEM: db "System", 0
S_INT32:  db "Int32", 0
T_NONE:   db "没有可以双修的女弟子（须是本门弟子，在队伍中或在门派里，且本月尚未双修）", 0
P1:       db "能与#PlayerName#双修，是弟子的福分。", 10, "#PlayerName#可要再唤一位姐妹同来？", 0
P1B:      db "能与#PlayerName#双修，是弟子的福分。", 0
P2:       db "既是#PlayerName#相召，弟子自当奉陪。", 0
P3:       db "我们并无共同修习的内功，今日只怕无从双修了。", 0
M6:       db "hhsx6", 0
S_WORK:   db "双修", 0
S_COLON:  db "：", 0
S_ADD:    db "增加实战经验 ", 0
S_LP:     db " (", 0
S_RP:     db "%)", 0
S_PCTFMT: db "+0;-0;0", 0
align 4
F_ONE:    dd 1.0
F_100:    dd 100.0
; 选项（plot 按顺序连续读取）
C_MORE:   db "再唤一位;StudyFightOther;hhsx2", 0
          db "就我们二人;StudyFightOther;hhsx3", 0
          db "改日再说;HideInteractUI", 0
C_START:  db "开始双修;StudyFightOther;hhsx3", 0
          db "改日再说;HideInteractUI", 0
C_GIVEUP: db "只好作罢;HideInteractUI", 0

; ---------- 变量（节是可读写的） ----------
align 8
G_OLD:    dq 0                      ; 改剧情目标人物前的值
G_I32:    dq 0                      ; System.Int32 类
G_D1:     dd -1                     ; 第一位 heroID
G_D2:     dd -1                     ; 第二位 heroID（-1 = 无）
G_SID:    dd 0                      ; 选中的内功 skillID
G_LV:     dd 0                      ; 它的武学等级（0~5）
FL:       times 0x20 db 0           ; 假 List：+10h 数组，+18h 个数（ShowChoosePanel 只读这两项，并立即复制）
FL2:      times 0x20 db 0
FA2:      times 0x30 db 0           ; 假数组：+20h 起是元素
FA:       times 0x20 + MAXN * 8 db 0
