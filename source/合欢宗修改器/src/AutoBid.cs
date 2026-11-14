// 拍卖自动出价：开拍按钮旁加「自动出价」（开拍并对这一件自动出价），出价按钮旁加「自动出价 / 停止自动」开关。
// 自动出价：当前出价人不是主角时，以「下一口价」出价，下一口价超过背包钱数就停止。
// 做法：GameAssembly.dll 末尾新增可读写执行的代码节 .hhauct，放入 AutoBidCave 的机器码；挂三处：
//   Button.Press 开头（按名字认出复制出来的按钮）、AuctionController.StartAuctionRoundButtonClicked 开头、
//   AuctionController.Update 里取完 this 之后（建按钮、刷新文字、自动出价）。
using System;
using System.Collections.Generic;

namespace LongYinMod
{
    public partial class GamePatcher
    {
        public const string AuctionSectionName = ".hhauct";

        class AuctionTargets
        {
            public long PressOff, StartOff, UpdOff;
            public ulong PressVa, StartVa, UpdVa;
            public Dictionary<string, ulong> Ext = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);   // 名字与 gen.py 生成的大小写不同
        }

        static readonly byte[] PushRbxSub20 = { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x20 };

        static ulong CallTarget(PeImage pe, byte[] d, int at) { return pe.OffToVa(at) + 5 + (ulong)(long)Bin.I32(d, at + 1); }
        static ulong RipTarget(PeImage pe, byte[] d, int insn, int len) { return pe.OffToVa(insn) + (ulong)len + (ulong)(long)Bin.I32(d, insn + len - 4); }

        AuctionTargets LocateAuction(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var cs = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var core = new Il2CppCode(pe, "UnityEngine.CoreModule.dll");
            var ui = new Il2CppCode(pe, "UnityEngine.UI.dll");
            var t = new AuctionTargets();
            Func<string, string, string[], ulong> va = (ty, me, ps) => cs.MethodVa(m.FindMethod(ty, me, ps).Token);
            Func<string, string, string[], ulong> vaCore = (ty, me, ps) => core.MethodVa(m.FindMethod("UnityEngine", ty, me, ps).Token);
            Func<string, string, string[], ulong> vaUi = (ty, me, ps) => ui.MethodVa(m.FindMethod("UnityEngine.UI", ty, me, ps).Token);
            Func<string, Exception> bad = what => new ModException("拍卖自动出价：" + what + " 与预期不同（游戏代码可能已改）");

            ulong press = vaUi("Button", "Press", new string[0]);
            ulong start = va("AuctionController", "StartAuctionRoundButtonClicked", new string[0]);
            ulong upd = va("AuctionController", "Update", new string[0]);
            t.Ext["GetName"] = vaCore("Object", "get_name", new string[0]);
            t.Ext["SetName"] = vaCore("Object", "set_name", new[] { "value" });
            t.Ext["Inst"] = vaCore("Object", "Instantiate", new[] { "original", "parent", "instantiateInWorldSpace" });
            t.Ext["GetTrans"] = vaCore("GameObject", "get_transform", new string[0]);
            t.Ext["Find"] = vaCore("Transform", "Find", new[] { "n" });
            t.Ext["SetLPos"] = vaCore("Transform", "set_localPosition", new[] { "value" });
            t.Ext["ChildN"] = vaCore("Transform", "get_childCount", new string[0]);
            t.Ext["Child"] = vaCore("Transform", "GetChild", new[] { "index" });
            t.Ext["Size"] = vaCore("RectTransform", "get_sizeDelta", new string[0]);
            t.Ext["StartCo"] = vaCore("MonoBehaviour", "StartCoroutine", new[] { "routine" });
            t.Ext["Interact"] = vaUi("Selectable", "set_interactable", new[] { "value" });
            t.Ext["SetText"] = va("LTLocalization", "SetText", new[] { "targetText", "targetValue" });
            t.Ext["Next"] = va("AuctionController", "GetNextOfferMoney", new string[0]);
            t.Ext["Refresh"] = va("AuctionController", "RefreshOfferMoney", new[] { "newOfferMoney", "newOfferHero" });
            t.Ext["Player"] = va("WorldData", "Player", new string[0]);
            t.Ext["StrNew"] = pe.Export("il2cpp_string_new");

            // 钩子位置
            t.PressOff = pe.VaToOff(press); t.PressVa = press;
            t.StartOff = pe.VaToOff(start); t.StartVa = start;
            if (Bin.IndexOf(dll, PushRbxSub20, (int)t.PressOff, (int)t.PressOff + 6) != t.PressOff) throw bad("Button.Press 开头");
            if (Bin.IndexOf(dll, PushRbxSub20, (int)t.StartOff, (int)t.StartOff + 6) != t.StartOff) throw bad("开拍按钮函数开头");
            t.Ext["PressBack"] = press + 6;
            t.Ext["StartBack"] = start + 6;

            long u = pe.VaToOff(upd);
            int uend = (int)Math.Min(dll.Length, u + 0x700);
            // cmp dword [rbx+48h],0 ; mov dword [rsp+50h],0 ; je ...
            int h = Unique(dll, new byte[] { 0x83, 0x7B, 0x48, 0x00, 0xC7, 0x44, 0x24, 0x50, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x84 }, (int)u, uend, "Update 开头的阶段判断");
            t.UpdOff = h; t.UpdVa = pe.OffToVa(h);
            t.Ext["UpdBack"] = t.UpdVa + 12;

            // Update 里：mov rdx,[rip+槽] ; mov rcx,rax ; call GetComponent<T>
            var comps = new List<int>();
            FindAll(dll, new byte[] { 0x48, 0x8B, 0xC8, 0xE8 }, (int)u, uend, comps);
            ulong getComp = 0, slotText = 0, slotBtn = 0;
            ulong interact = t.Ext["Interact"];
            foreach (int c in comps)
            {
                int ld = c - 7;
                if (dll[ld] != 0x48 || dll[ld + 1] != 0x8B || dll[ld + 2] != 0x15) continue;
                ulong tgt = CallTarget(pe, dll, c + 3);
                ulong slot = RipTarget(pe, dll, ld, 7);
                if (getComp == 0) getComp = tgt;
                if (tgt != getComp) continue;
                bool toInteract = false;
                for (int q = c + 8; q < c + 0x40; q++)
                    if (dll[q] == 0xE8 && CallTarget(pe, dll, q) == interact) { toInteract = true; break; }
                if (toInteract) { if (slotBtn == 0) slotBtn = slot; }
                else if (slotText == 0) slotText = slot;
            }
            if (getComp == 0 || slotText == 0 || slotBtn == 0) throw bad("Update 里取 Text / Button 组件的代码");
            t.Ext["GetComp"] = getComp; t.Ext["SlotText"] = slotText; t.Ext["SlotBtn"] = slotBtn;

            // 取 WorldData：xor ecx,ecx ; call X ; test rax,rax ; ... mov rcx,[rax+20h] ; ... call WorldData.Player
            ulong world = 0;
            for (int q = (int)u; q < uend - 0x30 && world == 0; q++)
                if (dll[q] == 0x33 && dll[q + 1] == 0xC9 && dll[q + 2] == 0xE8
                    && Bin.IndexOf(dll, new byte[] { 0x48, 0x8B, 0x48, 0x20 }, q + 7, q + 0x18) >= 0)
                    for (int r = q + 7; r < q + 0x30; r++)
                        if (dll[r] == 0xE8 && CallTarget(pe, dll, r) == t.Ext["Player"]) { world = CallTarget(pe, dll, q + 2); break; }
            if (world == 0) throw bad("Update 里取主角的代码");
            t.Ext["World"] = world;

            // 子物体名 "OfferButton" 仍是出价按钮（Update 里以字符串常量出现）
            bool offerName = false;
            for (int q = (int)u; q < uend - 7 && !offerName; q++)
                if (dll[q] == 0x48 && dll[q + 1] == 0x8B && dll[q + 2] == 0x15)
                {
                    long so = pe.VaToOff(RipTarget(pe, dll, q, 7));
                    if (so <= 0 || so + 8 > dll.Length) continue;
                    ulong enc = (ulong)Bin.I64(dll, (int)so);
                    if ((enc >> 32) != 0 || (enc & 1) == 0 || (enc >> 29) != 5) continue;
                    if (m.Literal((int)((enc & 0x1FFFFFFF) >> 1)) == "OfferButton") offerName = true;
                }
            if (!offerName) throw bad("出价按钮名字 OfferButton");

            // 汇编里写死的字段偏移
            Require(dll, u, 0x700, new byte[] { 0x83, 0x7B, 0x48, 0x02 }, "AuctionController.auctionStep(+48h)");
            Require(dll, u, 0x700, new byte[] { 0x80, 0x7B, 0x4C, 0x00 }, "AuctionController.havePlayer(+4Ch)");
            Require(dll, u, 0x700, new byte[] { 0x48, 0x8B, 0x4B, 0x20 }, "AuctionController.playerAuctionUI(+20h)");
            Require(dll, u, 0x700, new byte[] { 0x80, 0xBB, 0xB4, 0x00, 0x00, 0x00, 0x00 }, "AuctionController.skipping(+B4h)");
            Require(dll, u, 0x700, new byte[] { 0xF3, 0x0F, 0x10, 0xB3, 0x94, 0x00, 0x00, 0x00 }, "AuctionController.offerRoundLeftTime(+94h)");
            Require(dll, t.StartOff, 0x80, new byte[] { 0x48, 0x8B, 0x4B, 0x28 }, "AuctionController.startAuctionButton(+28h)");
            Require(dll, pe.VaToOff(va("AuctionController", "SetOfferHero", new[] { "targetHero" })), 0x80, new byte[] { 0x48, 0x89, 0xB3, 0x98, 0x00, 0x00, 0x00 }, "AuctionController.nowOfferHero(+98h)");
            Require(dll, pe.VaToOff(t.Ext["Next"]), 0x60, new byte[] { 0x48, 0x8B, 0x5B, 0x58 }, "AuctionController.auctionItemList(+58h)");
            long sure = pe.VaToOff(va("AuctionController", "SureOfferMoneyButtonClicked", new string[0]));
            Require(dll, sure, 0x140, new byte[] { 0x48, 0x8B, 0x80, 0x20, 0x02, 0x00, 0x00 }, "HeroData.itemListData(+220h)");
            Require(dll, sure, 0x140, new byte[] { 0x66, 0x0F, 0x6E, 0x40, 0x18 }, "ItemListData.money(+18h)");
            return t;
        }

        bool AutoBidPatched(byte[] dll) { return new PeImage(dll).Sections.Exists(s => s.Name == AuctionSectionName); }

        byte[] PatchAutoBid(byte[] dll, byte[] meta)
        {
            if (AutoBidPatched(dll)) throw new ModException("DLL 里已经有拍卖自动出价");
            var t = LocateAuction(dll, meta);
            var cave = (byte[])AutoBidCave.Code.Clone();
            ulong caveVa;
            var outb = PeImage.AddSection(dll, AuctionSectionName, cave.Length, 0xE0000020u, out caveVa);
            foreach (var fx in AutoBidCave.Fixups)
            {
                int at = (int)fx[0];
                long rel = (long)t.Ext[(string)fx[1]] - (long)(caveVa + (ulong)at + 4);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("拍卖自动出价：跳转距离过大");
                Array.Copy(BitConverter.GetBytes((int)rel), 0, cave, at, 4);
            }
            Array.Copy(cave, 0, outb, new PeImage(outb).VaToOff(caveVa), cave.Length);
            Action<long, ulong, ulong, int> jmp = (off, from, to, len) =>
            {
                outb[off] = 0xE9;
                Array.Copy(BitConverter.GetBytes((int)((long)to - (long)(from + 5))), 0, outb, off + 1, 4);
                for (int i = 5; i < len; i++) outb[off + i] = 0x90;
            };
            jmp(t.PressOff, t.PressVa, caveVa + 0, 6);
            jmp(t.StartOff, t.StartVa, caveVa + 5, 6);
            jmp(t.UpdOff, t.UpdVa, caveVa + 10, 12);
            return outb;
        }
    }
}
