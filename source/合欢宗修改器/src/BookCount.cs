// 秘籍持有数量提示：鼠标放在秘籍上时，在特效说明框下面再加一个框，显示主角背包 / 个人仓库里同一门武学秘籍的数量（按品质颜色）。
// 做法：GameAssembly.dll 末尾新增一个代码节 .hhbook，放入 BookCountCave 的机器码；
//       把 QuickDetail.ShowBookQuickDetail 尾声的第一条指令（movaps xmm6,[rsp+40h]）换成 jmp 到新代码，执行完再跳回。
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
            public long HookOff;           // 文件偏移：movaps xmm6,[rsp+40h]
            public ulong HookVa;
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

            // WorldData 所在的类指针槽：Player() 调用前的 mov rax,[rip+X] ; ... mov rax,[rax+0B8h] ; mov rcx,[rax] ; ... mov rcx,[rcx+20h]
            int callPlayer = -1;
            for (int p = (int)f; p < end - 5; p++)
                if (dll[p] == 0xE8 && pe.OffToVa(p) + 5 + (ulong)(long)Bin.I32(dll, p + 1) == t.Ext["Player"]) { callPlayer = p; break; }
            if (callPlayer < 0) throw new ModException("秘籍数量提示：找不到 WorldData.Player() 调用");
            int b8 = -1;
            for (int p = callPlayer - 7; p > callPlayer - 0x60; p--)
                if (dll[p] == 0x48 && dll[p + 1] == 0x8B && dll[p + 2] == 0x80 && Bin.U32(dll, p + 3) == 0xB8) { b8 = p; break; }
            if (b8 < 0 || Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x49, 0x20 }, b8, callPlayer) < 0)
                throw new ModException("秘籍数量提示：取 WorldData 的指令与预期不同");
            int ld = -1;
            for (int p = b8 - 7; p > b8 - 0x40; p--)
                if (dll[p] == 0x48 && dll[p + 1] == 0x8B && dll[p + 2] == 0x05) { ld = p; break; }
            if (ld < 0) throw new ModException("秘籍数量提示：找不到类指针");
            t.Ext["ClassSlot"] = pe.OffToVa(ld) + 7 + (ulong)(long)Bin.I32(dll, ld + 3);

            // 汇编里写死的字段偏移，逐一用游戏代码核对
            Require(dll, f, 0x800, new byte[] { 0x49, 0x8B, 0x4F, 0x70 }, "ItemData.bookData(+70h)");
            Require(dll, f, 0x800, new byte[] { 0x8B, 0x51, 0x10 }, "BookData.skillID(+10h)");
            Require(dll, pe.VaToOff(va("ItemData", "GetBookRareLvName", new string[0])), 0x100, new byte[] { 0x48, 0x63, 0x5E, 0x40 }, "ItemData.rareLv(+40h)");
            Require(dll, pe.VaToOff(va("ItemListData", "GetItemValue", new string[0])), 0x100, new byte[] { 0x48, 0x8B, 0x45, 0x28 }, "ItemListData.allItem(+28h)");
            Require(dll, pe.VaToOff(va("ItemListData", "RebuildItemTypeList", new string[0])), 0x400, new byte[] { 0x8B, 0x50, 0x14 }, "ItemData.type(+14h)");
            Require(dll, pe.VaToOff(va("HeroData", "LoseAllItem", new string[0])), 0x100, new byte[] { 0x48, 0x8B, 0x86, 0x20, 0x02, 0x00, 0x00 }, "HeroData.itemListData(+220h)");
            Require(dll, pe.VaToOff(va("PlotController", "OpenSelfStorage", new string[0])), 0x200, new byte[] { 0x28, 0x02, 0x00, 0x00 }, "HeroData.selfStorage(+228h)");
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
            // 钩子：movaps xmm6,[rsp+40h]（5 字节）→ jmp cave
            long j = (long)caveVa - (long)(t.HookVa + 5);
            outb[t.HookOff] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)j), 0, outb, t.HookOff + 1, 4);
            return outb;
        }
    }
}
