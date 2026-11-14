// 本门武学 = 藏经阁武学：主角所在门派的人，武学的秘籍在本门藏经阁里就算「本门武学」（吃本门武学威力加成）。
// 其他门派维持原判定（KungFuData.所属势力 == 出手者门派）。
// 做法：GameAssembly.dll 末尾新增代码节 .hhlib，放入 LibKungfuCave 的机器码；
//   BattleController.CountBaseDamage（伤害）和 KungfuSkillLvData.GetSkillFightScore（武学战力显示）里的
//   cmp dword [rax+18h],ecx ; jne 不本门（5 字节）换成 jmp 新代码。
using System;
using System.Collections.Generic;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        public const string LibSectionName = ".hhlib";

        class LibTargets
        {
            public long DmgOff, FsOff;
            public ulong DmgVa, FsVa;
            public Dictionary<string, ulong> Ext = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        }

        // 在函数里找：mov ecx,[reg+84h] ; cmp dword [rax+18h],ecx ; jne short
        static int FindForceCmp(byte[] d, int start, int end, byte[] movEcx, string what)
        {
            var pat = new byte[movEcx.Length + 4];
            Array.Copy(movEcx, pat, movEcx.Length);
            pat[movEcx.Length] = 0x39; pat[movEcx.Length + 1] = 0x48; pat[movEcx.Length + 2] = 0x18; pat[movEcx.Length + 3] = 0x75;
            return Unique(d, pat, start, end, what) + movEcx.Length;
        }

        LibTargets LocateLib(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var cs = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var t = new LibTargets();
            Func<string, string, string[], ulong> va = (ty, me, ps) => cs.MethodVa(m.FindMethod(ty, me, ps).Token);
            ulong dataBase = va("KungfuSkillLvData", "DataBase", new string[0]);
            t.Ext["Player"] = va("WorldData", "Player", new string[0]);
            t.Ext["GetForce"] = va("HeroData", "GetForce", new[] { "includeServant" });
            t.Ext["Find"] = va("ForceData", "BookStorageFindSkill", new[] { "_skillID" });
            t.Ext["World"] = FindWorldGetter(dll, pe, cs, m, t.Ext["Player"]);

            // 伤害：rdx = 出手者（mov rdx,[r13+40h]），rbx = KungfuSkillLvData（mov rcx,rbx ; call DataBase）
            long d = pe.VaToOff(va("BattleController", "CountBaseDamage", new[] { "attackUnit", "targetSkillLvData", "targetUnit", "fullCount" }));
            int h1 = FindForceCmp(dll, (int)d, (int)d + 0x3000, new byte[] { 0x8B, 0x8A, 0x84, 0x00, 0x00, 0x00 }, "伤害计算里的本门判定");
            Require(dll, h1 - 0x30, 0x30, new byte[] { 0x48, 0x8B, 0xCB, 0xE8 }, "伤害计算里武学数据在 rbx");
            Require(dll, h1 - 0x18, 0x18, new byte[] { 0x49, 0x8B, 0x55, 0x40 }, "伤害计算里出手者在 rdx");
            t.DmgOff = h1; t.DmgVa = pe.OffToVa(h1);
            t.Ext["Yes1"] = t.DmgVa + 5;
            t.Ext["No1"] = t.DmgVa + 5 + (ulong)(long)(sbyte)dll[h1 + 4];

            // 战力：r14 = 所属人物（GetBelongHero 结果），rdi = KungfuSkillLvData（mov rcx,rdi ; call DataBase）
            long f = pe.VaToOff(va("KungfuSkillLvData", "GetSkillFightScore", new string[0]));
            int h2 = FindForceCmp(dll, (int)f, (int)f + 0x1000, new byte[] { 0x41, 0x8B, 0x8E, 0x84, 0x00, 0x00, 0x00 }, "武学战力里的本门判定");
            Require(dll, h2 - 0x30, 0x30, new byte[] { 0x48, 0x8B, 0xCF, 0xE8 }, "武学战力里武学数据在 rdi");
            Require(dll, h2 - 0x40, 0x40, new byte[] { 0x4C, 0x8B, 0xF0 }, "武学战力里人物在 r14");
            t.FsOff = h2; t.FsVa = pe.OffToVa(h2);
            t.Ext["Yes2"] = t.FsVa + 5;
            t.Ext["No2"] = t.FsVa + 5 + (ulong)(long)(sbyte)dll[h2 + 4];

            // 两处之后紧跟的都应是取「本门武学威力」(213) 的 HeroSpeAddData.Get
            foreach (int h in new[] { h1, h2 })
                Require(dll, h, 0x30, new byte[] { 0xBA, 0xD5, 0x00, 0x00, 0x00 }, "本门武学威力(213)");
            Require(dll, pe.VaToOff(dataBase), 0xC0, new byte[] { 0x8B, 0x53, 0x10 }, "KungfuSkillLvData.skillID(+10h)");
            Require(dll, pe.VaToOff(t.Ext["Find"]), 0x100, new byte[] { 0x48, 0x8B, 0x86, 0xB8, 0x00, 0x00, 0x00 }, "ForceData.bookStorage(+B8h)");
            Require(dll, pe.VaToOff(t.Ext["Find"]), 0x100, new byte[] { 0x48, 0x8B, 0x48, 0x28 }, "ItemListData.allItem(+28h)");
            Require(dll, pe.VaToOff(t.Ext["GetForce"]), 0x40, new byte[] { 0x8B, 0x83, 0x84, 0x00, 0x00, 0x00 }, "HeroData.belongForceID(+84h)");
            return t;
        }

        bool LibKungfuPatched(byte[] dll) { return new PeImage(dll).Sections.Exists(s => s.Name == LibSectionName); }

        byte[] PatchLibKungfu(byte[] dll, byte[] meta)
        {
            if (LibKungfuPatched(dll)) throw new ModException("DLL 里已经有「本门武学 = 藏经阁武学」");
            var t = LocateLib(dll, meta);
            var cave = (byte[])LibKungfuCave.Code.Clone();
            ulong caveVa;
            var outb = PeImage.AddSection(dll, LibSectionName, cave.Length, out caveVa);
            foreach (var fx in LibKungfuCave.Fixups)
            {
                int at = (int)fx[0];
                long rel = (long)t.Ext[(string)fx[1]] - (long)(caveVa + (ulong)at + 4);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("本门武学：跳转距离过大");
                Array.Copy(BitConverter.GetBytes((int)rel), 0, cave, at, 4);
            }
            Array.Copy(cave, 0, outb, new PeImage(outb).VaToOff(caveVa), cave.Length);
            foreach (var j in new[] { new { Off = t.DmgOff, Va = t.DmgVa, To = caveVa + 0 }, new { Off = t.FsOff, Va = t.FsVa, To = caveVa + 5 } })
            {
                outb[j.Off] = 0xE9;
                Array.Copy(BitConverter.GetBytes((int)((long)j.To - (long)(j.Va + 5))), 0, outb, j.Off + 1, 4);
            }
            return outb;
        }
    }
}
