// 合欢殿「双修」：选本门女弟子（最多两位，在队伍里或在门派里，每人每月一次），选一门大家都会的内功，
//   过几天后主角和弟子各得该内功的实战经验（按内功等级 400/900/1500/2100/2600/3100，再乘各人的实战经验加成）。
// 做法：GameAssembly.dll 末尾新增可读写执行的代码节 .hhsx，放入 ShuangXiuCave 的机器码；挂两处：
//   BuildingUIController.StudyMaxState(param) 开头（合欢殿互动选项「双修」传 "hhsx0"），
//   PlotController.StudyFightOther(param) 开头（选人 / 选武学面板的回调和剧情选项传 "hhsx1"~"hhsx5"）；
//   参数不是这些标记时两个函数照常执行。合欢殿的互动选项在建筑表里（安装合欢殿时一起写入）。
//   选好内功后用 WorkingUIController 过几天（天数同研读秘籍：等级 / 2 + 1），结束后在经验条面板（同研读秘籍后）给每人加一条；
//   经验条面板原来固定给主角加经验，SkillExpShowPrefab.Update 里那个 call 改为调用新代码 +10：由武学的主人加；
//   几条经验条由新代码上下排开（面板本身不排列）。
//   武学说明的「修炼需求」原来只算武学主人，GetSkillDescribe 里那个 call 改为调用新代码 +15：
//   双修选武学面板打开时，第一位的武学列出主角、第一位、第二位各自的需求（含当前等级）。
//   经验条名字：SkillExpShowPrefab.Init 里取武学名的 call 改为调用新代码 +20：双修的几条名字处显示人物名，并复制 Name 文字挂在框右边显示「加成±P%」。
using System;
using System.Collections.Generic;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        public const string SxSectionName = ".hhsx";
        public const string SxButton = "双修?与门中女弟子同修内功，按内功等级增长实战经验-我-StudyMaxState-hhsx0";

        class SxTargets
        {
            public long SmsOff, SfoOff;
            public ulong SmsVa, SfoVa, ExpCallVa, NeedsCallVa, NameCallVa;   // NeedsCallVa：武学说明里「修炼需求」的 call GetSkillNeedsDescribe   // ExpCallVa：经验条面板里 call AddSkillFightExp 的位置
            public Dictionary<string, ulong> Ext = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        }

        static int[] Pat(string s)
        {
            var p = s.Split(' ');
            var r = new int[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = p[i] == "??" ? -1 : Convert.ToInt32(p[i], 16);
            return r;
        }

        static int UniquePat(byte[] d, string pat, long start, int len, Func<string, Exception> bad, string what)
        {
            var p = Pat(pat);
            int a = Bin.IndexOfPattern(d, p, (int)start, (int)start + len);
            if (a < 0 || Bin.IndexOfPattern(d, p, a + 1, (int)start + len) >= 0) throw bad(what);
            return a;
        }

        SxTargets LocateSx(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var cs = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var core = new Il2CppCode(pe, "UnityEngine.CoreModule.dll");
            var corlib = new Il2CppCode(pe, "mscorlib.dll");
            var fo = new Il2CppFieldOffsets(pe, m);
            var t = new SxTargets();
            Func<string, Exception> bad = what => new ModException("合欢殿双修：" + what + " 与预期不同（游戏代码可能已改）");
            Func<string, string, string[], ulong> va = (ty, me, ps) => cs.MethodVa(m.FindMethod(ty, me, ps).Token);
            Func<string, string, string[], ulong> vaCore = (ty, me, ps) => core.MethodVa(m.FindMethod("UnityEngine", ty, me, ps).Token);
            Func<string, string, string[], ulong> vaSys = (ty, me, ps) => corlib.MethodVa(m.FindMethod("System", ty, me, ps).Token);
            var none = new string[0];
            Action<string, string, int> fld = (ty, f, want) => { if (fo.Offset(null, ty, f) != want) throw bad(ty + "." + f + "(+" + want.ToString("X") + "h)"); };

            // 两个挂钩点：开头 3 个 push 正好 5 字节
            ulong sms = va("BuildingUIController", "StudyMaxState", new[] { "param" });
            ulong sfo = va("PlotController", "StudyFightOther", new[] { "param" });
            t.SmsVa = sms; t.SmsOff = pe.VaToOff(sms);
            t.SfoVa = sfo; t.SfoOff = pe.VaToOff(sfo);
            if (Bin.IndexOf(dll, new byte[] { 0x40, 0x53, 0x56, 0x41, 0x55 }, (int)t.SmsOff, (int)t.SmsOff + 5) != t.SmsOff) throw bad("StudyMaxState 开头");
            if (Bin.IndexOf(dll, new byte[] { 0x40, 0x53, 0x55, 0x41, 0x57 }, (int)t.SfoOff, (int)t.SfoOff + 5) != t.SfoOff) throw bad("StudyFightOther 开头");
            t.Ext["SmsBack"] = sms + 5;
            t.Ext["SfoBack"] = sfo + 5;

            t.Ext["Player"] = va("WorldData", "Player", none);
            t.Ext["World"] = FindWorldGetter(dll, pe, cs, m, t.Ext["Player"]);
            t.Ext["GetHero"] = va("WorldData", "GetHero", new[] { "heroID" });
            t.Ext["GetForce"] = va("HeroData", "GetForce", new[] { "includeServant" });
            t.Ext["OwnHeros"] = va("ForceData", "GetOwnHeros", none);
            t.Ext["FindSkill"] = va("HeroData", "FindSkill", new[] { "targetSkillID" });
            t.Ext["Database"] = va("KungfuSkillLvData", "DataBase", none);
            t.Ext["SkName"] = va("KungfuSkillLvData", "Name", new[] { "colored" });
            t.Ext["AddExp"] = va("HeroData", "AddSkillFightExp", new[] { "num", "targetSkill", "showInfo" });
            t.Ext["CcInst"] = va("ChooseController", "get_Instance", none);
            t.Ext["ShowChoose"] = va("ChooseController", "ShowChoosePanel", new[] { "_chooseType", "param", "_sendResultFucTarget", "_sendResultFuc", "_sendResultParam", "_filterType", "_cancelFuc" });
            t.Ext["PcInst"] = va("PlotController", "get_Instance", none);
            t.Ext["GetGo"] = vaCore("Component", "get_gameObject", none);
            t.Ext["BuInst"] = va("BuildingUIController", "get_Instance", none);
            t.Ext["HideBu"] = va("BuildingUIController", "HideBuildingUI", none);
            t.Ext["GcInst"] = va("GameController", "get_Instance", none);
            t.Ext["Tip"] = va("GameController", "ShowTextOnMouse", new[] { "text" });
            t.Ext["PlotCtor"] = va("SinglePlotData", ".ctor", new[] { "targetPlotText", "choiceDataTexts", "targetHeroType", "targetHeroName" });
            t.Ext["ChangePlot"] = va("PlotController", "ChangePlot", new[] { "plotData", "heroID" });
            t.Ext["I2S"] = vaSys("Int32", "ToString", none);
            t.Ext["Concat4"] = vaSys("String", "Concat", new[] { "str0", "str1", "str2", "str3" });
            t.Ext["Concat2"] = vaSys("String", "Concat", new[] { "str0", "str1" });
            t.Ext["StrNew"] = pe.Export("il2cpp_string_new");
            t.Ext["ValueBox"] = pe.Export("il2cpp_value_box");
            t.Ext["Corlib"] = pe.Export("il2cpp_get_corlib");
            t.Ext["ClassFrom"] = pe.Export("il2cpp_class_from_name");
            t.Ext["Wb"] = pe.Export("il2cpp_gc_wbarrier_set_field");
            t.Ext["ObjNew"] = pe.Export("il2cpp_object_new");
            t.Ext["Belong"] = va("KungfuSkillLvData", "GetBelongHero", none);
            t.Ext["HideInteract"] = va("PlotController", "HideInteractUI", none);
            t.Ext["WkInst"] = va("WorkingUIController", "get_Instance", none);
            t.Ext["StartWork"] = va("WorkingUIController", "StartWorking", new[] { "_workName", "dayNum", "callFuc", "callFucParam", "finishCallFuc", "finishCallFucParam" });
            t.Ext["SpeInst"] = va("SpeShowController", "get_Instance", none);
            t.Ext["ShowExp"] = va("SpeShowController", "ShowGetSkillExp", new[] { "_targetSkill", "_totalExp", "_expType", "showText" });
            t.Ext["HeroName"] = va("HeroData", "HeroName", new[] { "useSetName" });
            t.Ext["FightRate"] = va("HeroData", "GetFightExpRate", new[] { "targetSkill" });
            t.Ext["F2S"] = vaSys("Single", "ToString", new[] { "format" });
            t.Ext["GetTrans"] = vaCore("GameObject", "get_transform", none);
            t.Ext["GetRect"] = vaCore("RectTransform", "get_rect", none);
            t.Ext["GetLPos"] = vaCore("Transform", "get_localPosition", none);
            t.Ext["SetLPos"] = vaCore("Transform", "set_localPosition", new[] { "value" });
            t.Ext["Active"] = vaCore("GameObject", "get_activeSelf", none);
            t.Ext["Needs"] = va("AttriNumData", "GetSkillNeedsDescribe", new[] { "targetHero" });
            // 剧情选项里用到的函数
            va("PlotController", "HideInteractUI", none);
            va("PlotController", "HideInteractUIOneDay", none);

            // 「切磋」选人回调 StudyFightOtherTargetChoose：取 List<string>、GetComponent<HeroIconController>、SinglePlotData 的用法
            ulong tcVa = va("PlotController", "StudyFightOtherTargetChoose", none);
            long tc = pe.VaToOff(tcVa);
            // 函数开头：lea rcx,[rip+槽] ; call 初始化
            if (dll[tc + 0x12] != 0x48 || dll[tc + 0x13] != 0x8D || dll[tc + 0x14] != 0x0D || dll[tc + 0x19] != 0xE8) throw bad("切磋回调开头的初始化");
            t.Ext["InitMd"] = CallTarget(pe, dll, (int)tc + 0x19);
            // mov rcx,[rip+List<string>类] ; call 新建 ; mov rdx,[rip+构造] ; mov rcx,rax ; mov rsi,rax ; call 构造
            int a = UniquePat(dll, "48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 15 ?? ?? ?? ?? 48 8B C8 48 8B F0 E8", tc, 0x300, bad, "切磋回调里新建选项列表");
            t.Ext["SlotListK"] = RipTarget(pe, dll, a, 7);
            ulong objNewCg = CallTarget(pe, dll, a + 7);
            t.Ext["SlotListCtor"] = RipTarget(pe, dll, a + 12, 7);
            t.Ext["ListCtor"] = CallTarget(pe, dll, a + 25);
            // mov r8,[rip+Add] ; mov rcx,rsi ; mov rdx,[rip+字符串] ; call Add
            int b = UniquePat(dll, "4C 8B 05 ?? ?? ?? ?? 48 8B CE 48 8B 15 ?? ?? ?? ?? E8", tc, 0x300, bad, "切磋回调里添加选项");
            t.Ext["SlotListAdd"] = RipTarget(pe, dll, b, 7);
            t.Ext["ListAdd"] = CallTarget(pe, dll, b + 17);
            // mov rcx,[rax+48h](chooseResult) ; test ; je ; mov rdx,[rip+槽] ; call GetComponent ; test ; je short ; mov rcx,[rax+20h](heroData)
            int c = UniquePat(dll, "48 8B 48 48 48 85 C9 0F 84 ?? ?? ?? ?? 48 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 48 20", tc, 0x300, bad, "切磋回调里取选中人物");
            t.Ext["SlotHero"] = RipTarget(pe, dll, c + 13, 7);
            t.Ext["GetComp"] = CallTarget(pe, dll, c + 20);
            // 之后：mov rcx,[rip+SinglePlotData类] ; (mov rbx,rax ;) call 新建 …… call SinglePlotData..ctor
            ulong plotK = 0;
            for (int q = c; q < tc + 0x300 && plotK == 0; q++)
                if (dll[q] == 0x48 && dll[q + 1] == 0x8B && dll[q + 2] == 0x0D)
                    foreach (int k in new[] { 7, 10 })
                        if (dll[q + k] == 0xE8 && CallTarget(pe, dll, q + k) == objNewCg)
                        {
                            bool ctor = false;
                            for (int z = q + k + 5; z < q + k + 0x50; z++)
                                if (dll[z] == 0xE8 && CallTarget(pe, dll, z) == t.Ext["PlotCtor"]) { ctor = true; break; }
                            if (ctor) { plotK = RipTarget(pe, dll, q, 7); break; }
                        }
            if (plotK == 0) throw bad("切磋回调里新建剧情");
            t.Ext["SlotPlotK"] = plotK;

            // 情侣「一同修炼」选武学回调：GetComponent<武学图标> → +20h 交给 SetPlotSkill
            long lc = pe.VaToOff(va("PlotController", "LoverStudyTogetherChoosen", none));
            int d = UniquePat(dll, "48 8B 48 48 48 85 C9 0F 84 ?? ?? ?? ?? 48 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 0F 84 ?? ?? ?? ?? 48 8B 50 20", lc, 0x300, bad, "一同修炼回调里取选中武学");
            if (CallTarget(pe, dll, d + 20) != t.Ext["GetComp"]) throw bad("GetComponent 调用");
            ulong setSkill = va("PlotController", "SetPlotSkill", new[] { "targetSkill", "show" });
            bool toSet = false;
            for (int z = d + 39; z < d + 0x50; z++) if (dll[z] == 0xE8 && CallTarget(pe, dll, z) == setSkill) { toSet = true; break; }
            if (!toSet) throw bad("一同修炼回调里的 SetPlotSkill");
            t.Ext["SlotSkill"] = RipTarget(pe, dll, d + 13, 7);

            // List<int>.Add：PlayerInteractionTimeData.CheckGameUpdate 里 mov r8,[rip+槽] ; mov edx,1 ; call Add
            long cg = pe.VaToOff(va("PlayerInteractionTimeData", "CheckGameUpdate", none));
            int e = UniquePat(dll, "4C 8B 05 ?? ?? ?? ?? BA 01 00 00 00 E8", cg, 0x200, bad, "互动次数列表的 Add");
            t.Ext["SlotIntAdd"] = RipTarget(pe, dll, e, 7);
            t.Ext["IntAdd"] = CallTarget(pe, dll, e + 12);

            // 经验条面板 SkillExpShowPrefab.Update：实战经验分支
            //   mov r8,[rbx+18h](targetSkill) ; xor r9d,r9d ; movaps xmm1,xmm6 ; mov qword [rsp+20h],0 ; mov rcx,rax(主角) ; call AddSkillFightExp
            long up = pe.VaToOff(va("SkillExpShowPrefab", "Update", none));
            //   （理论经验分支代码相同、调用 AddSkillBookExp，按调用目标区分）
            int g = -1, hits = 0;
            var gp = Pat("4C 8B 43 18 45 33 C9 0F 28 CE 48 C7 44 24 20 00 00 00 00 48 8B C8 E8");
            for (int q = Bin.IndexOfPattern(dll, gp, (int)up, (int)up + 0x300); q >= 0; q = Bin.IndexOfPattern(dll, gp, q + 1, (int)up + 0x300))
                if (CallTarget(pe, dll, q + 22) == t.Ext["AddExp"]) { g = q; hits++; }
            if (hits != 1) throw bad("经验条面板里加实战经验");
            t.ExpCallVa = pe.OffToVa(g + 22);
            // 进度结束回调发给的对象 = PlotController（StopWorking 里取对象的 getter 与 PlotController.get_Instance 读同一个类槽）
            Func<ulong, ulong> getterSlot = gv =>
            {
                long o = pe.VaToOff(gv);
                int i = Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x05 }, (int)o, (int)o + 0x40);
                return i < 0 ? 0 : RipTarget(pe, dll, i, 7);
            };
            long sw = pe.VaToOff(va("WorkingUIController", "StopWorking", new[] { "finished" }));
            int gc = UniquePat(dll, "33 C9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 53 38", sw, 0x100, bad, "进度结束回调");
            ulong pcSlot = getterSlot(t.Ext["PcInst"]);
            if (pcSlot == 0 || getterSlot(CallTarget(pe, dll, gc + 2)) != pcSlot) throw bad("进度结束回调的对象");
            fld("WorkingUIController", "finishFuc", 0x38); fld("WorkingUIController", "workFinished", 0x60);
            fld("SkillExpShowPrefab", "targetSkill", 0x18);
            fld("SpeShowController", "newObj", 0xA8);
            fld("ChooseController", "choosePanel", 0x18); fld("ChooseController", "sendResultParam", 0x60);
            // 武学说明「修炼需求」：call GetSkillNeedsDescribe(r12 = 武学主人) ; comiss xmm8,xmm6 ; mov r14,[rip+"\n\n修炼需求{1}\n{0}"]
            long gd = pe.VaToOff(va("KungfuSkillLvData", "GetSkillDescribe", new[] { "fullDetail", "showDamage", "bookDescribe" }));
            int nh = -1, nhits = 0;
            var np = Pat("E8 ?? ?? ?? ?? 44 0F 2F C6 4C 8B 35");
            for (int q = Bin.IndexOfPattern(dll, np, (int)gd, (int)gd + 0x2000); q >= 0; q = Bin.IndexOfPattern(dll, np, q + 1, (int)gd + 0x2000))
                if (CallTarget(pe, dll, q) == t.Ext["Needs"]) { nh = q; nhits++; }
            if (nhits != 1) throw bad("武学说明里的修炼需求");
            t.NeedsCallVa = pe.OffToVa(nh);
            // 新代码从 rbx 取被说明的武学：调用前应有 mov rcx,rbx ; call DataBase
            bool rbxSkill = false;
            for (int z = nh - 0x30; z < nh; z++)
                if (dll[z] == 0x48 && dll[z + 1] == 0x8B && dll[z + 2] == 0xCB && dll[z + 3] == 0xE8 && CallTarget(pe, dll, z + 3) == t.Ext["Database"]) rbxSkill = true;
            if (!rbxSkill) throw bad("武学说明里武学在 rbx");
            fld("KungfuSkillLvData", "lv", 0x14);
            // 经验条初始化 SkillExpShowPrefab.Init：mov rcx,[rbx+18h](targetSkill) … call KungfuSkillLvData.Name（唯一一处）
            long ini = pe.VaToOff(va("SkillExpShowPrefab", "Init", none));
            int nmAt = -1, nmHits = 0;
            for (int q = (int)ini; q < ini + 0x500; q++)
                if (dll[q] == 0xE8 && CallTarget(pe, dll, q) == t.Ext["SkName"]) { nmAt = q; nmHits++; }
            if (nmHits != 1 || Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x4B, 0x18 }, nmAt - 0x18, nmAt) < 0) throw bad("经验条初始化里的武学名");
            t.NameCallVa = pe.OffToVa(nmAt);
            // 同处取 Name 文字组件：mov rdx,[rip+槽] ; mov rcx,rax ; call Component.GetComponent<T> ; …… 之后 SetText(该组件, 名字)
            int tc2 = -1, tcHits = 0;
            var tp = Pat("48 8B 15 ?? ?? ?? ?? 48 8B C8 E8");
            for (int q = Bin.IndexOfPattern(dll, tp, nmAt - 0x30, nmAt); q >= 0; q = Bin.IndexOfPattern(dll, tp, q + 1, nmAt)) { tc2 = q; tcHits++; }
            if (tcHits != 1) throw bad("经验条初始化里的文字组件");
            t.Ext["SlotText"] = RipTarget(pe, dll, tc2, 7);
            t.Ext["CGetComp"] = CallTarget(pe, dll, tc2 + 10);
            t.Ext["SetText"] = va("LTLocalization", "SetText", new[] { "targetText", "targetValue" });
            bool setOk = false;
            for (int z = nmAt + 5; z < nmAt + 0x40; z++) if (dll[z] == 0xE8 && CallTarget(pe, dll, z) == t.Ext["SetText"]) { setOk = true; break; }
            if (!setOk) throw bad("经验条初始化里写名字");
            t.Ext["Find"] = vaCore("Transform", "Find", new[] { "n" });
            t.Ext["CGetGo"] = vaCore("Component", "get_gameObject", none);
            t.Ext["Inst"] = vaCore("Object", "Instantiate", new[] { "original", "parent", "instantiateInWorldSpace" });
            // 研读天数 = floor(武学等级 × 0.5) + 1：movd xmm0,[rax+34h] ; cvtdq2ps ; mulss ; FloorToInt ; inc eax
            if (Require0(dll, pe.VaToOff(va("BookData", "ReadDayCost", none)), 0x100, new byte[] { 0x66, 0x0F, 0x6E, 0x40, 0x34, 0x33, 0xD2, 0x0F, 0x5B, 0xC0 }) < 0) throw bad("研读天数");

            // 汇编里写死的字段偏移和枚举值
            fld("HeroData", "heroID", 0x58); fld("HeroData", "isFemale", 0x80); fld("HeroData", "dead", 0x61);
            fld("HeroData", "atAreaID", 0xC0); fld("HeroData", "inPrison", 0xD1); fld("HeroData", "kungfuSkills", 0x260);
            fld("HeroData", "inTeam", 0x2F0); fld("HeroData", "teamLeader", 0x2F4); fld("HeroData", "playerInteractionTimeData", 0x308);
            fld("PlayerInteractionTimeData", "playerInteractTimeList", 0x10);
            fld("PlotController", "targetInteractHero", 0x70);
            fld("ChooseController", "chooseResult", 0x48);
            fld("HeroIconController", "heroData", 0x20);
            fld("KungfuSkillLvData", "skillID", 0x10);
            fld("KungfuSkillData", "type", 0x30); fld("KungfuSkillData", "rareLv", 0x34);
            Action<string, string, int> enm = (ty, f, want) => { int ti = m.FindType(ty); if (ti < 0 || m.FieldIndex(ti, f) != want + 1) throw bad(ty + "." + f + " = " + want); };
            enm("ChooseType", "HeroSkill", 0); enm("ChooseType", "Hero", 2);
            enm("FightSkillType", "Internal", 0);
            int pit = m.FindType("PlayerInteractionTimeType");
            if (pit < 0 || m.FieldIndex(pit, "value__") != 0 || m.FieldIndex(pit, "EatFoodTogetherTime") < 0) throw bad("互动次数种类");
            if (CountFields(m, pit) - 1 >= 40) throw bad("互动次数种类数（本功能用第 40 格）");
            return t;
        }

        static int Require0(byte[] d, long start, int len, byte[] pat) { return Bin.IndexOf(d, pat, (int)start, (int)start + len); }

        static int CountFields(Il2CppMeta m, int typeIndex)
        {
            int n = 0;
            while (n < 1000 && FieldName(m, typeIndex, n) != null) n++;
            return n;
        }

        static string FieldName(Il2CppMeta m, int typeIndex, int k)
        {
            int b = Bin.I32(m.D, 160) + typeIndex * 88;
            if (k >= Bin.U16(m.D, b + 68)) return null;
            return m.Str(Bin.I32(m.D, Bin.I32(m.D, 96) + (Bin.I32(m.D, b + 32) + k) * 12));
        }

        bool SxPatched(byte[] dll) { return new PeImage(dll).Sections.Exists(s => s.Name == SxSectionName); }

        byte[] PatchSx(byte[] dll, byte[] meta)
        {
            if (SxPatched(dll)) throw new ModException("DLL 里已经有合欢殿双修");
            var t = LocateSx(dll, meta);
            var cave = (byte[])ShuangXiuCave.Code.Clone();
            ulong caveVa;
            var outb = PeImage.AddSection(dll, SxSectionName, cave.Length, 0xE0000020u, out caveVa);
            foreach (var fx in ShuangXiuCave.Fixups)
            {
                int at = (int)fx[0];
                long rel = (long)t.Ext[(string)fx[1]] - (long)(caveVa + (ulong)at + 4);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("合欢殿双修：跳转距离过大");
                Array.Copy(BitConverter.GetBytes((int)rel), 0, cave, at, 4);
            }
            Array.Copy(cave, 0, outb, new PeImage(outb).VaToOff(caveVa), cave.Length);
            foreach (var j in new[] { new { Off = t.SmsOff, Va = t.SmsVa, To = caveVa + 0 }, new { Off = t.SfoOff, Va = t.SfoVa, To = caveVa + 5 } })
            {
                long jo = new PeImage(outb).VaToOff(j.Va);   // 加大 PE 头后文件偏移会变
                outb[jo] = 0xE9;
                Array.Copy(BitConverter.GetBytes((int)((long)j.To - (long)(j.Va + 5))), 0, outb, jo + 1, 4);
            }
            // 经验条面板：call AddSkillFightExp → call 新代码 +10（按武学的主人加经验）
            long co = new PeImage(outb).VaToOff(t.ExpCallVa);
            Array.Copy(BitConverter.GetBytes((int)((long)(caveVa + 10) - (long)(t.ExpCallVa + 5))), 0, outb, co + 1, 4);
            // 武学说明「修炼需求」：call GetSkillNeedsDescribe → call 新代码 +15（双修选武学时列出所有参与者）
            long no = new PeImage(outb).VaToOff(t.NeedsCallVa);
            Array.Copy(BitConverter.GetBytes((int)((long)(caveVa + 15) - (long)(t.NeedsCallVa + 5))), 0, outb, no + 1, 4);
            // 经验条名字：call KungfuSkillLvData.Name → call 新代码 +20（双修的几条换成人物名和加成）
            long nmo = new PeImage(outb).VaToOff(t.NameCallVa);
            Array.Copy(BitConverter.GetBytes((int)((long)(caveVa + 20) - (long)(t.NameCallVa + 5))), 0, outb, nmo + 1, 4);
            return outb;
        }
    }
}
