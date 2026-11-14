// 首次研读也可跳过：原版只有「这门武学这个品级研读过」才问是否跳过（跳过得 记录 × 90%）。
// 补丁后这门武学这个品级没有记录时当作已经研读过：记录 = 默认值 / 0.9，跳过得默认值；
//   默认值按这门武学的武学等级（KungFuData 等级 0~5）取，在 ReadSkipCave 末尾。
// 做法：GameAssembly.dll 末尾新增代码节 .hhread，放入 ReadSkipCave 的机器码；
//       ReadBookController.RealStartReadBook 里两处「进入研读面板」的条件跳转改为跳到新代码。
using System;
using System.Collections.Generic;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        public const string ReadSectionName = ".hhread";

        class ReadTargets
        {
            public long JeOff, JaeOff;      // 文件偏移：两条 0F 8x rel32
            public ulong JeVa, JaeVa;
            public Dictionary<string, ulong> Ext = new Dictionary<string, ulong>();
        }

        static int Unique(byte[] d, byte[] pat, int start, int end, string what)
        {
            var hits = new List<int>();
            FindAll(d, pat, start, end, hits);
            if (hits.Count != 1) throw new ModException("首次研读跳过：" + what + " 与预期不同（" + hits.Count + "）");
            return hits[0];
        }

        ReadTargets LocateRead(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var code = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var t = new ReadTargets();
            Func<string, string, string[], ulong> va = (ty, me, ps) => code.MethodVa(m.FindMethod(ty, me, ps).Token);

            ulong rsr = va("ReadBookController", "RealStartReadBook", new string[0]);
            ulong panelFn = va("ReadBookController", "ShowReadBookPanel", new string[0]);
            ulong getMax = va("HeroData", "GetSkillMaxPracticeExp", new[] { "targetID" });
            t.Ext["Ctor"] = va("SkillMaxPracticeExpData", ".ctor", new[] { "_skillID" });
            t.Ext["AddRec"] = va("HeroData", "AddSkillMaxPracticeExp", new[] { "target" });
            t.Ext["ObjNew"] = pe.Export("il2cpp_object_new");
            t.Ext["Wb"] = pe.Export("il2cpp_gc_wbarrier_set_field");
            t.Ext["Db"] = va("KungfuSkillLvData", "DataBase", new string[0]);

            long f = pe.VaToOff(rsr);
            if (f < 0) throw new ModException("RealStartReadBook 地址无效");
            int end = (int)Math.Min(dll.Length, f + 0x900);

            // 取记录后：mov [rsi+0B0h],rax ... mov rbx,[rsi+0B0h] ; test rbx,rbx ; je 面板
            int a = Unique(dll, new byte[] { 0x48, 0x8B, 0x9E, 0xB0, 0x00, 0x00, 0x00, 0x48, 0x85, 0xDB, 0x0F, 0x84 }, (int)f, end, "记录为空的跳转");
            int call = a - 0x1B;
            if (dll[call] != 0xE8 || pe.OffToVa(call) + 5 + (ulong)(long)Bin.I32(dll, call + 1) != getMax)
                throw new ModException("首次研读跳过：GetSkillMaxPracticeExp 调用位置与预期不同");
            Require(dll, call, 0x1B, new byte[] { 0x48, 0x89, 0x86, 0xB0, 0x00, 0x00, 0x00 }, "ReadBookController.记录(+B0h)");
            t.JeOff = a + 10; t.JeVa = pe.OffToVa(t.JeOff);

            // 记录 <= 0：comiss xmm0,[rax+rdi*4+20h] ; jae 面板
            int b = Unique(dll, new byte[] { 0x0F, 0x2F, 0x44, 0xB8, 0x20, 0x0F, 0x83 }, (int)f, end, "记录为 0 的跳转");
            if (b < a || b - a > 0x60) throw new ModException("首次研读跳过：两处跳转的位置关系与预期不同");
            Require(dll, a, b - a, new byte[] { 0x48, 0x8B, 0x86, 0xC0, 0x00, 0x00, 0x00 }, "ReadBookController.秘籍(+C0h)");
            Require(dll, a, b - a, new byte[] { 0x48, 0x8B, 0x5B, 0x18 }, "SkillMaxPracticeExpData.maxReadExp(+18h)");
            Require(dll, a, b - a, new byte[] { 0x48, 0x63, 0x78, 0x40 }, "ItemData.rareLv(+40h)");
            t.JaeOff = b + 5; t.JaeVa = pe.OffToVa(t.JaeOff);

            ulong panel1 = t.JeVa + 6 + (ulong)(long)Bin.I32(dll, (int)t.JeOff + 2);
            ulong panel2 = t.JaeVa + 6 + (ulong)(long)Bin.I32(dll, (int)t.JaeOff + 2);
            if (panel1 != panel2) throw new ModException("首次研读跳过：两处跳转目标不同");
            long p = pe.VaToOff(panel1);
            if (!(dll[p] == 0x33 && dll[p + 1] == 0xD2 && dll[p + 2] == 0x48 && dll[p + 3] == 0x8B && dll[p + 4] == 0xCE && dll[p + 5] == 0xE8)
                || panel1 + 10 + (ulong)(long)Bin.I32(dll, (int)p + 6) != panelFn)
                throw new ModException("首次研读跳过：进入研读面板的代码与预期不同");
            t.Ext["Panel"] = panel1;
            t.Ext["Menu"] = t.JaeVa + 6;

            // 其他写死的偏移和数值
            Require(dll, f, 0x400, new byte[] { 0x48, 0x8B, 0x8E, 0xA8, 0x00, 0x00, 0x00 }, "ReadBookController.人物(+A8h)");
            Require(dll, f, 0x400, new byte[] { 0x48, 0x89, 0xBE, 0xB8, 0x00, 0x00, 0x00 }, "ReadBookController.武学(+B8h)");
            Require(dll, f, 0x400, new byte[] { 0x8B, 0x50, 0x10 }, "KungfuSkillLvData.skillID(+10h)");
            Require(dll, pe.VaToOff(getMax), 0x80, new byte[] { 0x48, 0x8B, 0x8D, 0x50, 0x02, 0x00, 0x00 }, "HeroData.skillMaxPracticeExpData(+250h)");
            // DataBase() 只用 this+10h 的 skillID 去查武学表（新代码用栈上的假对象调用它）
            {
                long db = pe.VaToOff(t.Ext["Db"]);
                ulong gsd = va("GameDataController", "GetSkillDataBase", new[] { "skillID" });
                int k = Unique(dll, new byte[] { 0x8B, 0x53, 0x10 }, (int)db, (int)db + 0xC0, "KungfuSkillLvData.DataBase 读取 skillID");
                bool jmpOk = false;
                for (int q = k; q < k + 0x20; q++)
                    if (dll[q] == 0xE9 && pe.OffToVa(q) + 5 + (ulong)(long)Bin.I32(dll, q + 1) == gsd) { jmpOk = true; break; }
                if (!jmpOk) throw new ModException("首次研读跳过：KungfuSkillLvData.DataBase 与预期不同");
            }
            Require(dll, pe.VaToOff(panelFn), 0x1000, new byte[] { 0x2B, 0x48, 0x34 }, "KungFuData.等级(+34h)");
            Require(dll, pe.VaToOff(t.Ext["Ctor"]), 0x100, new byte[] { 0x48, 0x8D, 0x4F, 0x18 }, "SkillMaxPracticeExpData 构造");
            Require(dll, pe.VaToOff(va("ReadBookController", "AutoReadBook", new string[0])), 0x100, new byte[] { 0xF3, 0x0F, 0x59, 0xB0, 0x68, 0x01, 0x00, 0x00 }, "跳过经验比例的位置");
            Require(dll, pe.VaToOff(va("GlobalData", ".cctor", new string[0])), 0x8000, new byte[] { 0xC7, 0x81, 0x68, 0x01, 0x00, 0x00, 0x66, 0x66, 0x66, 0x3F }, "跳过经验比例（90%）");
            return t;
        }

        bool ReadSkipPatched(byte[] dll) { return new PeImage(dll).Sections.Exists(s => s.Name == ReadSectionName); }

        byte[] PatchReadSkip(byte[] dll, byte[] meta)
        {
            if (ReadSkipPatched(dll)) throw new ModException("DLL 里已经有首次研读跳过");
            var t = LocateRead(dll, meta);
            var cave = (byte[])ReadSkipCave.Code.Clone();
            ulong caveVa;
            var outb = PeImage.AddSection(dll, ReadSectionName, cave.Length, out caveVa);
            foreach (var fx in ReadSkipCave.Fixups)
            {
                int at = (int)fx[0];
                long rel = (long)t.Ext[(string)fx[1]] - (long)(caveVa + (ulong)at + 4);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("首次研读跳过：跳转距离过大");
                Array.Copy(BitConverter.GetBytes((int)rel), 0, cave, at, 4);
            }
            Array.Copy(cave, 0, outb, new PeImage(outb).VaToOff(caveVa), cave.Length);
            // 两条条件跳转（0F 84 / 0F 83 rel32）改为跳到新代码，条件不变
            foreach (var j in new[] { new { Off = t.JeOff, Va = t.JeVa }, new { Off = t.JaeOff, Va = t.JaeVa } })
            {
                long rel = (long)caveVa - (long)(j.Va + 6);
                Array.Copy(BitConverter.GetBytes((int)rel), 0, outb, j.Off + 2, 4);
            }
            return outb;
        }
    }
}
