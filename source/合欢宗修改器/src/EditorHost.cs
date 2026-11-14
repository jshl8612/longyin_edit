// 龙胤立志传存档修改器.exe：把网页版存档修改器包成桌面程序
//   · 放在游戏根目录运行；游戏数据（武功、天赋、门派、地点、人物、建筑名、属性）运行时从 resources.assets 读取
//   · 内置只监听 127.0.0.1 的本地服务提供页面和存档读写，用 Edge（或 Chrome）的应用窗口打开
//   · 窗口关闭后程序自动退出
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LongYinMod
{
    public static class EditorHost
    {
        static string gameDir, saveDir, token, page;
        static DateTime lastPing = DateTime.MinValue, started = DateTime.Now, byeAt = DateTime.MinValue;
        static volatile bool quit;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                gameDir = FindGame(args);
                if (gameDir == null) return 1;
                saveDir = Path.Combine(gameDir, "LongYinLiZhiZhuan_Data", "Save");
                if (!Directory.Exists(saveDir)) throw new ModException("找不到存档文件夹：" + saveDir);

                string dict, bnames;
                BuildGameDict(new GameFiles(gameDir).Resources, out dict, out bnames);
                if (args.Contains("--dumpdict")) { File.WriteAllText("game_dict.gen.json", dict); File.WriteAllText("bnames.gen.json", bnames); return 0; }

                token = Guid.NewGuid().ToString("N");
                string tpl;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("editor.html"))
                using (var r = new StreamReader(s, Encoding.UTF8)) tpl = r.ReadToEnd();
                if (tpl.IndexOf("__GAME_DICT__") < 0 || tpl.IndexOf("__BNAMES__") < 0) throw new ModException("内置页面模板损坏");
                var host = "<script>window.__HOST__=" + "{\"token\":" + Js(token) + ",\"game\":" + Js(gameDir) + "};</script>";
                int at = tpl.IndexOf("<script");
                page = tpl.Substring(0, at) + host + tpl.Substring(at);
                page = page.Replace("__GAME_DICT__", dict).Replace("__BNAMES__", bnames);

                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                new Thread(() => Accept(listener)) { IsBackground = true }.Start();
                string url = "http://127.0.0.1:" + port + "/?t=" + token;
                if (args.Contains("--nobrowser")) Console.WriteLine(url); else OpenWindow(url);

                // 等待退出：
                //   · 关窗口时页面发 /api/bye，之后 8 秒内没有新请求（刷新页面会马上重新连上）就退出；
                //   · 页面在后台时浏览器会放慢 / 暂停定时器，心跳可能几分钟才来一次，所以不能按心跳超时退出，
                //     只在 2 小时完全没有动静时退出（浏览器异常关闭时的兜底）；
                //   · 启动后 3 分钟内一直没连上也退出。
                while (!quit)
                {
                    Thread.Sleep(1000);
                    var now = DateTime.Now;
                    if (lastPing == DateTime.MinValue) { if ((now - started).TotalMinutes > 3) break; }
                    else if (byeAt > lastPing && (now - byeAt).TotalSeconds > 8) break;
                    else if ((now - lastPing).TotalHours > 2) break;
                }
                listener.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                try { MessageBox.Show(ex.Message, "龙胤立志传存档修改器", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                return 1;
            }
        }

        // ---------------- 游戏目录 ----------------
        static string FindGame(string[] args)
        {
            var given = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (given != null && new GameFiles(given).Looks()) return given;
            var here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (new GameFiles(here).Looks()) return here;
            var d = Settings.LoadDir() ?? SteamLocator.Find();
            if (d != null) return d;
            using (var dlg = new FolderBrowserDialog { Description = "请选择龙胤立志传的游戏目录（含 LongYinLiZhiZhuan_Data 的文件夹）。建议把本程序直接放在游戏目录里。" })
            {
                while (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (new GameFiles(dlg.SelectedPath).Looks()) return dlg.SelectedPath;
                    MessageBox.Show("这个文件夹不是游戏目录。", "龙胤立志传存档修改器");
                }
            }
            return null;
        }

        // ---------------- 从游戏文件生成数据字典（格式与网页版的 game_dict.json 相同）----------------
        static void BuildGameDict(string resources, out string dict, out string bnames)
        {
            var sf = new SerializedFile(File.ReadAllBytes(resources));
            Func<string, List<Dictionary<string, string>>> table = name =>
            {
                int ss, sl;
                var text = GamePatcher.ReadTextAsset(sf.Raw(GamePatcher.FindTextAsset(sf, name)), out ss, out sl);
                var rows = Csv(text);
                var head = rows[0];
                var l = new List<Dictionary<string, string>>();
                foreach (var r in rows.Skip(1))
                {
                    if (r.Count == 0 || r[0].Trim() == "") continue;
                    var d = new Dictionary<string, string>();
                    for (int i = 0; i < head.Count; i++) d[head[i]] = i < r.Count ? r[i] : "";
                    l.Add(d);
                }
                return l;
            };
            Func<Dictionary<string, string>, string, string> col = (r, prefix) =>
            {
                foreach (var kv in r) if (kv.Key.StartsWith(prefix)) return kv.Value;
                throw new ModException("数据表缺少列「" + prefix + "」，游戏版本可能不兼容");
            };

            var sb = new StringBuilder("{");
            // FORCE
            var force = new Dictionary<string, string>();
            foreach (var r in table("ForceData")) force[col(r, "id")] = col(r, "名字");
            force["-1"] = "江湖";
            sb.Append("\"FORCE\":{").Append(string.Join(",", force.Select(kv => Js(kv.Key) + ":" + Js(kv.Value)))).Append("},");
            // KF
            sb.Append("\"KF\":{");
            sb.Append(string.Join(",", table("KungFuData").Select(r =>
            {
                string f; if (!force.TryGetValue(col(r, "所属势力"), out f)) f = "江湖";
                int lv; int.TryParse(col(r, "等级"), out lv);
                var e = "{\"n\":" + Js(col(r, "名字")) + ",\"c\":" + Js(col(r, "类别")) + ",\"l\":" + lv + ",\"f\":" + Js(f);
                if (col(r, "运功效果") != "") e += ",\"eq\":" + Js(col(r, "运功效果"));
                if (col(r, "使用特效") != "") e += ",\"us\":" + Js(col(r, "使用特效"));
                return Js(col(r, "id")) + ":" + e + "}";
            })));
            sb.Append("},");
            // AREA
            sb.Append("\"AREA\":{").Append(string.Join(",", table("AreaData").Select(r => Js(col(r, "id")) + ":" + Js(col(r, "名字"))))).Append("},");
            // HERO（特殊人物）
            sb.Append("\"HERO\":{").Append(string.Join(",", table("SpeHeroData").Select(r =>
            {
                var e = "{\"n\":" + Js(col(r, "名字").Replace(".", ""));
                if (col(r, "绰号") != "") e += ",\"k\":" + Js(col(r, "绰号"));
                return Js(col(r, "id")) + ":" + e + "}";
            }))).Append("},");
            // TAG（天赋）
            sb.Append("\"TAG\":{").Append(string.Join(",", table("HeroTagData").Select(r =>
            {
                var e = "{\"n\":" + Js(col(r, "名称")) + ",\"c\":" + Js(col(r, "大类")) + ",\"e\":" + Js(col(r, "效果"));
                if (col(r, "领悟需求") != "") e += ",\"r\":" + Js(col(r, "领悟需求"));
                return Js(col(r, "编号")) + ":" + e + "}";
            }))).Append("},");
            // ATTR（属性 / 特效）
            sb.Append("\"ATTR\":{").Append(string.Join(",", table("SpeAddDataBase").Select(r =>
            {
                var p = col(r, "百分比显示");
                return Js(col(r, "序号")) + ":{\"n\":" + Js(col(r, "特效")) + ",\"p\":" + (p == "1" || p.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? 1 : 0) + "}";
            }))).Append("}");
            sb.Append("}");
            dict = sb.ToString();
            bnames = "{" + string.Join(",", table("BuildingData").Select(r => Js(col(r, "id")) + ":" + Js(col(r, "名字")))) + "}";
        }

        static List<List<string>> Csv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cur = new StringBuilder();
            bool q = false;
            int i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i++; } else q = false; }
                    else cur.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { row.Add(cur.ToString()); cur.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(cur.ToString()); cur.Clear(); rows.Add(row); row = new List<string>(); }
                else cur.Append(c);
            }
            if (cur.Length > 0 || row.Count > 0) { row.Add(cur.ToString()); rows.Add(row); }
            return rows;
        }

        static string Js(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    case '<': b.Append("\\u003c"); break;   // 避免 </script> 提前结束脚本
                    default: if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
                }
            }
            return b.Append('"').ToString();
        }

        // ---------------- 打开窗口 ----------------
        static void OpenWindow(string url)
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LongYinMod", "EditorWindow");
            foreach (var exe in new[] { Browser("msedge.exe", @"Microsoft\Edge\Application\msedge.exe"), Browser("chrome.exe", @"Google\Chrome\Application\chrome.exe") })
            {
                if (exe == null) continue;
                try
                {
                    Process.Start(new ProcessStartInfo(exe, "--app=\"" + url + "\" --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --window-size=1440,900") { UseShellExecute = false });
                    return;
                }
                catch { }
            }
            Process.Start(url);   // 没有 Edge / Chrome：用默认浏览器
        }

        static string Browser(string exeName, string rel)
        {
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName))
                    {
                        var p = k == null ? null : k.GetValue("") as string;
                        if (p != null && File.Exists(p)) return p;
                    }
                }
                catch { }
            }
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA" })
            {
                var b = Environment.GetEnvironmentVariable(env);
                if (b == null) continue;
                var p = Path.Combine(b, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // ---------------- 本地服务 ----------------
        static void Accept(TcpListener l)
        {
            while (!quit)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => { try { Handle(c); } catch { } finally { try { c.Close(); } catch { } } });
            }
        }

        static void Handle(TcpClient c)
        {
            c.ReceiveTimeout = 30000;
            var ns = c.GetStream();
            // 请求头
            var head = new List<byte>();
            while (true)
            {
                int b = ns.ReadByte();
                if (b < 0) return;
                head.Add((byte)b);
                int n = head.Count;
                if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break;
                if (n > 65536) return;
            }
            var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return;
            string method = first[0], target = first[1];
            var hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ln in lines.Skip(1)) { int k = ln.IndexOf(':'); if (k > 0) hdr[ln.Substring(0, k).Trim()] = ln.Substring(k + 1).Trim(); }
            byte[] body = new byte[0];
            string cl;
            if (hdr.TryGetValue("Content-Length", out cl))
            {
                long len = long.Parse(cl);
                if (len > 200L * 1024 * 1024) { Send(ns, 413, "text/plain", "太大"); return; }
                body = new byte[len];
                int got = 0;
                while (got < len) { int r = ns.Read(body, got, (int)len - got); if (r <= 0) return; got += r; }
            }
            string path = target, query = "";
            int qi = target.IndexOf('?');
            if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
            var qs = new Dictionary<string, string>();
            foreach (var kv in query.Split('&')) { int e = kv.IndexOf('='); if (e > 0) qs[Uri.UnescapeDataString(kv.Substring(0, e))] = Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' ')); }

            // 只接受来自本机页面、带口令的请求
            string host;
            if (!hdr.TryGetValue("Host", out host) || !(host.StartsWith("127.0.0.1:") || host.StartsWith("localhost:"))) { Send(ns, 403, "text/plain", "forbidden"); return; }
            string tk;
            bool authed = (hdr.TryGetValue("X-Token", out tk) && tk == token) || (qs.TryGetValue("t", out tk) && tk == token);
            if (!authed) { Send(ns, 403, "text/plain", "forbidden"); return; }

            try
            {
                lastPing = DateTime.Now;   // 任何带口令的请求都算还活着
                if (method == "GET" && path == "/") { Send(ns, 200, "text/html; charset=utf-8", page); return; }
                if (path == "/api/ping") { Send(ns, 200, "text/plain", "ok"); return; }
                if (path == "/api/bye") { byeAt = DateTime.Now.AddMilliseconds(1); Send(ns, 200, "text/plain", "ok"); return; }   // 关窗后约 8 秒退出；刷新页面会马上重新报到
                if (method == "GET" && path == "/api/list")
                {
                    var dir = Resolve(qs.ContainsKey("path") ? qs["path"] : "", true);
                    var items = new List<string>();
                    if (Directory.Exists(dir))
                    {
                        foreach (var d in Directory.GetDirectories(dir)) items.Add("{\"name\":" + Js(Path.GetFileName(d)) + ",\"kind\":\"directory\"}");
                        foreach (var f in Directory.GetFiles(dir)) items.Add("{\"name\":" + Js(Path.GetFileName(f)) + ",\"kind\":\"file\"}");
                    }
                    Send(ns, 200, "application/json; charset=utf-8", "[" + string.Join(",", items) + "]");
                    return;
                }
                if (method == "GET" && path == "/api/file")
                {
                    var f = Resolve(qs["path"], false);
                    if (!File.Exists(f)) { Send(ns, 404, "text/plain", "not found"); return; }
                    var data = File.ReadAllBytes(f);
                    long mtime = (long)(File.GetLastWriteTimeUtc(f) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    Send(ns, 200, "application/octet-stream", data, "X-Mtime: " + mtime + "\r\n");
                    return;
                }
                if (method == "POST" && path == "/api/write")
                {
                    var f = Resolve(qs["path"], false);
                    var name = Path.GetFileName(f);
                    if (!Regex.IsMatch(name, @"^(Hero|Save|Info|TempHero)(\.\d{8}_\d{6}\.bak)?$")) throw new ModException("不允许写入这个文件：" + name);
                    if (!Regex.IsMatch(Path.GetFileName(Path.GetDirectoryName(f)), @"^SaveSlot\d+$")) throw new ModException("只能写入存档槽位文件夹");
                    var tmp = f + ".tmp";
                    File.WriteAllBytes(tmp, body);
                    if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
                    Send(ns, 200, "text/plain", "ok");
                    return;
                }
                Send(ns, 404, "text/plain", "not found");
            }
            catch (Exception ex) { Send(ns, 500, "text/plain; charset=utf-8", ex.Message); }
        }

        // 相对存档目录的路径：只允许 SaveSlotN / 文件名 两层，不能跳出存档目录
        static string Resolve(string rel, bool dir)
        {
            rel = (rel ?? "").Replace('\\', '/').Trim('/');
            var parts = rel.Length == 0 ? new string[0] : rel.Split('/');
            if (parts.Length > 2 || parts.Any(p => p.Length == 0 || p == "." || p == ".." || !Regex.IsMatch(p, @"^[A-Za-z0-9_.]+$")))
                throw new ModException("路径不合法");
            if (parts.Length >= 1 && !Regex.IsMatch(parts[0], @"^SaveSlot\d+$")) throw new ModException("路径不合法");
            if (!dir && parts.Length != 2) throw new ModException("路径不合法");
            var full = Path.GetFullPath(Path.Combine(new[] { saveDir }.Concat(parts).ToArray()));
            if (!full.StartsWith(Path.GetFullPath(saveDir), StringComparison.OrdinalIgnoreCase)) throw new ModException("路径不合法");
            return full;
        }

        static void Send(NetworkStream ns, int code, string type, string text) { Send(ns, code, type, Encoding.UTF8.GetBytes(text), ""); }
        static void Send(NetworkStream ns, int code, string type, byte[] data, string extra = "")
        {
            var h = "HTTP/1.1 " + code + " " + (code == 200 ? "OK" : "Error") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + data.Length +
                    "\r\nCache-Control: no-store\r\nConnection: close\r\n" + extra + "\r\n";
            var hb = Encoding.ASCII.GetBytes(h);
            ns.Write(hb, 0, hb.Length);
            ns.Write(data, 0, data.Length);
            ns.Flush();
        }
    }
}
