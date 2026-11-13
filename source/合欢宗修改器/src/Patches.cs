// LongYinMod - game file patches. Everything is located by name, nothing by fixed offset,
// so a game update either keeps working or is reported as incompatible before anything is written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LongYinMod
{
    public static class Hhd
    {
        public const string Name = "合欢殿";
        public const string LookAlike = "金顶";           // borrow appearance / icon / background
        public const string AreaLimit = "999";           // matches no area -> never pre-placed at new game

        // column name -> value; columns not listed are copied from the look-alike row
        public static Dictionary<string, string> Row(int newId, int iconId)
        {
            return new Dictionary<string, string>
            {
                { "id", newId.ToString() },
                { "名字", Name },
                { "图标", iconId.ToString() },
                { "类别", "6" },
                { "唯一", "1" },
                { "居中", "0" },
                { "随机位置", "4" },
                { "互动选项", "提升内力上限-我-StudyMaxState-1;提升生命上限-我-StudyMaxState-0;宴饮-我-SpeStartParty;招女弟子?招募门派弟子-我-StartHireFollower" },
                { "每月产出", "粮食+150" },
                { "每月维护", "银钱-100" },
                { "加成", "全员内功2;全员绝技2;内功经验0.2;全员口才2" },
                { "增加效率", "" },
                { "升级消耗", "木料1000;药材1000" },
                { "建造时间", "20" },
                { "限制区域", AreaLimit },
                { "商店设置", "" },
                { "描述", "合欢宗之本殿，红绡低垂，暖香氤氲。门中弟子于此调息运气、涵养内元，兼习绝技；宾客往来，宴饮不绝。" },
            };
        }
    }

    // new talent (HeroTagData row); effects are user-configurable (TalentConfig)
    public static class Hhzz
    {
        public const string Name = "合欢宗主";
        public const string DefaultEffect = "吸内+0.2;斗转+0.2;内劲+0.2;好感获取+0.2";
        public static string Effect { get { return TalentConfig.Load(); } }
        public static Dictionary<string, string> Row(int newId)
        {
            return new Dictionary<string, string>
            {
                { "编号", newId.ToString() }, { "名称", Name }, { "价值", "10" }, { "影响目标", "Self" },
                { "效果", Effect }, { "同义组", "" }, { "反义组", "" }, { "可随机", "0" }, { "展示顺序", "0" },
                { "领悟需求", "" }, { "替换", "" }, { "大类", "特效" }, { "显示箭头", "FALSE" },
            };
        }
        // every effect name must already exist in SpeAddDataBase, otherwise the game would not know it
        public static string[] EffectNames { get { return TalentConfig.Parse(Effect).Select(e => e.Key).ToArray(); } }
    }

    public static class TalentConfig
    {
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LongYinMod", "talent.txt"); } }
        public static string Load()
        {
            try { if (File.Exists(FilePath)) { var t = File.ReadAllText(FilePath).Trim(); if (Valid(t)) return t; } } catch { }
            return Hhzz.DefaultEffect;
        }
        public static void Save(string effect)
        {
            if (!Valid(effect)) throw new ModException("天赋效果格式不正确");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, effect);
        }
        public static bool Valid(string effect)
        {
            if (string.IsNullOrEmpty(effect) || effect.Contains(",")) return false;
            try { return Parse(effect).Count > 0; } catch { return false; }
        }
        // "吸内+0.2;力道+5" -> [(吸内,0.2),(力道,5)]
        public static List<KeyValuePair<string, double>> Parse(string effect)
        {
            var l = new List<KeyValuePair<string, double>>();
            foreach (var part in effect.Split(';'))
            {
                if (part.Length == 0) continue;
                var m = System.Text.RegularExpressions.Regex.Match(part, @"^(.+?)([+-]?[0-9]*\.?[0-9]+)$");
                if (!m.Success) throw new ModException("无法识别的效果：" + part);
                l.Add(new KeyValuePair<string, double>(m.Groups[1].Value, double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
            }
            return l;
        }
        public static string Format(IEnumerable<KeyValuePair<string, double>> items)
        {
            return string.Join(";", items.Select(kv => kv.Key + (kv.Value >= 0 ? "+" : "-") + Math.Abs(kv.Value).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    public class EffectInfo
    {
        public string Name, Desc;
        public bool Percent;
        public int Trigger;
        public string TriggerText { get { return Trigger == 0 ? "常驻" : Trigger == 1 ? "攻击时" : Trigger == 2 ? "被攻击时" : "时机" + Trigger; } }
    }

    public class GameFiles
    {
        public string Root;
        public string DataDir { get { return Path.Combine(Root, "LongYinLiZhiZhuan_Data"); } }
        public string Dll { get { return Path.Combine(Root, "GameAssembly.dll"); } }
        public string Meta { get { return Path.Combine(DataDir, "il2cpp_data", "Metadata", "global-metadata.dat"); } }
        public string Resources { get { return Path.Combine(DataDir, "resources.assets"); } }
        public string Ggm { get { return Path.Combine(DataDir, "globalgamemanagers"); } }
        public GameFiles(string root) { Root = root; }

        public bool Looks()
        {
            return File.Exists(Dll) && File.Exists(Resources) && File.Exists(Ggm) && File.Exists(Meta);
        }

        public static bool GameRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("LongYinLiZhiZhuan").Length > 0; }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // BuildingData table helpers
    // ---------------------------------------------------------------------------------------------
    public class BuildingTable
    {
        public string Bom = "";
        public List<string> Header;
        public List<List<string>> Rows = new List<List<string>>();
        public string Text;

        public string NameCol = "名字", IdCol = "id", What = "建筑表";
        public BuildingTable(string text, string nameCol, string idCol, string what) : this(text) { NameCol = nameCol; IdCol = idCol; What = what; }
        public BuildingTable(string text)
        {
            Text = text;
            if (text.StartsWith("﻿")) { Bom = "﻿"; text = text.Substring(1); }
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Header = lines[0].Split(',').ToList();
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0) Rows.Add(lines[i].Split(',').ToList());
        }

        public int Col(string prefix)
        {
            for (int i = 0; i < Header.Count; i++)
                if (Header[i].StartsWith(prefix)) return i;
            throw new ModException(What + "里找不到列「" + prefix + "」，表格结构可能已变");
        }

        public List<string> RowByName(string name)
        {
            int c = Col(NameCol);
            foreach (var r in Rows) if (r.Count > c && r[c] == name) return r;
            return null;
        }

        public int MaxId()
        {
            int c = Col(IdCol), m = -1;
            foreach (var r in Rows) { int v; if (int.TryParse(r[c], out v) && v > m) m = v; }
            return m;
        }

        public int Id(List<string> row) { return int.Parse(row[Col(IdCol)]); }

        public string AppendRow(Dictionary<string, string> want, List<string> template)
        {
            foreach (var k in want.Keys) Col(k);
            var row = template != null ? new List<string>(template) : new List<string>();
            while (row.Count < Header.Count) row.Add("");
            foreach (var kv in want) row[Col(kv.Key)] = kv.Value;
            foreach (var v in row) if (v.Contains(",")) throw new ModException(What + "数据含逗号");
            string body = Text;
            if (!body.EndsWith("\r\n")) body += "\r\n";
            return body + string.Join(",", row) + "\r\n";
        }

        // id of the special building whose option needs "门派功能0" (first sect's special building)
        public int SpecialBase()
        {
            int ci = Col("互动选项");
            foreach (var r in Rows)
                if (r.Count > ci && r[ci].Contains("门派功能0-")) return Id(r);
            throw new ModException("建筑表里找不到「门派功能0」的特殊建筑，无法确定特殊建筑编号规则");
        }
    }

    // ---------------------------------------------------------------------------------------------
    public partial class GamePatcher
    {
        public GameFiles G;
        public List<string> Log = new List<string>();
        public int NewId = -1, IconId = -1, SpecialBase = -1;
        public string AtlasFile;   // absolute path of the file holding UIAtlas

        public GamePatcher(GameFiles g) { G = g; }

        static byte[] ReadAll(string p) { return File.ReadAllBytes(p); }
        static string Bak(string p) { return p + ".bak"; }

        // ---------------- resources.assets : BuildingData row ----------------
        internal static SerializedFile.ObjInfo FindTextAsset(SerializedFile sf, string name)
        {
            foreach (var o in sf.OfClass(49)) if (sf.ObjName(o) == name) return o;
            throw new ModException("resources.assets 里找不到表 " + name);
        }

        internal static string ReadTextAsset(byte[] raw, out int scriptStart, out int scriptLen)
        {
            int o = 0;
            Bin.ReadAlignedString(raw, ref o);
            scriptLen = Bin.I32(raw, o);
            scriptStart = o + 4;
            return Encoding.UTF8.GetString(raw, scriptStart, scriptLen);
        }

        public static BuildingTable LoadBuildingTable(byte[] resources)
        {
            var sf = new SerializedFile(resources);
            var oi = FindTextAsset(sf, "BuildingData");
            int ss, sl;
            return new BuildingTable(ReadTextAsset(sf.Raw(oi), out ss, out sl));
        }

        public static BuildingTable LoadTagTable(byte[] resources)
        {
            var sf = new SerializedFile(resources);
            int ss, sl;
            return new BuildingTable(ReadTextAsset(sf.Raw(FindTextAsset(sf, "HeroTagData")), out ss, out sl), "名称", "编号", "天赋表");
        }

        public static List<EffectInfo> LoadEffects(byte[] resources)
        {
            var sf = new SerializedFile(resources);
            int ss, sl;
            var t = new BuildingTable(ReadTextAsset(sf.Raw(FindTextAsset(sf, "SpeAddDataBase")), out ss, out sl), "特效", "序号", "效果表");
            int cn = t.Col("特效"), cp = t.Col("百分比显示"), cd = t.Col("描述"), ct = t.Col("触发时机");
            var l = new List<EffectInfo>();
            foreach (var r in t.Rows)
            {
                if (r.Count <= Math.Max(Math.Max(cn, cp), Math.Max(cd, ct)) || r[cn].Length == 0) continue;
                int tr; int.TryParse(r[ct], out tr);
                l.Add(new EffectInfo { Name = r[cn], Percent = r[cp] == "1", Desc = r[cd].Replace("\\n", " "), Trigger = tr });
            }
            return l;
        }

        static bool HasBuilding(byte[] res) { return LoadBuildingTable(res).RowByName(Hhd.Name) != null; }
        static bool HasTag(byte[] res) { return LoadTagTable(res).RowByName(Hhzz.Name) != null; }

        public int TagId = -1;

        static byte[] ReplaceText(SerializedFile sf, SerializedFile.ObjInfo oi, string body)
        {
            var raw = sf.Raw(oi);
            int ss, sl;
            ReadTextAsset(raw, out ss, out sl);
            var script = Encoding.UTF8.GetBytes(body);
            var ms = new MemoryStream();
            ms.Write(raw, 0, ss - 4);
            ms.Write(BitConverter.GetBytes(script.Length), 0, 4);
            ms.Write(script, 0, script.Length);
            int tail = Bin.Align4(ss + sl);
            while (ms.Length % 4 != 0) ms.WriteByte(0);
            if (tail < raw.Length) ms.Write(raw, tail, raw.Length - tail);
            return sf.WithObjectReplaced(oi.PathId, ms.ToArray());
        }

        byte[] PatchBuilding(byte[] res)
        {
            var sf = new SerializedFile(res);
            var oi = FindTextAsset(sf, "BuildingData");
            int ss, sl;
            var t = new BuildingTable(ReadTextAsset(sf.Raw(oi), out ss, out sl));
            if (t.RowByName(Hhd.Name) != null) throw new ModException("文件里已经有合欢殿");
            var look = t.RowByName(Hhd.LookAlike);
            if (look == null) throw new ModException("建筑表里找不到「" + Hhd.LookAlike + "」，无法借用外观");
            if (t.Id(look) != IconId || t.MaxId() + 1 != NewId) throw new ModException("内部错误：建筑编号不一致");
            return ReplaceText(sf, oi, t.AppendRow(Hhd.Row(NewId, int.Parse(look[t.Col("图标")])), look));
        }

        byte[] PatchTag(byte[] res)
        {
            var sf = new SerializedFile(res);
            int ss, sl;
            var spe = new BuildingTable(ReadTextAsset(sf.Raw(FindTextAsset(sf, "SpeAddDataBase")), out ss, out sl), "特效", "序号", "效果表");
            int nc = spe.Col("特效");
            if (!TalentConfig.Valid(Hhzz.Effect)) throw new ModException("合欢宗主天赋效果设置无效");
            foreach (var en in Hhzz.EffectNames)
                if (!spe.Rows.Any(r => r.Count > nc && r[nc] == en)) throw new ModException("效果表里找不到效果「" + en + "」");
            var ot = FindTextAsset(sf, "HeroTagData");
            var tags = new BuildingTable(ReadTextAsset(sf.Raw(ot), out ss, out sl), "名称", "编号", "天赋表");
            if (tags.RowByName(Hhzz.Name) != null) throw new ModException("文件里已经有天赋合欢宗主");
            TagId = tags.MaxId() + 1;
            if (TagId != tags.Rows.Count) throw new ModException("天赋表编号不连续，无法安全追加");
            return ReplaceText(sf, ot, tags.AppendRow(Hhzz.Row(TagId), null));
        }

        // ---------------- globalgamemanagers : Resources container ----------------
        class Entry { public byte[] Key; public int FileId; public long PathId; }

        static SerializedFile.ObjInfo ResourceManager(SerializedFile sf)
        {
            var l = sf.OfClass(147);
            if (l.Count != 1) throw new ModException("globalgamemanagers 里找不到资源路径表");
            return l[0];
        }

        static List<Entry> ReadContainer(byte[] raw, out int end)
        {
            var l = new List<Entry>();
            int o = 0;
            int n = Bin.I32(raw, o); o += 4;
            if (n <= 0 || n > 1000000) throw new ModException("资源路径表异常");
            for (int i = 0; i < n; i++)
            {
                int len = Bin.I32(raw, o);
                var e = new Entry();
                e.Key = new byte[len];
                Array.Copy(raw, o + 4, e.Key, 0, len);
                o = Bin.Align4(o + 4 + len);
                e.FileId = Bin.I32(raw, o);
                e.PathId = Bin.I64(raw, o + 4);
                o += 12;
                l.Add(e);
            }
            end = o;
            return l;
        }

        static string K(Entry e) { return Encoding.UTF8.GetString(e.Key); }

        bool GgmPatched(byte[] data)
        {
            var sf = new SerializedFile(data);
            int end;
            var c = ReadContainer(sf.Raw(ResourceManager(sf)), out end);
            return c.Any(e => K(e) == "textures/background/" + Hhd.Name);
        }

        static int CompareBytes(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }

        byte[] PatchGgm(byte[] pristine)
        {
            var sf = new SerializedFile(pristine);
            var rm = ResourceManager(sf);
            var raw = sf.Raw(rm);
            int end;
            var c = ReadContainer(raw, out end);
            if (c.Any(e => K(e) == "textures/background/" + Hhd.Name)) throw new ModException("原版文件里已经有合欢殿路径");
            var add = new List<Entry>();
            string skSrc = "skeleton/building/" + IconId + "/", skDst = "skeleton/building/" + NewId + "/";
            if (c.Any(e => K(e).StartsWith(skDst))) throw new ModException("游戏里已存在编号 " + NewId + " 的建筑骨骼，编号冲突");
            foreach (var e in c)
            {
                string k = K(e);
                if (k.StartsWith(skSrc))
                    add.Add(new Entry { Key = Encoding.UTF8.GetBytes(skDst + k.Substring(skSrc.Length)), FileId = e.FileId, PathId = e.PathId });
                if (k == "textures/background/" + Hhd.LookAlike)
                    add.Add(new Entry { Key = Encoding.UTF8.GetBytes("textures/background/" + Hhd.Name), FileId = e.FileId, PathId = e.PathId });
            }
            if (!add.Any(e => K(e).StartsWith(skDst))) throw new ModException("找不到「" + Hhd.LookAlike + "」的地图造型");
            if (!add.Any(e => K(e).StartsWith("textures/"))) throw new ModException("找不到「" + Hhd.LookAlike + "」的内景背景");
            // stable merge by byte order (the original table is byte-ordered)
            var all = new List<Entry>(c);
            foreach (var a in add)
            {
                int pos = all.Count;
                for (int i = 0; i < all.Count; i++) if (CompareBytes(all[i].Key, a.Key) > 0) { pos = i; break; }
                all.Insert(pos, a);
            }
            var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(all.Count), 0, 4);
            foreach (var e in all)
            {
                ms.Write(BitConverter.GetBytes(e.Key.Length), 0, 4);
                ms.Write(e.Key, 0, e.Key.Length);
                while (ms.Length % 4 != 0) ms.WriteByte(0);
                ms.Write(BitConverter.GetBytes(e.FileId), 0, 4);
                ms.Write(BitConverter.GetBytes(e.PathId), 0, 8);
            }
            ms.Write(raw, end, raw.Length - end);
            return sf.WithObjectReplaced(rm.PathId, ms.ToArray());
        }

        // where UIAtlas lives (resolved from the pristine globalgamemanagers)
        long atlasPathId;
        void ResolveAtlas(byte[] ggmPristine)
        {
            var sf = new SerializedFile(ggmPristine);
            int end;
            var c = ReadContainer(sf.Raw(ResourceManager(sf)), out end);
            var e = c.FirstOrDefault(x => K(x) == "uiatlas");
            if (e == null) throw new ModException("资源路径表里找不到 uiatlas 图集");
            if (e.FileId < 1 || e.FileId > sf.Externals.Count) throw new ModException("uiatlas 引用异常");
            AtlasFile = Path.Combine(G.DataDir, sf.Externals[e.FileId - 1].Replace('/', Path.DirectorySeparatorChar));
            atlasPathId = e.PathId;
            if (!File.Exists(AtlasFile)) throw new ModException("找不到图集文件 " + AtlasFile);
        }

        // ---------------- UIAtlas : buildingicon_<id> ----------------
        class Atlas { public string Name; public int SpritesCountOff, SpritesEnd, NamesCountOff, NamesEnd; public List<long[]> Sprites = new List<long[]>(); public List<string> Names = new List<string>(); }

        static Atlas ReadAtlas(byte[] raw)
        {
            var a = new Atlas();
            int o = 0;
            a.Name = Bin.ReadAlignedString(raw, ref o);
            a.SpritesCountOff = o;
            int n1 = Bin.I32(raw, o); o += 4;
            for (int i = 0; i < n1; i++) { a.Sprites.Add(new long[] { Bin.I32(raw, o), Bin.I64(raw, o + 4) }); o += 12; }
            a.SpritesEnd = o;
            a.NamesCountOff = o;
            int n2 = Bin.I32(raw, o); o += 4;
            for (int i = 0; i < n2; i++) a.Names.Add(Bin.ReadAlignedString(raw, ref o));
            a.NamesEnd = o;
            if (n1 != n2) throw new ModException("图集结构异常");
            return a;
        }

        bool AtlasPatched(byte[] data)
        {
            var sf = new SerializedFile(data);
            var oi = sf.Find(atlasPathId);
            if (oi == null) throw new ModException("图集文件里找不到 UIAtlas");
            return ReadAtlas(sf.Raw(oi)).Names.Contains("buildingicon_" + NewId);
        }

        byte[] PatchAtlas(byte[] pristine)
        {
            var sf = new SerializedFile(pristine);
            var oi = sf.Find(atlasPathId);
            if (oi == null) throw new ModException("图集文件里找不到 UIAtlas");
            var raw = sf.Raw(oi);
            var a = ReadAtlas(raw);
            string src = "buildingicon_" + IconId, dst = "buildingicon_" + NewId;
            int idx = a.Names.IndexOf(src);
            if (idx < 0) throw new ModException("图集里找不到 " + src);
            if (a.Names.Contains(dst)) throw new ModException("图集里已经有 " + dst);
            var ms = new MemoryStream();
            ms.Write(raw, 0, a.SpritesCountOff);
            ms.Write(BitConverter.GetBytes(a.Sprites.Count + 1), 0, 4);
            ms.Write(raw, a.SpritesCountOff + 4, a.SpritesEnd - a.SpritesCountOff - 4);
            ms.Write(BitConverter.GetBytes((int)a.Sprites[idx][0]), 0, 4);
            ms.Write(BitConverter.GetBytes(a.Sprites[idx][1]), 0, 8);
            ms.Write(BitConverter.GetBytes(a.Names.Count + 1), 0, 4);
            ms.Write(raw, a.NamesCountOff + 4, a.NamesEnd - a.NamesCountOff - 4);
            var s = Bin.AlignedString(dst);
            ms.Write(s, 0, s.Length);
            ms.Write(raw, a.NamesEnd, raw.Length - a.NamesEnd);
            return sf.WithObjectReplaced(oi.PathId, ms.ToArray());
        }

        // ---------------- GameAssembly.dll : HeroData.HaveForceFunction ----------------
        static readonly byte[] DllSig = { 0x48, 0x8B, 0xCB, 0x32, 0xD2, 0xE8 };
        static readonly int[] CmpJne = { 0x39, 0xBB, -1, -1, -1, -1, 0x75, -1 };            // cmp [rbx+X],edi ; jne
        static readonly byte[] Epilogue = { 0x48, 0x8B, 0x5C, 0x24, 0x30, 0x48, 0x83, 0xC4, 0x20, 0x5F, 0xC3 };

        class DllTargets { public long HffOff; public ulong HffVa, GetForce, MainArea, FindBuilding; }

        DllTargets LocateDll(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            var pe = new PeImage(dll);
            var code = new Il2CppCode(pe, "Assembly-CSharp.dll");
            var t = new DllTargets();
            t.HffVa = code.MethodVa(m.FindMethod("HeroData", "HaveForceFunction", new[] { "forceID" }).Token);
            t.GetForce = code.MethodVa(m.FindMethod("HeroData", "GetForce", new[] { "includeServant" }).Token);
            t.MainArea = code.MethodVa(m.FindMethod("ForceData", "MainArea", new string[0]).Token);
            t.FindBuilding = code.MethodVa(m.FindMethod("AreaData", "FindBuilding", new[] { "buildingID" }).Token);
            t.HffOff = pe.VaToOff(t.HffVa);
            if (t.HffOff < 0) throw new ModException("HaveForceFunction 地址无效");
            return t;
        }

        static bool DllPatchedAt(byte[] dll, long off)
        {
            return Bin.IndexOf(dll, DllSig, (int)off, (int)off + 0x200) >= 0;
        }

        bool HffPatched(byte[] dll, byte[] meta) { return DllPatchedAt(dll, LocateDll(dll, meta).HffOff); }
        bool CustomSkillPatched(byte[] dll, byte[] meta)
        {
            long c = LocateCustomSkill(dll, meta);
            return c >= 0 && Bin.IndexOf(dll, CsSigNew, (int)c, (int)c + 0x200) >= 0;
        }

        // ---- 自创武学上限 ----
        // WorldData.HavePlayerCustomSkill(rareLv): originally "player already made a skill of this rarity" (=> one per rarity, 6 total).
        // Patched: counts the player's custom skills of that rarity and returns true once the count reaches MaxCustomSkills.
        public const int MaxCustomSkills = 9;
        //   cmp byte [rax+20h],0 ; je loop ; cmp [rcx+34h],ebx ; jne loop ; mov dil,1 ; mov [rsp+90h],dil
        static readonly int[] CsSigOld = { 0x80, 0x78, 0x20, 0x00, 0x74, -1, 0x39, 0x59, 0x34, 0x75, -1, 0x40, 0xB7, 0x01, 0x40, 0x88, 0xBC, 0x24, 0x90, 0x00, 0x00, 0x00 };
        static readonly byte[] CsSigNew = { 0x40, 0xFE, 0xC7, 0x40, 0x80, 0xFF };   // ... inc dil ; cmp dil,N

        long LocateCustomSkill(byte[] dll, byte[] meta)
        {
            var m = new Il2CppMeta(meta);
            Il2CppMeta.MethodRef mr;
            try { mr = m.FindMethod("WorldData", "HavePlayerCustomSkill", new[] { "rareLv" }); }
            catch (ModException) { return -1; }   // game version without custom skills
            var pe = new PeImage(dll);
            var code = new Il2CppCode(pe, "Assembly-CSharp.dll");
            return pe.VaToOff(code.MethodVa(mr.Token));
        }

        byte[] PatchCustomSkill(byte[] dll, byte[] meta)
        {
            long f = LocateCustomSkill(dll, meta);
            if (f < 0) throw new ModException("当前游戏版本没有自创武学功能");
            int s = Bin.IndexOfPattern(dll, CsSigOld, (int)f, (int)f + 0x200);
            if (s < 0) throw new ModException("HavePlayerCustomSkill 结构与预期不同");
            if (Bin.IndexOfPattern(dll, CsSigOld, s + 1, (int)f + 0x200) >= 0) throw new ModException("HavePlayerCustomSkill 判定指令不唯一");
            int jne = s + 9;
            int loopTarget = jne + 2 + (sbyte)dll[jne + 1];
            var outb = (byte[])dll.Clone();
            int w = s + 6;                                           // overwrite 16 bytes from "cmp [rcx+34h],ebx"
            // cmp [rcx+34h],ebx ; jne loop ; inc dil ; cmp dil,N ; jb loop
            var c = new List<byte> { 0x39, 0x59, 0x34, 0x75, (byte)(sbyte)(loopTarget - (w + 5)), 0x40, 0xFE, 0xC7, 0x40, 0x80, 0xFF, (byte)MaxCustomSkills, 0x72 };
            int rel = loopTarget - (w + c.Count + 1);
            if (rel < -128 || rel > 127) throw new ModException("HavePlayerCustomSkill 跳转距离异常");
            c.Add((byte)(sbyte)rel);
            while (c.Count < 16) c.Add(0x90);
            for (int i = 0; i < 16; i++) outb[w + i] = c[i];
            return outb;
        }

        byte[] PatchDll(byte[] pristine, byte[] meta)
        {
            if (SpecialBase < 0 || SpecialBase > 127) throw new ModException("特殊建筑编号规则异常");
            var t = LocateDll(pristine, meta);
            int f = (int)t.HffOff;
            if (DllPatchedAt(pristine, f)) throw new ModException("DLL 里已经有这个修改");
            // prologue: mov edi,edx ; mov rbx,rcx
            if (Bin.IndexOf(pristine, new byte[] { 0x8B, 0xFA }, f, f + 0x30) < 0 || Bin.IndexOf(pristine, new byte[] { 0x48, 0x8B, 0xD9 }, f, f + 0x30) < 0)
                throw new ModException("HaveForceFunction 开头结构与预期不同");
            int s = Bin.IndexOfPattern(pristine, CmpJne, f, f + 0x200);
            if (s < 0) throw new ModException("HaveForceFunction 里找不到门派判定指令");
            if (Bin.IndexOfPattern(pristine, CmpJne, s + 1, f + 0x200) >= 0) throw new ModException("HaveForceFunction 里门派判定指令不唯一");
            int e = Bin.IndexOf(pristine, Epilogue, s, f + 0x200);
            if (e < 0) throw new ModException("HaveForceFunction 里找不到返回指令");
            const int codeLen = 47;
            if (e - s < codeLen || e - (s + codeLen) > 127) throw new ModException("HaveForceFunction 可改写空间不符");

            var c = new List<byte>();
            Action<byte[]> put = bs => c.AddRange(bs);
            Action<ulong> call = target =>
            {
                ulong at = t.HffVa + (ulong)(s + c.Count - f);
                long rel = (long)target - (long)(at + 5);
                if (rel < int.MinValue || rel > int.MaxValue) throw new ModException("调用距离超出范围");
                c.Add(0xE8); c.AddRange(BitConverter.GetBytes((int)rel));
            };
            put(new byte[] { 0x48, 0x8B, 0xCB });              // mov rcx, rbx        (HeroData)
            put(new byte[] { 0x32, 0xD2 });                    // xor dl, dl          (includeServant=false)
            call(t.GetForce);                                  // -> ForceData
            put(new byte[] { 0x48, 0x85, 0xC0, 0x74, 0x1B });  // test rax,rax ; je setne
            put(new byte[] { 0x48, 0x8B, 0xC8 });              // mov rcx, rax
            call(t.MainArea);                                  // -> AreaData
            put(new byte[] { 0x48, 0x85, 0xC0, 0x74, 0x0E });  // test rax,rax ; je setne
            put(new byte[] { 0x48, 0x8B, 0xC8 });              // mov rcx, rax
            put(new byte[] { 0x8D, 0x57, (byte)SpecialBase }); // lea edx,[rdi+base]  (building id)
            call(t.FindBuilding);                              // -> building or null
            put(new byte[] { 0x48, 0x85, 0xC0 });              // test rax, rax
            put(new byte[] { 0x0F, 0x95, 0xC0 });              // setne al
            c.Add(0xEB); c.Add((byte)(e - (s + codeLen)));     // jmp epilogue
            if (c.Count != codeLen) throw new ModException("内部错误：代码长度");
            var outb = (byte[])pristine.Clone();
            for (int i = s; i < e; i++) outb[i] = 0x90;
            for (int i = 0; i < c.Count; i++) outb[s + i] = c[i];
            return outb;
        }

        // ---------------- orchestration (independent, selectable features) ----------------
        public const string FHff = "hff", FCustom = "custom", FBook = "book", FHhd = "hhd", FTag = "tag";

        public class FileState
        {
            public string Path;
            public byte[] Current, Pristine;
            public bool PristineIsCurrent;
        }

        public class Feature
        {
            public string Key, Title, Desc;
            public string[] Files;          // file names (for display)
            public bool Installed;          // present in the current files
            public bool NeedsUpdate;        // installed, but the files differ from what this version would write
            public string Error;            // non-null => incompatible with this game version
            public bool Compatible { get { return Error == null; } }
            public string StateText
            {
                get
                {
                    if (!Compatible) return (Installed ? "已安装，但" : "") + "不兼容：" + Error;
                    if (Installed) return NeedsUpdate ? "已安装，可更新" : "已安装";
                    return "未安装";
                }
            }
        }

        public class Plan
        {
            public List<Feature> Features = new List<Feature>();
            public Dictionary<string, FileState> Files = new Dictionary<string, FileState>();
            public Func<HashSet<string>, Dictionary<string, byte[]>> Build;
            public Feature this[string key] { get { return Features.First(f => f.Key == key); } }
            public HashSet<string> InstalledKeys { get { return new HashSet<string>(Features.Where(f => f.Installed).Select(f => f.Key)); } }
            public bool AnyModified { get { return Files.Values.Any(f => !f.PristineIsCurrent); } }
        }

        FileState LoadFile(string path, Func<byte[], bool> modified)
        {
            var fs = new FileState { Path = path, Current = ReadAll(path) };
            if (!modified(fs.Current)) { fs.Pristine = fs.Current; fs.PristineIsCurrent = true; return fs; }
            string b = Bak(path);
            if (!File.Exists(b)) throw new ModException(Path.GetFileName(path) + " 已被修改，但找不到原版备份 " + Path.GetFileName(b) + "。请用 Steam「验证游戏文件完整性」恢复后重试。");
            var bb = ReadAll(b);
            if (modified(bb)) throw new ModException(Path.GetFileName(b) + " 也是修改过的，无法确定原版。请用 Steam 验证完整性。");
            fs.Pristine = bb; fs.PristineIsCurrent = false;
            return fs;
        }

        static bool Safe(Func<bool> f) { try { return f(); } catch (ModException) { return false; } }

        public Plan Analyze()
        {
            Log.Clear();
            if (!G.Looks()) throw new ModException("这个目录不是龙胤立志传的游戏目录（找不到 GameAssembly.dll / resources.assets 等文件）");
            var meta = ReadAll(G.Meta);
            var plan = new Plan();

            Func<byte[], bool> hffOn = d => Safe(() => HffPatched(d, meta));
            Func<byte[], bool> csOn = d => Safe(() => CustomSkillPatched(d, meta));
            Func<byte[], bool> bookOn = d => Safe(() => BookCountPatched(d));
            var dll = LoadFile(G.Dll, d => hffOn(d) || csOn(d) || bookOn(d));
            var res = LoadFile(G.Resources, d => HasBuilding(d) || HasTag(d));
            var ggm = LoadFile(G.Ggm, GgmPatched);
            ResolveAtlas(ggm.Pristine);

            // ids come from the pristine tables and must be known before checking the atlas (buildingicon_<NewId>)
            var t = LoadBuildingTable(res.Pristine);
            var look = t.RowByName(Hhd.LookAlike);
            if (look == null) throw new ModException("建筑表里找不到「" + Hhd.LookAlike + "」");
            IconId = t.Id(look); NewId = t.MaxId() + 1; SpecialBase = t.SpecialBase();

            var atl = LoadFile(AtlasFile, AtlasPatched);
            foreach (var f in new[] { dll, res, ggm, atl }) plan.Files[f.Path] = f;

            string nDll = Path.GetFileName(G.Dll), nRes = Path.GetFileName(G.Resources), nGgm = Path.GetFileName(G.Ggm), nAtl = Path.GetFileName(AtlasFile);
            plan.Features.Add(new Feature { Key = FHff, Title = "门派特性随特殊建筑", Files = new[] { nDll },
                Desc = "驻地有哪派的特殊建筑，就获得那派的被动特性", Installed = hffOn(dll.Current) });
            plan.Features.Add(new Feature { Key = FCustom, Title = "自创武学每品级" + MaxCustomSkills + "门", Files = new[] { nDll },
                Desc = "原版每个品级只能自创 1 门", Installed = csOn(dll.Current) });
            plan.Features.Add(new Feature { Key = FBook, Title = "秘籍持有数量提示", Files = new[] { nDll },
                Desc = "悬停秘籍时多一个框，显示背包 / 仓库里的数量", Installed = bookOn(dll.Current) });
            plan.Features.Add(new Feature { Key = FHhd, Title = "合欢殿", Files = new[] { nRes, nGgm, nAtl },
                Desc = "新建筑（借用金顶外观），自立门户后可建造", Installed = HasBuilding(res.Current) || GgmPatched(ggm.Current) || AtlasPatched(atl.Current) });
            plan.Features.Add(new Feature { Key = FTag, Title = "合欢宗主天赋", Files = new[] { nRes },
                Desc = "新天赋，效果在「合欢宗主天赋」页设置", Installed = HasTag(res.Current) });

            plan.Build = sel =>
            {
                var o = new Dictionary<string, byte[]>();
                var d = dll.Pristine;
                if (sel.Contains(FHff)) d = PatchDll(d, meta);
                if (sel.Contains(FCustom)) d = PatchCustomSkill(d, meta);
                if (sel.Contains(FBook)) d = PatchBookCount(d, meta);
                o[dll.Path] = d;
                var r = res.Pristine;
                if (sel.Contains(FHhd)) r = PatchBuilding(r);
                if (sel.Contains(FTag)) r = PatchTag(r);
                o[res.Path] = r;
                o[ggm.Path] = sel.Contains(FHhd) ? PatchGgm(ggm.Pristine) : ggm.Pristine;
                o[atl.Path] = sel.Contains(FHhd) ? PatchAtlas(atl.Pristine) : atl.Pristine;
                return o;
            };

            // compatibility: each feature alone on the pristine files
            foreach (var f in plan.Features)
            {
                try { plan.Build(new HashSet<string> { f.Key }); }
                catch (ModException ex) { f.Error = ex.Message; }
            }
            // up to date? rebuild the currently installed set and compare
            var inst = new HashSet<string>(plan.Features.Where(f => f.Installed && f.Compatible).Select(f => f.Key));
            try
            {
                var want = plan.Build(inst);
                foreach (var f in plan.Features.Where(x => x.Installed && x.Compatible))
                {
                    var paths = plan.Files.Values.Where(fs => f.Files.Contains(Path.GetFileName(fs.Path))).Select(fs => fs.Path);
                    f.NeedsUpdate = paths.Any(pth => !Bin.Equal(want[pth], plan.Files[pth].Current));
                }
            }
            catch (ModException) { }
            return plan;
        }

        // writes the files so that exactly the selected features are installed (unselected ones are removed)
        public static string Apply(Plan plan, HashSet<string> sel)
        {
            if (GameFiles.GameRunning()) throw new ModException("游戏正在运行，请先关闭游戏。");
            var bad = plan.Features.Where(f => sel.Contains(f.Key) && !f.Compatible).ToList();
            if (bad.Count > 0) throw new ModException("所选功能与当前游戏版本不兼容，未做任何修改：\n" + string.Join("\n", bad.Select(b => "· " + b.Title + "：" + b.Error)));
            var want = plan.Build(sel);
            var sb = new StringBuilder();
            foreach (var fs in plan.Files.Values)
                if (fs.PristineIsCurrent && !Bin.Equal(want[fs.Path], fs.Pristine))
                {
                    File.WriteAllBytes(Bak(fs.Path), fs.Pristine);
                    sb.AppendLine("已备份原版 " + Path.GetFileName(fs.Path) + ".bak");
                }
            foreach (var fs in plan.Files.Values)
            {
                var data = want[fs.Path];
                if (Bin.Equal(data, fs.Current)) continue;
                string tmp = fs.Path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (Bin.Md5(File.ReadAllBytes(tmp)) != Bin.Md5(data)) { File.Delete(tmp); throw new ModException("写入校验失败：" + fs.Path); }
                File.Copy(tmp, fs.Path, true);
                File.Delete(tmp);
                sb.AppendLine("已写入 " + Path.GetFileName(fs.Path) + (Bin.Equal(data, fs.Pristine) ? "（恢复为原版）" : ""));
            }
            foreach (var f in plan.Features)
            {
                bool on = sel.Contains(f.Key);
                if (on && !f.Installed) sb.AppendLine(f.Title + "：已安装");
                else if (on && f.NeedsUpdate) sb.AppendLine(f.Title + "：已更新");
                else if (!on && f.Installed) sb.AppendLine(f.Title + "：已卸载");
            }
            if (sb.Length == 0) sb.AppendLine("没有需要改动的文件。");
            return sb.ToString();
        }
    }
}
