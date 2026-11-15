// 结缘上限无限：原版 PlotController.StartAskHeroToLoverPlot 里
//   主角.PreLovers.Count >= GlobalData.MaxLoverNum(4) 时提示「情侣数量已达上限！」并不能结缘。
// 补丁把这条 jge（0F 8D rel32）换成 6 字节 NOP，其余不变：
//   GlobalData.MaxLoverNum 仍是 4（人物详情里「情侣 N/4」的显示、GameController.MeetLoverResultRequire 的结局条件照旧）。
using System;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        static readonly byte[] LoverCmp = { 0x3B, 0x98, 0x94, 0x00, 0x00, 0x00 };          // cmp ebx,[rax+94h]（MaxLoverNum）
        static readonly byte[] LoverNop = { 0x66, 0x0F, 0x1F, 0x44, 0x00, 0x00 };          // 6 字节 NOP

        // 返回 cmp 指令的文件偏移（其后 6 字节是 jge 或已改的 NOP）
        int LocateLover(byte[] dll, byte[] meta, out bool patched)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var cs = new Il2CppCode(pe, "Assembly-CSharp.dll");
            Func<string, Exception> bad = what => new ModException("结缘上限：" + what + " 与预期不同（游戏代码可能已改）");
            long f = pe.VaToOff(cs.MethodVa(m.FindMethod("PlotController", "StartAskHeroToLoverPlot", new[] { "backFuc" }).Token));
            int at = -1, hits = 0;
            for (int q = Bin.IndexOf(dll, LoverCmp, (int)f, (int)f + 0x600); q >= 0; q = Bin.IndexOf(dll, LoverCmp, q + 1, (int)f + 0x600)) { at = q; hits++; }
            if (hits != 1) throw bad("情侣数量判定");
            bool jge = dll[at + 6] == 0x0F && dll[at + 7] == 0x8D;
            bool nop = true;
            for (int k = 0; k < 6; k++) if (dll[at + 6 + k] != LoverNop[k]) nop = false;
            if (!jge && !nop) throw bad("情侣数量判定后的跳转");
            // 前面：mov rax,[rax+330h](PreLovers) ; … ; mov ebx,[rax+18h](Count)
            if (Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x80, 0x30, 0x03, 0x00, 0x00 }, at - 0x50, at) < 0 ||
                Bin.IndexOf(dll, new byte[] { 0x8B, 0x58, 0x18 }, at - 0x40, at) < 0) throw bad("情侣列表");
            var fo = new Il2CppFieldOffsets(pe, m);
            if (fo.Offset(null, "HeroData", "PreLovers") != 0x330) throw bad("HeroData.PreLovers(+330h)");
            if (fo.Offset(null, "GlobalData", "MaxLoverNum") != 0x94) throw bad("GlobalData.MaxLoverNum(+94h)");
            if (jge)
            {
                // jge 的目标应是提示「情侣数量已达上限！」的分支
                long tgt = at + 12 + Bin.I32(dll, at + 8);
                if (tgt <= at || tgt > f + 0x1000) throw bad("超上限分支");
            }
            patched = nop;
            return at;
        }

        bool LoverPatched(byte[] dll, byte[] meta) { bool p; LocateLover(dll, meta, out p); return p; }

        byte[] PatchLover(byte[] dll, byte[] meta)
        {
            bool p;
            int at = LocateLover(dll, meta, out p);
            if (p) throw new ModException("DLL 里已经有「结缘上限无限」");
            var outb = (byte[])dll.Clone();
            Array.Copy(LoverNop, 0, outb, at + 6, 6);
            return outb;
        }
    }
}
