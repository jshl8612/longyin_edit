// 秘籍持有数量提示：鼠标放在秘籍或武功上时，在说明框最后再加一个框，显示同一门武学秘籍的数量（按品质颜色）：
//   背包、仓库、宗门仓库、藏经阁（门派取主角所属门派），数量为 0 的行不显示；全都没有时显示「背包：无」。
// 做法：GameAssembly.dll 末尾新增一个代码节 .hhbook，放入 BookCountCave 的机器码；
//       把 QuickDetail.ShowBookQuickDetail / ShowSkillQuickDetail 尾声的第一条指令（movaps xmm6,[rsp+N]）
//       换成 jmp 到新代码（入口 +0 / +5），执行完再跳回。
using System;
using System.Collections.Generic;
using System.Text;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        public const string BookSectionName = ".hhbook";

        class BookTargets
        {
            public long HookOff, SkillOff;    // 文件偏移：movaps xmm6,[rsp+40h] / [rsp+30h]
            public ulong HookVa, SkillVa;
            public Dictionary<string, ulong> Ext = new Dictionary<string, ulong>();
        }

        static int FindAll(byte[] d, byte[] pat, int start, int end, List<int> hits)
        {
            for (int p = start; ; p++)
            {
                p = Bin.IndexOf(d, pat, p, end);
                if (p < 0) break;
                hits.Add(p);
            }
            return hits.Count;
        }

        static void Require(byte[] d, long fOff, int len, byte[] pat, string what)
        {
            if (fOff < 0 || Bin.IndexOf(d, pat, (int)fOff, (int)fOff + len) < 0)
                throw new ModException("秘籍数量提示：" + what + " 与预期不同（游戏代码可能已改）");
        }

        BookTargets LocateBook(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var code = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var corlib = new Il2CppCode(pe, "mscorlib.dll");
            var t = new BookTargets();
            Func<string, string, string[], ulong> va = (ty, me, ps) => code.MethodVa(m.FindMethod(ty, me, ps).Token);

            ulong show = va("QuickDetail", "ShowBookQuickDetail", new[] { "target", "bookData" });
            t.Ext["Player"] = va("WorldData", "Player", new string[0]);
            t.Ext["AddTab"] = va("QuickDetail", "AddDescribeTab", new[] { "_text" });
            t.Ext["Color"] = va("GlobalData", "GenerateRareLvColorText", new[] { "text", "rareLv" });
            t.Ext["Concat"] = corlib.MethodVa(m.FindMethod("String", "Concat", new[] { "str0", "str1" }).Token);
            t.Ext["StrNew"] = pe.Export("il2cpp_string_new");
            t.Ext["GetForce"] = va("HeroData", "GetForce", new[] { "includeServant" });

            long f = pe.VaToOff(show);
            if (f < 0) throw new ModException("ShowBookQuickDetail 地址无效");
            int end = (int)Math.Min(dll.Length, f + 0x1800);

            // 钩子位置：唯一的 movaps xmm6,[rsp+40h] 后面紧跟 mov r14,[rsp+xx]（函数尾声）
            var hits = new List<int>();
            FindAll(dll, new byte[] { 0x0F, 0x28, 0x74, 0x24, 0x40, 0x4C, 0x8B, 0x74, 0x24 }, (int)f, end, hits);
            if (hits.Count != 1) throw new ModException("秘籍数量提示：找不到 ShowBookQuickDetail 的尾声（" + hits.Count + "）");
            t.HookOff = hits[0];
            t.HookVa = pe.OffToVa(t.HookOff);
            t.Ext["Back"] = t.HookVa + 5;

            // 武功说明：ShowSkillQuickDetail(target, skillLvData, showUseTime) 尾声；rdi = skillLvData（开头 mov rdi,r8）
            ulong showSkill = va("QuickDetail", "ShowSkillQuickDetail", new[] { "target", "skillLvData", "showUseTime" });
            long sf = pe.VaToOff(showSkill);
            int send = (int)Math.Min(dll.Length, sf + 0x1000);
            hits.Clear();
            FindAll(dll, new byte[] { 0x0F, 0x28, 0x74, 0x24, 0x30, 0x4C, 0x8B, 0x74, 0x24 }, (int)sf, send, hits);
            if (hits.Count != 1) throw new ModException("秘籍数量提示：找不到 ShowSkillQuickDetail 的尾声（" + hits.Count + "）");
            t.SkillOff = hits[0];
            t.SkillVa = pe.OffToVa(t.SkillOff);
            t.Ext["Back2"] = t.SkillVa + 5;
            Require(dll, sf, 0x20, new byte[] { 0x49, 0x8B, 0xF8 }, "ShowSkillQuickDetail 里 rdi = skillLvData");
            ulong refresh = va("QuickDetail", "RefreshSkillRangeUI", new[] { "skillLvData" });
            bool rdiOk = false;
            for (int p = (int)sf; p < t.SkillOff - 8 && !rdiOk; p++)
                if (dll[p] == 0x48 && dll[p + 1] == 0x8B && dll[p + 2] == 0xD7)          // mov rdx,rdi
                    for (int q = p + 3; q < p + 12; q++)
                        if (dll[q] == 0xE8 && CallTarget(pe, dll, q) == refresh) { rdiOk = true; break; }
            if (!rdiOk) throw new ModException("秘籍数量提示：ShowSkillQuickDetail 里 skillLvData 的寄存器与预期不同");
            Require(dll, pe.VaToOff(va("KungfuSkillLvData", "DataBase", new string[0])), 0xC0, new byte[] { 0x8B, 0x53, 0x10 }, "KungfuSkillLvData.skillID(+10h)");

            // 取 WorldData：用 AuctionController.Update 里那个自带初始化的静态 getter，
            // 并核对它取的类与 ShowBookQuickDetail 里取 WorldData 用的类相同
            ulong world = FindWorldGetter(dll, pe, code, m, t.Ext["Player"]);
            t.Ext["World"] = world;
            long wg = pe.VaToOff(world);
            int wl = Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x80, 0xB8, 0x00, 0x00, 0x00 }, (int)wg, (int)wg + 0x60);
            if (wl < 7 || dll[wl - 7] != 0x48 || dll[wl - 6] != 0x8B || dll[wl - 5] != 0x05) throw new ModException("秘籍数量提示：取 WorldData 的 getter 与预期不同");
            ulong encGetter = (ulong)Bin.I64(dll, (int)pe.VaToOff(RipTarget(pe, dll, wl - 7, 7)));
            int callPlayer = -1;
            for (int p = (int)f; p < end - 5; p++)
                if (dll[p] == 0xE8 && CallTarget(pe, dll, p) == t.Ext["Player"]) { callPlayer = p; break; }
            int b8 = -1;
            for (int p = callPlayer - 7; callPlayer > 0 && p > callPlayer - 0x60; p--)
                if (dll[p] == 0x48 && dll[p + 1] == 0x8B && dll[p + 2] == 0x80 && Bin.U32(dll, p + 3) == 0xB8) { b8 = p; break; }
            int ld = -1;
            for (int p = b8 - 7; b8 > 0 && p > b8 - 0x40; p--)
                if (dll[p] == 0x48 && dll[p + 1] == 0x8B && dll[p + 2] == 0x05) { ld = p; break; }
            if (ld < 0 || (ulong)Bin.I64(dll, (int)pe.VaToOff(RipTarget(pe, dll, ld, 7))) != encGetter)
                throw new ModException("秘籍数量提示：取 WorldData 的类与预期不同");

            // 汇编里写死的字段偏移，逐一用游戏代码核对
            Require(dll, f, 0x800, new byte[] { 0x49, 0x8B, 0x4F, 0x70 }, "ItemData.bookData(+70h)");
            Require(dll, f, 0x800, new byte[] { 0x8B, 0x51, 0x10 }, "BookData.skillID(+10h)");
            Require(dll, pe.VaToOff(va("ItemData", "GetBookRareLvName", new string[0])), 0x100, new byte[] { 0x48, 0x63, 0x5E, 0x40 }, "ItemData.rareLv(+40h)");
            Require(dll, pe.VaToOff(va("ItemListData", "GetItemValue", new string[0])), 0x100, new byte[] { 0x48, 0x8B, 0x45, 0x28 }, "ItemListData.allItem(+28h)");
            Require(dll, pe.VaToOff(va("ItemListData", "RebuildItemTypeList", new string[0])), 0x400, new byte[] { 0x8B, 0x50, 0x14 }, "ItemData.type(+14h)");
            Require(dll, pe.VaToOff(va("HeroData", "LoseAllItem", new string[0])), 0x100, new byte[] { 0x48, 0x8B, 0x86, 0x20, 0x02, 0x00, 0x00 }, "HeroData.itemListData(+220h)");
            Require(dll, pe.VaToOff(va("PlotController", "OpenSelfStorage", new string[0])), 0x200, new byte[] { 0x28, 0x02, 0x00, 0x00 }, "HeroData.selfStorage(+228h)");
            Require(dll, pe.VaToOff(va("PlotController", "OpenOtherForceStorage", new[] { "maxItemLv" })), 0x400, new byte[] { 0x48, 0x8B, 0x98, 0xA0, 0x00, 0x00, 0x00 }, "ForceData.forceStorage(+A0h)");
            Require(dll, pe.VaToOff(va("ForceData", "BookStorageFindSkill", new[] { "_skillID" })), 0x100, new byte[] { 0x48, 0x8B, 0x86, 0xB8, 0x00, 0x00, 0x00 }, "ForceData.bookStorage(+B8h)");
            Require(dll, pe.VaToOff(t.Ext["GetForce"]), 0x40, new byte[] { 0x8B, 0x83, 0x84, 0x00, 0x00, 0x00 }, "HeroData.GetForce");
            return t;
        }

        bool BookCountPatched(byte[] dll) { return new PeImage(dll).Sections.Exists(s => s.Name == BookSectionName); }

        byte[] PatchBookCount(byte[] dll, byte[] meta)
        {
            if (BookCountPatched(dll)) throw new ModException("DLL 里已经有秘籍数量提示");
            var t = LocateBook(dll, meta);
            var cave = (byte[])BookCountCave.Code.Clone();
            ulong caveVa;
            var outb = PeImage.AddSection(dll, BookSectionName, cave.Length, out caveVa);
            foreach (var fx in BookCountCave.Fixups)
            {
                int at = (int)fx[0];
                ulong target = t.Ext[(string)fx[1]];
                long rel = (long)target - (long)(caveVa + (ulong)at + 4);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("秘籍数量提示：跳转距离过大");
                Array.Copy(BitConverter.GetBytes((int)rel), 0, cave, at, 4);
            }
            var pe2 = new PeImage(outb);
            long caveOff = pe2.VaToOff(caveVa);
            Array.Copy(cave, 0, outb, caveOff, cave.Length);
            // 钩子：movaps xmm6,[rsp+N]（5 字节）→ jmp 入口（秘籍 +0，武功 +5）
            long j = (long)caveVa - (long)(t.HookVa + 5);
            outb[t.HookOff] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)j), 0, outb, t.HookOff + 1, 4);
            long j2 = (long)(caveVa + 5) - (long)(t.SkillVa + 5);
            outb[t.SkillOff] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)j2), 0, outb, t.SkillOff + 1, 4);
            return outb;
        }
    }
}
