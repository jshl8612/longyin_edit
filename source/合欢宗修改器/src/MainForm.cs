// LongYinMod - window UI
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LongYinMod
{
    public partial class MainForm : Form
    {
        TextBox txtDir, txtLog;
        Button btnBrowse, btnScan, btnInstall, btnRestore, btnReload, btnApply;
        ListView lvParts, lvSlots;
        Label lblGameSummary, lblForce, lblUnlock, lblMale, lblHint, lblTalent;
        CheckBox chkUnlock, chkFemale, chkTalent;
        HeroDoc hero;
        GamePatcher.Plan plan;
        bool fillingParts;
        List<SlotInfo> slots;
        SaveDoc doc;
        SlotInfo curSlot;

        static readonly Font UiFont = new Font("Microsoft YaHei UI", 9f);
        static readonly Font BoldFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);

        public MainForm()
        {
            Text = "龙胤立志传 · 合欢宗修改器";
            Font = UiFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 660);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;

            // ---- top: game folder
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = 4, Padding = new Padding(8, 8, 8, 0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(new Label { Text = "游戏目录", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
            txtDir = new TextBox { Dock = DockStyle.Fill };
            top.Controls.Add(txtDir, 1, 0);
            btnBrowse = new Button { Text = "浏览…", AutoSize = true };
            btnScan = new Button { Text = "检测", AutoSize = true };
            top.Controls.Add(btnBrowse, 2, 0);
            top.Controls.Add(btnScan, 3, 0);

            // ---- log
            txtLog = new TextBox { Dock = DockStyle.Bottom, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            tabs.TabPages.Add(BuildGamePage());
            tabs.TabPages.Add(BuildSavePage());
            tabs.TabPages.Add(BuildTalentPage());

            Controls.Add(tabs);
            Controls.Add(txtLog);
            Controls.Add(top);

            btnBrowse.Click += (s, e) => Browse();
            btnScan.Click += (s, e) => ScanAll();
            Shown += (s, e) => { txtDir.Text = Settings.LoadDir() ?? SteamLocator.Find() ?? ""; if (txtDir.Text.Length > 0) ScanAll(); else LogLine("请选择游戏目录（包含 GameAssembly.dll 的文件夹）。"); };
        }

        TabPage BuildGamePage()
        {
            var p = new TabPage("游戏文件") { Padding = new Padding(8) };
            lblGameSummary = new Label { Dock = DockStyle.Top, Height = 46, Font = BoldFont, Text = "", Padding = new Padding(2, 4, 2, 0) };
            lvParts = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, GridLines = true, CheckBoxes = true };
            lvParts.Columns.Add("功能", 165);
            lvParts.Columns.Add("说明", 320);
            lvParts.Columns.Add("涉及文件", 250);
            lvParts.Columns.Add("状态", 160);
            lvParts.ItemCheck += (s, e) =>
            {
                if (fillingParts || plan == null) return;
                var f = (GamePatcher.Feature)lvParts.Items[e.Index].Tag;
                if (!f.Compatible && e.NewValue == CheckState.Checked) e.NewValue = CheckState.Unchecked;
            };
            lvParts.ItemChecked += (s, e) => { if (!fillingParts) UpdateInstallButton(); };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 92, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
            btnInstall = new Button { Text = "按勾选安装 / 更新", Width = 170, Height = 32, Font = BoldFont };
            btnRestore = new Button { Text = "全部还原为原版", Width = 150, Height = 32 };
            var note = new Label
            {
                AutoSize = false, Width = 840, Height = 46,
                Text = "· 勾选要用的功能，取消勾选的已安装功能会被卸载。安装前会自动把原版备份为 .bak。\n" +
                       "· 游戏更新后再点「检测」，原版会被重新识别，可直接重装。改之前请先关闭游戏。",
                ForeColor = SystemColors.GrayText
            };
            bar.Controls.Add(btnInstall); bar.Controls.Add(btnRestore); bar.Controls.Add(note);
            p.Controls.Add(lvParts); p.Controls.Add(bar); p.Controls.Add(lblGameSummary);
            btnInstall.Click += (s, e) => Install();
            btnRestore.Click += (s, e) => Restore();
            return p;
        }

        TabPage BuildSavePage()
        {
            var p = new TabPage("存档") { Padding = new Padding(8) };
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var leftPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var rightPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            split.Controls.Add(leftPanel, 0, 0);
            split.Controls.Add(rightPanel, 1, 0);
            lvSlots = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true };
            lvSlots.Columns.Add("槽位", 70);
            lvSlots.Columns.Add("角色", 80);
            lvSlots.Columns.Add("游戏日期", 90);
            lvSlots.Columns.Add("门派", 140);
            lvSlots.Columns.Add("文件时间", 130);
            btnReload = new Button { Text = "刷新存档列表", Dock = DockStyle.Bottom, Height = 30 };
            leftPanel.Controls.Add(lvSlots);
            leftPanel.Controls.Add(btnReload);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10, 4, 4, 4), AutoScroll = true };
            right.Controls.Add(new Label { Text = "玩家门派", Font = BoldFont, AutoSize = true });
            lblForce = new Label { AutoSize = true, MaximumSize = new Size(380, 0), Text = "← 选择一个存档" };
            right.Controls.Add(lblForce);
            right.Controls.Add(new Label { Text = " ", AutoSize = true });
            right.Controls.Add(new Label { Text = "修改内容", Font = BoldFont, AutoSize = true });
            chkUnlock = new CheckBox { Text = "解锁合欢殿（之后在建造菜单里自己建）", AutoSize = true, Checked = true };
            lblUnlock = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(18, 0, 0, 4) };
            chkFemale = new CheckBox { Text = "门派只收女弟子（男性比例设为 0）", AutoSize = true, Checked = true };
            lblMale = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(18, 0, 0, 4) };
            right.Controls.Add(chkUnlock); right.Controls.Add(lblUnlock);
            right.Controls.Add(chkFemale); right.Controls.Add(lblMale);
            chkTalent = new CheckBox { Text = "主角获得天赋「" + Hhzz.Name + "」", AutoSize = true, Checked = true };
            lblTalent = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(18, 0, 0, 4) };
            right.Controls.Add(chkTalent); right.Controls.Add(lblTalent);
            btnApply = new Button { Text = "写入存档", Width = 150, Height = 32, Font = BoldFont, Enabled = false };
            right.Controls.Add(btnApply);
            lblHint = new Label
            {
                AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.FromArgb(160, 80, 0),
                Text = "\n游戏开着也可以写入：写完后在游戏里重新读取这个存档即可生效（读档前别在游戏里存到这个槽位，否则修改会被覆盖）。\n建议先在 Steam 里关闭本游戏的云存档（库 → 右键游戏 → 属性 → 通用），否则启动时可能被云端旧存档覆盖。\n每次写入都会在存档旁自动备份（Save / Hero.日期时间.bak）。"
            };
            right.Controls.Add(lblHint);
            rightPanel.Controls.Add(right);
            p.Controls.Add(split);

            btnReload.Click += (s, e) => LoadSlots();
            lvSlots.SelectedIndexChanged += (s, e) => SelectSlot();
            btnApply.Click += (s, e) => ApplySave();
            return p;
        }

        // ---------------- helpers
        void LogLine(string s)
        {
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s.Replace("\n", "\r\n") + "\r\n");
        }

        GameFiles Game() { return new GameFiles(txtDir.Text.Trim()); }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "选择龙胤立志传游戏目录（包含 GameAssembly.dll 的文件夹）" })
            {
                if (Directory.Exists(txtDir.Text)) d.SelectedPath = txtDir.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { txtDir.Text = d.SelectedPath; ScanAll(); }
            }
        }

        void Busy(bool b) { Cursor = b ? Cursors.WaitCursor : Cursors.Default; Application.DoEvents(); }

        void ScanAll()
        {
            var g = Game();
            if (!g.Looks()) { LogLine("这个目录不是游戏目录：" + g.Root); lblGameSummary.Text = "未找到游戏"; return; }
            Settings.SaveDir(g.Root);
            ScanGame();
            LoadSlots();
            LoadTalentPage();
        }

        // ---------------- game files
        void ScanGame()
        {
            Busy(true);
            fillingParts = true;
            lvParts.Items.Clear();
            try
            {
                var gp = new GamePatcher(Game());
                plan = gp.Analyze();
                foreach (var f in plan.Features)
                {
                    var it = new ListViewItem(new[] { f.Title, f.Desc, string.Join("、", f.Files), f.StateText }) { Tag = f };
                    it.Checked = f.Compatible;   // default: every compatible feature, installed or not
                    it.ForeColor = !f.Compatible ? Color.Firebrick : f.Installed ? (f.NeedsUpdate ? Color.DarkOrange : Color.ForestGreen) : SystemColors.WindowText;
                    lvParts.Items.Add(it);
                }
                int bad = plan.Features.Count(f => !f.Compatible);
                lblGameSummary.Text = bad > 0 ? "有 " + bad + " 项与当前游戏版本不兼容（无法勾选），其余可以正常安装"
                    : "勾选要用的功能后点「按勾选安装 / 更新」（合欢殿编号 " + gp.NewId + "，外观借用 " + Hhd.LookAlike + "）";
                lblGameSummary.ForeColor = bad > 0 ? Color.DarkOrange : SystemColors.ControlText;
                btnRestore.Enabled = plan.AnyModified;
                LogLine("游戏文件检测完成：" + string.Join("，", plan.Features.Select(f => f.Title + "=" + f.StateText)));
            }
            catch (Exception ex)
            {
                plan = null;
                lblGameSummary.Text = "✗ 检测失败"; lblGameSummary.ForeColor = Color.Firebrick;
                btnRestore.Enabled = false;
                LogLine("检测失败：" + ex.Message);
            }
            finally { fillingParts = false; Busy(false); UpdateInstallButton(); }
        }

        HashSet<string> CheckedFeatures()
        {
            var h = new HashSet<string>();
            foreach (ListViewItem it in lvParts.Items) if (it.Checked) h.Add(((GamePatcher.Feature)it.Tag).Key);
            return h;
        }

        void UpdateInstallButton()
        {
            if (plan == null) { btnInstall.Enabled = false; return; }
            var sel = CheckedFeatures();
            bool changes = plan.Features.Any(f => sel.Contains(f.Key) != f.Installed || (sel.Contains(f.Key) && f.NeedsUpdate));
            btnInstall.Enabled = changes;
        }

        void Install()
        {
            if (plan == null) return;
            var sel = CheckedFeatures();
            var add = plan.Features.Where(f => sel.Contains(f.Key) && !f.Installed).ToList();
            var upd = plan.Features.Where(f => sel.Contains(f.Key) && f.Installed && f.NeedsUpdate).ToList();
            var del = plan.Features.Where(f => !sel.Contains(f.Key) && f.Installed).ToList();
            var msg = new List<string>();
            if (add.Count > 0) msg.Add("安装：" + string.Join("、", add.Select(f => f.Title)));
            if (upd.Count > 0) msg.Add("更新：" + string.Join("、", upd.Select(f => f.Title)));
            if (del.Count > 0) msg.Add("卸载：" + string.Join("、", del.Select(f => f.Title)));
            string warn = "";
            if (del.Any(f => f.Key == GamePatcher.FHhd)) warn += "\n\n注意：卸载合欢殿后，存档里已建好的合欢殿将无法正常显示。";
            if (del.Any(f => f.Key == GamePatcher.FTag)) warn += "\n\n注意：卸载合欢宗主天赋后，已拥有该天赋的角色可能出错。";
            if (MessageBox.Show(this, string.Join("\n", msg) + warn + "\n\n原版会备份为 .bak。请确认游戏已经关闭。继续吗？", "按勾选安装", MessageBoxButtons.OKCancel, del.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK) return;
            Busy(true);
            try { LogLine(GamePatcher.Apply(plan, sel)); }
            catch (Exception ex) { LogLine("安装失败：" + ex.Message); MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { Busy(false); }
            ScanGame();
            if (curSlot != null) SelectSlot();
        }

        void Restore()
        {
            if (plan == null) return;
            if (MessageBox.Show(this, "把所有被修改的游戏文件还原为原版？\n（存档里已建好的合欢殿、已拥有的合欢宗主天赋在还原后可能无法正常显示，请自行斟酌。）", "全部还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Busy(true);
            try { LogLine(GamePatcher.Apply(plan, new HashSet<string>())); }
            catch (Exception ex) { LogLine("还原失败：" + ex.Message); MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { Busy(false); }
            ScanGame();
            if (curSlot != null) SelectSlot();
        }

        // ---------------- saves
        void LoadSlots()
        {
            lvSlots.Items.Clear();
            doc = null; curSlot = null; btnApply.Enabled = false;
            lblForce.Text = "← 选择一个存档";
            try
            {
                slots = Saves.List(Game());
                foreach (var s in slots)
                {
                    var it = new ListViewItem(new[] { s.Index + (s.Index == 0 ? " 自动" : s.Index == 10 ? " 快速" : ""), s.Character, s.GameDate, Saves.StripTags(s.ForceLine), s.SaveMtime.ToString("MM-dd HH:mm") });
                    it.Tag = s;
                    lvSlots.Items.Add(it);
                }
                LogLine("找到 " + slots.Count + " 个存档。");
            }
            catch (Exception ex) { LogLine("读取存档列表失败：" + ex.Message); }
        }

        void SelectSlot()
        {
            if (lvSlots.SelectedItems.Count == 0) return;
            curSlot = (SlotInfo)lvSlots.SelectedItems[0].Tag;
            Busy(true);
            try
            {
                doc = SaveDoc.Load(Path.Combine(curSlot.Dir, "Save"), curSlot);
                var sb = new StringBuilder();
                sb.AppendLine("存档 " + curSlot.Index + "：" + curSlot.Character + "　" + curSlot.GameDate);
                if (doc.Player == null)
                {
                    sb.AppendLine(string.IsNullOrEmpty(curSlot.ForceName) ? "角色目前没有加入门派。" : "没能在存档里找到门派「" + curSlot.ForceName + "」。");
                }
                else
                {
                    var f = doc.Player;
                    sb.AppendLine("门派：" + f.Display + (string.IsNullOrEmpty(f.SetName) ? "" : "（原 " + f.Name + "）") + "　职位：" + doc.PlayerTitle);
                    sb.AppendLine("驻地：区域 " + f.MainAreaId);
                    sb.AppendLine(f.Replaced ? "✓ 已自立门户" : "✗ 尚未自立门户（这是游戏原有门派）");
                }
                lblForce.Text = sb.ToString();
                lblForce.ForeColor = doc.Player != null && doc.Player.Replaced ? Color.ForestGreen : Color.DarkOrange;

                int? hid = Saves.InstalledHhdId(Game());
                if (hid == null) { chkUnlock.Enabled = false; chkUnlock.Checked = false; lblUnlock.Text = "游戏文件里还没有合欢殿，请先在「游戏文件」页安装。"; }
                else
                {
                    bool has = doc.Unlocked.Contains(hid.Value);
                    chkUnlock.Enabled = !has; chkUnlock.Checked = !has;
                    lblUnlock.Text = has ? "已解锁（编号 " + hid + "）" : "未解锁（编号 " + hid + "）。解锁后在任意驻地的建造菜单里都能建。";
                }
                if (doc.Player == null) { chkFemale.Enabled = false; chkFemale.Checked = false; lblMale.Text = "需要先加入或自立一个门派。"; }
                else
                {
                    bool zero = doc.Player.MaleRate == 0;
                    chkFemale.Enabled = !zero; chkFemale.Checked = !zero;
                    lblMale.Text = zero ? "已经是只收女弟子" : "当前男性比例 " + doc.Player.MaleRate + "。只影响之后新进门的弟子。";
                }
                hero = null;
                int? tid = Saves.InstalledTagId(Game());
                string heroPath = Path.Combine(curSlot.Dir, "Hero");
                if (tid == null) { chkTalent.Enabled = false; chkTalent.Checked = false; lblTalent.Text = "游戏文件里还没有这个天赋，请先在「游戏文件」页安装/更新。"; }
                else if (!File.Exists(heroPath)) { chkTalent.Enabled = false; chkTalent.Checked = false; lblTalent.Text = "这个槽位没有 Hero 文件。"; }
                else
                {
                    try
                    {
                        hero = HeroDoc.Load(heroPath);
                        bool has = hero.Tags.Contains(tid.Value);
                        chkTalent.Enabled = !has; chkTalent.Checked = !has;
                        lblTalent.Text = (has ? hero.Name + " 已拥有（编号 " + tid + "）" : "给 " + hero.Name + " 添加（编号 " + tid + "）")
                            + "\n效果：" + Pretty(Hhzz.Effect);
                    }
                    catch (Exception ex) { chkTalent.Enabled = false; chkTalent.Checked = false; lblTalent.Text = "读取主角失败：" + ex.Message; }
                }
                bool canUnlock = chkUnlock.Enabled, canFemale = chkFemale.Enabled;
                btnApply.Enabled = canUnlock || canFemale || chkTalent.Enabled;
                if (doc.Player != null && !doc.Player.Replaced && canFemale)
                    LogLine("提示：存档 " + curSlot.Index + " 尚未自立门户，「只收女弟子」会作用在原有门派「" + doc.Player.Display + "」上。");
            }
            catch (Exception ex) { doc = null; btnApply.Enabled = false; lblForce.Text = "读取失败"; LogLine("读取存档失败：" + ex.Message); }
            finally { Busy(false); }
        }

        void ApplySave()
        {
            if (doc == null || curSlot == null) return;
            int? hid = Saves.InstalledHhdId(Game());
            bool unlock = chkUnlock.Enabled && chkUnlock.Checked;
            bool female = chkFemale.Enabled && chkFemale.Checked;
            bool talent = chkTalent.Enabled && chkTalent.Checked && hero != null;
            int? tid = Saves.InstalledTagId(Game());
            if (!unlock && !female && !talent) { LogLine("没有勾选任何修改。"); return; }
            if (doc.Player != null && !doc.Player.Replaced && female &&
                MessageBox.Show(this, "这个存档还没有自立门户，「只收女弟子」会作用在游戏原有门派「" + doc.Player.Display + "」上。继续吗？", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                var data = (unlock || female) ? doc.Apply(unlock, hid ?? -1, female) : null;
                var hdata = talent && tid != null ? hero.AddTag(tid.Value) : null;
                if (data == null && hdata == null) { LogLine("存档无需修改。"); return; }
                // check both files before writing either one
                if (data != null) Saves.CheckUnchanged(doc.Path, doc.Raw);
                if (hdata != null) Saves.CheckUnchanged(hero.Path, hero.Raw);
                if (data != null)
                {
                    string bak = Saves.Write(doc, data);
                    LogLine("已写入存档 " + curSlot.Index + "（" + (unlock ? "解锁合欢殿 " : "") + (female ? "只收女弟子" : "") + "），原存档备份为 " + Path.GetFileName(bak));
                }
                if (hdata != null)
                {
                    string bak = Saves.WriteFile(hero.Path, hero.Raw, SaveIO.Encode(hdata, hero.Raw));
                    LogLine("已写入存档 " + curSlot.Index + "（" + hero.Name + " 获得天赋「" + Hhzz.Name + "」），原文件备份为 " + Path.GetFileName(bak));
                }
                MessageBox.Show(this, "写入完成。\n在游戏里（重新）读取存档 " + curSlot.Index + " 即可生效。\n\n如果 Steam 弹出云存档冲突，请选「本地」。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { LogLine("写入失败：" + ex.Message); MessageBox.Show(this, ex.Message, "写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            SelectSlot();
        }
    }

    public static class Settings
    {
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LongYinMod", "settings.txt"); } }
        public static string LoadDir()
        {
            try { var s = File.ReadAllText(FilePath).Trim(); return new GameFiles(s).Looks() ? s : null; } catch { return null; }
        }
        public static void SaveDir(string d)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, d); } catch { }
        }
    }

    public static class SteamLocator
    {
        public static string Find()
        {
            var cands = new List<string>();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var sp = k == null ? null : k.GetValue("SteamPath") as string;
                    if (sp != null)
                    {
                        sp = sp.Replace('/', '\\');
                        cands.Add(sp);
                        var vdf = Path.Combine(sp, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                                cands.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                }
            }
            catch { }
            foreach (var d in new[] { "C", "D", "E", "F", "G" }) cands.Add(d + @":\SteamLibrary");
            foreach (var c in cands)
            {
                var p = Path.Combine(c, "steamapps", "common", "LongYinLiZhiZhuan");
                if (new GameFiles(p).Looks()) return p;
            }
            return null;
        }
    }
}
