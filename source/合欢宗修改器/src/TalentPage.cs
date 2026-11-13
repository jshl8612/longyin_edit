// LongYinMod - "合欢宗主" talent editor tab
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LongYinMod
{
    public partial class MainForm
    {
        DataGridView gridTalent;
        Label lblTalentState;
        Button btnTalentAdd, btnTalentDel, btnTalentDefault, btnTalentApply;
        List<EffectInfo> effects = new List<EffectInfo>();
        Dictionary<string, EffectInfo> effectByName = new Dictionary<string, EffectInfo>();
        bool talentLoading;
        public const int MaxTalentEffects = 24;   // the largest vanilla talent (终归一统) has 24

        TabPage BuildTalentPage()
        {
            var p = new TabPage("合欢宗主天赋") { Padding = new Padding(8) };
            lblTalentState = new Label { Dock = DockStyle.Top, Height = 52, Text = "", Padding = new Padding(2, 4, 2, 0) };

            gridTalent = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnEnter, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            };
            var cName = new DataGridViewComboBoxColumn { Name = "name", HeaderText = "效果", Width = 160, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
            var cVal = new DataGridViewTextBoxColumn { Name = "val", HeaderText = "数值", Width = 80 };
            var cUnit = new DataGridViewTextBoxColumn { Name = "unit", HeaderText = "单位", Width = 50, ReadOnly = true };
            var cTrig = new DataGridViewTextBoxColumn { Name = "trig", HeaderText = "生效时机", Width = 80, ReadOnly = true };
            var cDesc = new DataGridViewTextBoxColumn { Name = "desc", HeaderText = "说明", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
            gridTalent.Columns.AddRange(new DataGridViewColumn[] { cName, cVal, cUnit, cTrig, cDesc });
            gridTalent.CellValueChanged += (s, e) => { if (!talentLoading && e.RowIndex >= 0 && e.ColumnIndex == 0) { FillRowInfo(gridTalent.Rows[e.RowIndex]); RefreshCombos(e.RowIndex); } };
            gridTalent.CurrentCellDirtyStateChanged += (s, e) => { if (gridTalent.IsCurrentCellDirty && gridTalent.CurrentCell is DataGridViewComboBoxCell) gridTalent.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            gridTalent.DataError += (s, e) => { e.ThrowException = false; };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 96, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
            btnTalentAdd = new Button { Text = "添加效果", Width = 130, Height = 32 };
            btnTalentDel = new Button { Text = "删除选中", Width = 100, Height = 32 };
            btnTalentDefault = new Button { Text = "恢复默认", Width = 100, Height = 32 };
            btnTalentApply = new Button { Text = "保存并安装到游戏", Width = 170, Height = 32, Font = BoldFont };
            var note = new Label
            {
                AutoSize = false, Width = 840, Height = 46, ForeColor = SystemColors.GrayText,
                Text = "· 数值单位为 % 的效果填百分数（20 = +20%），其余填点数（5 = +5），负数为减少。效果只能从游戏已有的效果里选。\n" +
                       "· 天赋效果写在游戏文件里：保存后会自动重新安装（需关闭游戏）。已拥有此天赋的角色无需改存档，进游戏即为新效果。"
            };
            bar.Controls.Add(btnTalentAdd); bar.Controls.Add(btnTalentDel); bar.Controls.Add(btnTalentDefault); bar.Controls.Add(btnTalentApply); bar.Controls.Add(note);
            p.Controls.Add(gridTalent); p.Controls.Add(bar); p.Controls.Add(lblTalentState);

            btnTalentAdd.Click += (s, e) => AddTalentRow(null, double.NaN);
            btnTalentDel.Click += (s, e) =>
            {
                gridTalent.EndEdit();
                var sel = gridTalent.SelectedRows.Cast<DataGridViewRow>().ToList();
                if (sel.Count == 0 && gridTalent.CurrentRow != null) sel.Add(gridTalent.CurrentRow);
                gridTalent.CurrentCell = null;
                foreach (var r in sel) gridTalent.Rows.Remove(r);
                RefreshCombos(-1);
            };
            btnTalentDefault.Click += (s, e) => FillTalentGrid(Hhzz.DefaultEffect);
            btnTalentApply.Click += (s, e) => ApplyTalent();
            return p;
        }

        void LoadTalentPage()
        {
            try
            {
                effects = GamePatcher.LoadEffects(File.ReadAllBytes(Game().Resources));
                effectByName = new Dictionary<string, EffectInfo>();
                foreach (var ef in effects) if (!effectByName.ContainsKey(ef.Name)) effectByName[ef.Name] = ef;
                var col = (DataGridViewComboBoxColumn)gridTalent.Columns["name"];
                col.Items.Clear();
                foreach (var ef in effects) col.Items.Add(ef.Name);
                FillTalentGrid(TalentConfig.Load());
                UpdateTalentState();
            }
            catch (Exception ex) { lblTalentState.Text = "读取游戏效果表失败：" + ex.Message; }
        }

        void UpdateTalentState()
        {
            string installed = null;
            try
            {
                var t = GamePatcher.LoadTagTable(File.ReadAllBytes(Game().Resources));
                var r = t.RowByName(Hhzz.Name);
                if (r != null) installed = r[t.Col("效果")];
            }
            catch { }
            string cfg = TalentConfig.Load();
            lblTalentState.Text = "当前设置：" + Pretty(cfg) + "\n游戏里：" + (installed == null ? "尚未安装" : Pretty(installed) + (installed == cfg ? "　✓ 与设置一致" : "　（与设置不同，需要安装）"));
            lblTalentState.ForeColor = installed == cfg ? Color.ForestGreen : Color.DarkOrange;
        }

        string Pretty(string effect)
        {
            try
            {
                return string.Join("，", TalentConfig.Parse(effect).Select(kv =>
                {
                    EffectInfo ef; bool pct = effectByName.TryGetValue(kv.Key, out ef) && ef.Percent;
                    double v = pct ? kv.Value * 100 : kv.Value;
                    return kv.Key + (v >= 0 ? "+" : "") + v.ToString("0.##", CultureInfo.InvariantCulture) + (pct ? "%" : "");
                }));
            }
            catch { return effect; }
        }

        void FillTalentGrid(string effect)
        {
            talentLoading = true;
            gridTalent.Rows.Clear();
            foreach (var kv in TalentConfig.Parse(effect)) AddTalentRow(kv.Key, kv.Value);
            talentLoading = false;
            RefreshCombos(-1);
            gridTalent.CurrentCell = null;
            gridTalent.ClearSelection();
        }

        void AddTalentRow(string name, double raw)
        {
            var col = (DataGridViewComboBoxColumn)gridTalent.Columns["name"];
            if (name == null)
            {
                if (gridTalent.Rows.Count >= MaxTalentEffects) { MessageBox.Show(this, "最多 " + MaxTalentEffects + " 个效果。", "已达上限", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                gridTalent.EndEdit();
                var used = UsedNames(-1);
                var free = effects.FirstOrDefault(x => !used.Contains(x.Name));
                if (free == null) return;
                name = free.Name;
            }
            if (!col.Items.Contains(name)) col.Items.Add(name);   // unknown name from config: keep visible, flagged on save
            int i = gridTalent.Rows.Add();
            var row = gridTalent.Rows[i];
            bool was = talentLoading; talentLoading = true;
            row.Cells["name"].Value = name;
            talentLoading = was;
            EffectInfo ef; bool pct = effectByName.TryGetValue(name, out ef) && ef.Percent;
            if (!double.IsNaN(raw)) row.Cells["val"].Value = (pct ? raw * 100 : raw).ToString("0.##########", CultureInfo.InvariantCulture);
            else row.Cells["val"].Value = pct ? "10" : "1";
            FillRowInfo(row);
            if (!talentLoading) RefreshCombos(-1);
            if (double.IsNaN(raw)) { gridTalent.ClearSelection(); row.Selected = true; gridTalent.CurrentCell = row.Cells["name"]; }
        }

        HashSet<string> UsedNames(int exceptRow)
        {
            var h = new HashSet<string>();
            foreach (DataGridViewRow r in gridTalent.Rows)
                if (r.Index != exceptRow) { var n = r.Cells["name"].Value as string; if (n != null) h.Add(n); }
            return h;
        }

        // each row's dropdown only offers effects not used by the other rows; skipRow = row being edited
        void RefreshCombos(int skipRow)
        {
            bool was = talentLoading; talentLoading = true;
            foreach (DataGridViewRow r in gridTalent.Rows)
            {
                if (r.Index == skipRow) continue;
                var cell = (DataGridViewComboBoxCell)r.Cells["name"];
                var own = cell.Value as string;
                var others = UsedNames(r.Index);
                var want = effects.Where(ef => !others.Contains(ef.Name)).Select(ef => ef.Name).ToList();
                if (own != null && !want.Contains(own)) want.Insert(0, own);
                bool same = cell.Items.Count == want.Count;
                for (int i = 0; same && i < want.Count; i++) if ((string)cell.Items[i] != want[i]) same = false;
                if (same) continue;
                cell.Items.Clear();
                foreach (var n in want) cell.Items.Add(n);
                cell.Value = own;
            }
            talentLoading = was;
            btnTalentAdd.Enabled = gridTalent.Rows.Count < MaxTalentEffects;
            btnTalentAdd.Text = "添加效果（" + gridTalent.Rows.Count + "/" + MaxTalentEffects + "）";
        }

        void FillRowInfo(DataGridViewRow row)
        {
            string n = row.Cells["name"].Value as string ?? "";
            EffectInfo ef;
            if (effectByName.TryGetValue(n, out ef))
            {
                row.Cells["unit"].Value = ef.Percent ? "%" : "点";
                row.Cells["trig"].Value = ef.TriggerText;
                row.Cells["desc"].Value = ef.Desc;
            }
            else { row.Cells["unit"].Value = ""; row.Cells["trig"].Value = ""; row.Cells["desc"].Value = "（游戏里没有这个效果）"; }
        }

        string TalentFromGrid()
        {
            gridTalent.EndEdit();
            var items = new List<KeyValuePair<string, double>>();
            foreach (DataGridViewRow row in gridTalent.Rows)
            {
                string n = row.Cells["name"].Value as string ?? "";
                EffectInfo ef;
                if (!effectByName.TryGetValue(n, out ef)) throw new ModException("第 " + (row.Index + 1) + " 行：游戏里没有效果「" + n + "」");
                string vs = (Convert.ToString(row.Cells["val"].Value) ?? "").Trim().TrimEnd('%');
                double v;
                if (!double.TryParse(vs, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v == 0 || double.IsInfinity(v))
                    throw new ModException("第 " + (row.Index + 1) + " 行「" + n + "」：数值无效（不能为空或 0）");
                if (items.Any(x => x.Key == n)) throw new ModException("效果「" + n + "」重复了，请合并成一行");
                items.Add(new KeyValuePair<string, double>(n, ef.Percent ? Math.Round(v / 100, 6) : v));
            }
            if (items.Count == 0) throw new ModException("至少需要一个效果");
            if (items.Count > MaxTalentEffects) throw new ModException("最多 " + MaxTalentEffects + " 个效果");
            return TalentConfig.Format(items);
        }

        void ApplyTalent()
        {
            string eff;
            try { eff = TalentFromGrid(); }
            catch (ModException ex) { MessageBox.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            TalentConfig.Save(eff);
            LogLine("合欢宗主天赋设置已保存：" + Pretty(eff));
            FillTalentGrid(eff);
            ScanGame();
            if (plan != null && plan[GamePatcher.FTag].Installed)
            {
                if (!plan[GamePatcher.FTag].NeedsUpdate) { }
                else if (GameFiles.GameRunning())
                    MessageBox.Show(this, "设置已保存。\n游戏正在运行，无法修改游戏文件。请关闭游戏后，到「游戏文件」页点「按勾选安装 / 更新」。", "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                {
                    try { LogLine(GamePatcher.Apply(plan, plan.InstalledKeys)); }
                    catch (Exception ex) { LogLine("安装失败：" + ex.Message); MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    ScanGame();
                }
            }
            else MessageBox.Show(this, "设置已保存。\n游戏里还没有安装合欢宗主天赋：到「游戏文件」页勾选「合欢宗主天赋」后点「按勾选安装 / 更新」。", "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateTalentState();
        }
    }
}
