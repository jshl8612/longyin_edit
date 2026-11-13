// LongYinMod - save editing. The save is JSON; we parse it into a tree that remembers byte spans
// and splice only the values we change, so everything else stays byte-identical.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LongYinMod
{
    public class JNode
    {
        public char Kind;            // o a s n t f z (object array string number true false null)
        public int Start, End;       // byte span [Start, End)
        public List<KeyValuePair<string, JNode>> Obj;
        public List<JNode> Arr;
        public string Str;           // decoded string or raw number text

        public JNode this[string k]
        {
            get
            {
                if (Obj == null) return null;
                foreach (var kv in Obj) if (kv.Key == k) return kv.Value;
                return null;
            }
        }
        public string S { get { return Kind == 's' ? Str : null; } }
        public bool B { get { return Kind == 't'; } }
        public double N { get { return Kind == 'n' ? double.Parse(Str, CultureInfo.InvariantCulture) : double.NaN; } }
        public int I { get { return (int)N; } }
    }

    public class JParser
    {
        byte[] b; int p;
        public static JNode Parse(byte[] data)
        {
            var j = new JParser { b = data, p = 0 };
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) j.p = 3;
            var n = j.Value();
            j.Ws();
            if (j.p != data.Length) throw new ModException("存档 JSON 末尾有多余内容");
            return n;
        }
        // parse only the first element of a top-level array (the Hero file is ~30MB; the player is element 0)
        public static JNode ParseFirstElement(byte[] data)
        {
            var j = new JParser { b = data, p = 0 };
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) j.p = 3;
            j.Ws();
            if (j.b[j.p] != '[') throw new ModException("Hero 文件格式不对（不是数组）");
            j.p++;
            return j.Value();
        }
        void Ws() { while (p < b.Length && (b[p] == ' ' || b[p] == '\n' || b[p] == '\r' || b[p] == '\t')) p++; }
        JNode Value()
        {
            Ws();
            if (p >= b.Length) throw new ModException("存档 JSON 意外结束");
            var n = new JNode { Start = p };
            byte c = b[p];
            if (c == '{')
            {
                n.Kind = 'o'; n.Obj = new List<KeyValuePair<string, JNode>>(); p++; Ws();
                if (b[p] == '}') { p++; }
                else while (true)
                    {
                        Ws(); string k = StrLit(); Ws();
                        if (b[p] != ':') throw new ModException("存档 JSON 格式错误 @" + p); p++;
                        n.Obj.Add(new KeyValuePair<string, JNode>(k, Value())); Ws();
                        if (b[p] == ',') { p++; continue; }
                        if (b[p] == '}') { p++; break; }
                        throw new ModException("存档 JSON 格式错误 @" + p);
                    }
            }
            else if (c == '[')
            {
                n.Kind = 'a'; n.Arr = new List<JNode>(); p++; Ws();
                if (b[p] == ']') { p++; }
                else while (true)
                    {
                        n.Arr.Add(Value()); Ws();
                        if (b[p] == ',') { p++; continue; }
                        if (b[p] == ']') { p++; break; }
                        throw new ModException("存档 JSON 格式错误 @" + p);
                    }
            }
            else if (c == '"') { n.Kind = 's'; n.Str = StrLit(); }
            else if (c == 't') { n.Kind = 't'; Expect("true"); }
            else if (c == 'f') { n.Kind = 'f'; Expect("false"); }
            else if (c == 'n') { n.Kind = 'z'; Expect("null"); }
            else
            {
                n.Kind = 'n'; int s = p;
                while (p < b.Length && "+-0123456789.eE".IndexOf((char)b[p]) >= 0) p++;
                if (p == s) throw new ModException("存档 JSON 格式错误 @" + p);
                n.Str = Encoding.ASCII.GetString(b, s, p - s);
            }
            n.End = p;
            return n;
        }
        void Expect(string w)
        {
            for (int i = 0; i < w.Length; i++) if (b[p + i] != w[i]) throw new ModException("存档 JSON 格式错误 @" + p);
            p += w.Length;
        }
        string StrLit()
        {
            if (b[p] != '"') throw new ModException("存档 JSON 格式错误 @" + p);
            p++;
            int s = p;
            bool esc = false;
            while (b[p] != '"') { if (b[p] == '\\') { esc = true; p++; } p++; }
            string raw = Encoding.UTF8.GetString(b, s, p - s);
            p++;
            if (!esc) return raw;
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch != '\\') { sb.Append(ch); continue; }
                char e = raw[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }

    // Save files: plain JSON (old saves) or gzip-compressed JSON (game 2026-11 and later).
    // We edit the JSON and write it back in the same container format.
    public static class SaveIO
    {
        public static bool IsGzip(byte[] raw) { return raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B; }

        public static byte[] Decode(byte[] raw)
        {
            if (!IsGzip(raw)) return raw;
            using (var src = new MemoryStream(raw))
            using (var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                gz.CopyTo(dst);
                return dst.ToArray();
            }
        }

        // like = the original on-disk bytes: same container (gzip or not) and, for gzip, the same header bytes
        public static byte[] Encode(byte[] json, byte[] like)
        {
            if (!IsGzip(like)) return json;
            byte[] r;
            using (var dst = new MemoryStream())
            {
                using (var gz = new System.IO.Compression.GZipStream(dst, System.IO.Compression.CompressionMode.Compress, true))
                    gz.Write(json, 0, json.Length);
                r = dst.ToArray();
            }
            // header: copy MTIME/XFL/OS from the game's file (not covered by the CRC)
            if (r.Length > 10 && like.Length > 10 && (r[3] & 0x1E) == 0 && (like[3] & 0x1E) == 0)
                for (int i = 4; i < 10; i++) r[i] = like[i];
            if (!Bin.Equal(Decode(r), json)) throw new ModException("压缩校验失败");
            return r;
        }
    }

    public class SlotInfo
    {
        public int Index;
        public string Dir;
        public string Character = "", GameDate = "", Mode = "", ForceLine = "", Difficulty = "", SaveTime = "", Version = "";
        public string ForceName = "", Title = "";
        public DateTime SaveMtime;
        public bool HasSave;
    }

    public class ForceInfo
    {
        public int Index, ForceId, MainAreaId;
        public string Name, SetName;
        public bool Replaced;
        public double MaleRate;
        public string Display { get { return string.IsNullOrEmpty(SetName) ? Name : SetName; } }
    }

    public class SaveDoc
    {
        public string Path;
        public byte[] Raw;           // bytes on disk (maybe gzip)
        public byte[] Data;          // JSON
        public JNode Root;
        public List<ForceInfo> Forces = new List<ForceInfo>();
        public List<int> Unlocked = new List<int>();
        public ForceInfo Player;
        public string PlayerTitle = "";

        public static SaveDoc Load(string path, SlotInfo slot)
        {
            var d = new SaveDoc { Path = path, Raw = File.ReadAllBytes(path) };
            d.Data = SaveIO.Decode(d.Raw);
            d.Root = JParser.Parse(d.Data);
            var forces = d.Root["Forces"];
            if (forces == null || forces.Kind != 'a') throw new ModException("存档里没有门派列表 Forces");
            for (int i = 0; i < forces.Arr.Count; i++)
            {
                var f = forces.Arr[i];
                d.Forces.Add(new ForceInfo
                {
                    Index = i,
                    ForceId = f["forceID"].I,
                    Name = f["forceName"].S,
                    SetName = f["forceSetName"] != null ? f["forceSetName"].S : "",
                    Replaced = f["replacedForce"] != null && f["replacedForce"].B,
                    MainAreaId = f["mainAreaID"] != null ? f["mainAreaID"].I : -1,
                    MaleRate = f["forceMaleRate"] != null ? f["forceMaleRate"].N : double.NaN,
                });
            }
            var u = d.Root["speBuildingUnlocked"];
            if (u == null || u.Kind != 'a') throw new ModException("存档里没有特殊建筑解锁记录 speBuildingUnlocked");
            foreach (var x in u.Arr) d.Unlocked.Add(x.I);
            // player force: from the slot description "门派名<color=..>职位</color>"
            if (slot != null && !string.IsNullOrEmpty(slot.ForceName))
            {
                d.PlayerTitle = slot.Title;
                d.Player = d.Forces.FirstOrDefault(f => f.SetName == slot.ForceName) ?? d.Forces.FirstOrDefault(f => f.Name == slot.ForceName);
            }
            return d;
        }

        // edits: list of (span, replacement bytes)
        public byte[] Apply(bool unlock, int unlockId, bool femaleOnly)
        {
            var edits = new List<Tuple<int, int, byte[]>>();
            if (unlock && !Unlocked.Contains(unlockId))
            {
                var u = Root["speBuildingUnlocked"];
                var vals = new List<int>(Unlocked) { unlockId };
                edits.Add(Tuple.Create(u.Start, u.End, Encoding.ASCII.GetBytes("[" + string.Join(",", vals) + "]")));
            }
            if (femaleOnly)
            {
                if (Player == null) throw new ModException("没有识别到玩家门派");
                var mr = Root["Forces"].Arr[Player.Index]["forceMaleRate"];
                if (mr == null || mr.Kind != 'n') throw new ModException("存档里找不到门派男性比例");
                if (mr.N != 0) edits.Add(Tuple.Create(mr.Start, mr.End, Encoding.ASCII.GetBytes("0.0")));
            }
            if (edits.Count == 0) return null;
            edits.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            var ms = new MemoryStream();
            int cur = Data.Length;
            var chunks = new List<byte[]>();
            // rebuild from the back
            var outList = new List<byte[]>();
            int end = Data.Length;
            foreach (var e in edits)
            {
                outList.Add(Slice(Data, e.Item2, end));
                outList.Add(e.Item3);
                end = e.Item1;
            }
            outList.Add(Slice(Data, 0, end));
            outList.Reverse();
            foreach (var c in outList) ms.Write(c, 0, c.Length);
            return ms.ToArray();
        }

        static byte[] Slice(byte[] a, int s, int e)
        {
            var r = new byte[e - s];
            Array.Copy(a, s, r, 0, e - s);
            return r;
        }
    }

    public class HeroDoc
    {
        public string Path;
        public byte[] Raw;           // bytes on disk (maybe gzip)
        public byte[] Data;          // JSON
        public JNode Player;
        public string Name = "";
        public List<int> Tags = new List<int>();

        public static HeroDoc Load(string path)
        {
            var d = new HeroDoc { Path = path, Raw = File.ReadAllBytes(path) };
            d.Data = SaveIO.Decode(d.Raw);
            d.Player = JParser.ParseFirstElement(d.Data);
            if (d.Player.Kind != 'o' || d.Player["heroID"] == null || d.Player["heroID"].I != 0) throw new ModException("Hero 文件第一个角色不是主角（heroID 0）");
            var nm = d.Player["heroName"];
            d.Name = nm != null && nm.S != null ? nm.S : "";
            var t = d.Player["heroTagData"];
            if (t == null || t.Kind != 'a') throw new ModException("主角数据里没有天赋列表 heroTagData");
            foreach (var x in t.Arr) d.Tags.Add(x["tagID"].I);
            return d;
        }

        public byte[] AddTag(int tagId)
        {
            if (Tags.Contains(tagId)) return null;
            var t = Player["heroTagData"];
            // newer saves omit null fields ("sourceHero":null); follow whatever the existing entries do
            bool withNull = t.Arr.Count > 0 ? t.Arr[0]["sourceHero"] != null : !SaveIO.IsGzip(Raw);
            string item = "{\"tagID\":" + tagId + ",\"leftTime\":-1.0" + (withNull ? ",\"sourceHero\":null" : "") + "}";
            int at = t.End - 1;   // position of ']'
            var ins = Encoding.ASCII.GetBytes((t.Arr.Count > 0 ? "," : "") + item);
            var r = new byte[Data.Length + ins.Length];
            Array.Copy(Data, 0, r, 0, at);
            Array.Copy(ins, 0, r, at, ins.Length);
            Array.Copy(Data, at, r, at + ins.Length, Data.Length - at);
            // verify
            var chk = JParser.ParseFirstElement(r);
            var tt = chk["heroTagData"];
            if (tt == null || tt.Arr.Count != t.Arr.Count + 1 || tt.Arr[tt.Arr.Count - 1]["tagID"].I != tagId) throw new ModException("添加天赋后校验失败");
            return r;
        }
    }

    public static class Saves
    {
        public static string SaveRoot(GameFiles g) { return Path.Combine(g.DataDir, "Save"); }

        public static List<SlotInfo> List(GameFiles g)
        {
            var l = new List<SlotInfo>();
            string root = SaveRoot(g);
            if (!Directory.Exists(root)) return l;
            for (int i = 0; i <= 10; i++)
            {
                string dir = Path.Combine(root, "SaveSlot" + i);
                var s = new SlotInfo { Index = i, Dir = dir };
                string save = Path.Combine(dir, "Save"), info = Path.Combine(dir, "Info");
                s.HasSave = File.Exists(save);
                if (!s.HasSave) continue;
                s.SaveMtime = File.GetLastWriteTime(save);
                if (File.Exists(info))
                {
                    try
                    {
                        var j = JParser.Parse(SaveIO.Decode(File.ReadAllBytes(info)));
                        s.Version = j["SaveVersion"] != null ? j["SaveVersion"].S : "";
                        s.SaveTime = j["SaveTime"] != null ? j["SaveTime"].S : "";
                        var det = (j["SaveDetail"] != null ? j["SaveDetail"].S : "").Split('\n');
                        if (det.Length > 0) s.Character = det[0];
                        if (det.Length > 1) s.GameDate = det[1];
                        if (det.Length > 2) s.Mode = det[2];
                        if (det.Length > 3) s.ForceLine = det[3];
                        if (det.Length > 4) s.Difficulty = det[4];
                        var m = Regex.Match(s.ForceLine, @"^(.*?)<color=[^>]*>(.*?)</color>");
                        if (m.Success) { s.ForceName = m.Groups[1].Value; s.Title = m.Groups[2].Value; }
                        else s.ForceName = "";
                    }
                    catch { }
                }
                l.Add(s);
            }
            return l;
        }

        public static string StripTags(string s) { return Regex.Replace(s ?? "", "<[^>]*>", ""); }

        // The building id of HeHuanDian in the INSTALLED game files (null if not installed)
        public static int? InstalledHhdId(GameFiles g)
        {
            try
            {
                var t = GamePatcher.LoadBuildingTable(File.ReadAllBytes(g.Resources));
                var r = t.RowByName(Hhd.Name);
                return r == null ? (int?)null : t.Id(r);
            }
            catch { return null; }
        }

        public static int? InstalledTagId(GameFiles g)
        {
            try
            {
                var t = GamePatcher.LoadTagTable(File.ReadAllBytes(g.Resources));
                var r = t.RowByName(Hhzz.Name);
                return r == null ? (int?)null : t.Id(r);
            }
            catch { return null; }
        }

        public static string Write(SaveDoc doc, byte[] newData)
        {
            // verify the result parses and the edits are in place
            var check = JParser.Parse(newData);
            if (check["Forces"] == null || check["Forces"].Arr.Count != doc.Forces.Count) throw new ModException("修改后的存档校验失败，未写入");
            return WriteFile(doc.Path, doc.Raw, SaveIO.Encode(newData, doc.Raw));
        }

        public static void CheckUnchanged(string path, byte[] loaded)
        {
            if (!Bin.Equal(File.ReadAllBytes(path), loaded)) throw new ModException(System.IO.Path.GetFileName(path) + " 在读取之后被改动过（可能是游戏或 Steam 云同步写入了）。请重新读取后再试。");
        }

        public static string WriteFile(string path, byte[] loaded, byte[] newData)
        {
            CheckUnchanged(path, loaded);
            string bak = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
            File.WriteAllBytes(bak, loaded);
            File.WriteAllBytes(path, newData);
            if (!Bin.Equal(File.ReadAllBytes(path), newData)) throw new ModException("写入校验失败：" + path);
            return bak;
        }
    }
}
