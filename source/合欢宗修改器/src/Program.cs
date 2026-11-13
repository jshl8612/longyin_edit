using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("LongYinMod")]
[assembly: System.Reflection.AssemblyProduct("龙胤立志传 合欢宗修改器")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace LongYinMod
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest(args);
            if (args.Length > 0 && args[0] == "--idtest") { try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { } return IdTest.Run(args.Skip(1).ToArray()); }
            if (args.Length > 1 && (args[0] == "--install" || args[0] == "--restore" || args[0] == "--status"))
            {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
                try
                {
                    var gp = new GamePatcher(new GameFiles(args[1]));
                    var plan = gp.Analyze();
                    // --install <root> [feature keys...]   (no keys = all compatible features)
                    if (args[0] == "--install")
                    {
                        var sel = args.Length > 2 ? new System.Collections.Generic.HashSet<string>(args.Skip(2))
                                                  : new System.Collections.Generic.HashSet<string>(plan.Features.Where(f => f.Compatible).Select(f => f.Key));
                        Console.Write(GamePatcher.Apply(plan, sel));
                    }
                    else if (args[0] == "--restore") Console.Write(GamePatcher.Apply(plan, new System.Collections.Generic.HashSet<string>()));
                    else foreach (var f in plan.Features) Console.WriteLine(f.Key + " | " + f.Title + " | " + f.StateText);
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); return 1; }
            }
            try { if (Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        // --selftest <gameRoot> <outDir> [slot]
        //   analyzes the game files, writes what each patched file would be into outDir, and
        //   optionally applies the save edits of a slot into outDir/Save.<slot>
        static int SelfTest(string[] a)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            var g = new GameFiles(a[1]);
            string outDir = a[2];
            Directory.CreateDirectory(outDir);
            try
            {
                var gp = new GamePatcher(g);
                var plan = gp.Analyze();
                Console.WriteLine("NewId=" + gp.NewId + " IconId=" + gp.IconId + " SpecialBase=" + gp.SpecialBase + " Atlas=" + Path.GetFileName(gp.AtlasFile));
                foreach (var f in plan.Features) Console.WriteLine(f.Key + " | " + f.Title + " | " + f.StateText);
                var all = plan.Build(new System.Collections.Generic.HashSet<string>(plan.Features.Where(f => f.Compatible).Select(f => f.Key)));
                foreach (var kv in all)
                {
                    File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(kv.Key)), kv.Value);
                    Console.WriteLine("   " + Path.GetFileName(kv.Key) + " md5=" + Bin.Md5(kv.Value) + " size=" + kv.Value.Length);
                }
                if (a.Length > 3)
                {
                    int slot = int.Parse(a[3]);
                    var s = Saves.List(g).First(x => x.Index == slot);
                    var doc = SaveDoc.Load(Path.Combine(s.Dir, "Save"), s);
                    var pl = doc.Player;
                    Console.WriteLine("Slot " + slot + ": force='" + s.ForceName + "' title='" + s.Title + "' -> " + (pl == null ? "none" : pl.Display + " id=" + pl.ForceId + " area=" + pl.MainAreaId + " replaced=" + pl.Replaced + " male=" + pl.MaleRate));
                    Console.WriteLine("  unlocked=[" + string.Join(",", doc.Unlocked) + "]");
                    var data = doc.Apply(true, gp.NewId, true);
                    if (data != null) { File.WriteAllBytes(Path.Combine(outDir, "Save." + slot), data); Console.WriteLine("  edited save written, size " + doc.Data.Length + " -> " + data.Length); }
                    else Console.WriteLine("  nothing to change");
                }
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); return 1; }
        }
    }
}
