using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirEnvir;
using Entry = NpcVisualEditor.DropAnalyzer.Entry;

namespace NpcVisualEditor
{
    /// <summary>
    /// P0 #1 掉落表分析工具（只读离线分析）：
    /// 有效爆率（复刻引擎两次 int 截断）/ 异常体检 / Monte Carlo 产出模拟。
    /// </summary>
    public class DropAnalysisForm : Form
    {
        private readonly string _dropsDir;
        private readonly string _serverDir;
        private readonly Envir _envir;
        private readonly HashSet<string> _itemNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DropAnalyzer.Result> _results = new(StringComparer.OrdinalIgnoreCase);

        private ListBox _lstFiles;
        private TextBox _txtFilter;
        private DataGridView _gridRows, _gridIssues;
        private NumericUpDown _numDropRate, _numBonus, _numRarity, _numSims;
        private Label _lblSummary;

        public DropAnalysisForm(string serverDir, Envir envir)
        {
            _serverDir = serverDir;
            _envir = envir;
            _dropsDir = Path.Combine(serverDir, "Envir", "Drops");
            foreach (var it in envir.ItemInfoList)
                if (!string.IsNullOrEmpty(it.Name)) _itemNames.Add(it.Name);

            Text = "掉落表分析工具 — 有效爆率 / 异常体检 / 产出模拟";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 760);
            MinimumSize = new Size(900, 600);
            BuildUi();
            LoadFileList();
        }

        private void BuildUi()
        {
            var left = new Panel { Dock = DockStyle.Left, Width = 240 };
            _txtFilter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "筛选怪物/文件..." };
            _txtFilter.TextChanged += (s, e) => LoadFileList(_txtFilter.Text);
            _lstFiles = new ListBox { Dock = DockStyle.Fill };
            _lstFiles.SelectedIndexChanged += (s, e) => ShowCurrent();
            left.Controls.Add(_lstFiles);
            left.Controls.Add(_txtFilter);

            var param = new Panel { Dock = DockStyle.Top, Height = 52 };
            int x = 12;
            AddParam(param, "DropRate:", x, out _numDropRate, 1, 1, 10000);
            AddParam(param, "掉落收益%:", x + 130, out _numBonus, 101, 0, 1000);
            AddParam(param, "稀有度%:", x + 280, out _numRarity, 0, 0, 1000);
            AddParam(param, "模拟次数:", x + 420, out _numSims, 100000, 1000, 5000000, 100);
            var btnAnalyze = new Button { Text = "分析", Location = new Point(x + 610, 4), Width = 70 };
            btnAnalyze.Click += (s, e) => ShowCurrent();
            var btnSim = new Button { Text = "模拟产出", Location = new Point(x + 690, 4), Width = 90 };
            btnSim.Click += (s, e) => SimulateCurrent();
            var btnScan = new Button { Text = "全库体检", Location = new Point(x + 790, 4), Width = 90 };
            btnScan.Click += (s, e) => ScanAll();
            var btnTree = new Button { Text = "轮盘分解/全服估算", Location = new Point(x + 890, 4), Width = 150 };
            btnTree.Click += (s, e) => new DropTreeForm(_serverDir, _envir).Show(this);
            param.Controls.AddRange(new Control[] { btnAnalyze, btnSim, btnScan, btnTree });

            _gridRows = NewGrid(DockStyle.Fill);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 200 };
            _gridIssues = NewGrid(DockStyle.Fill);
            _lblSummary = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            bottom.Controls.Add(_gridIssues);
            bottom.Controls.Add(_lblSummary);

            Controls.Add(_gridRows);
            Controls.Add(bottom);
            Controls.Add(param);
            Controls.Add(left);
        }

        private void AddParam(Panel p, string label, int x, out NumericUpDown num, int val, int min, int max, int inc = 1)
        {
            p.Controls.Add(new Label { Text = label, Location = new Point(x, 8), AutoSize = true });
            num = new NumericUpDown { Location = new Point(x + 62, 5), Width = val >= 100000 ? 100 : 60, Minimum = min, Maximum = max, Value = val, Increment = inc };
            p.Controls.Add(num);
        }

        private DataGridView NewGrid(DockStyle dock) => new DataGridView
        {
            Dock = dock, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToResizeRows = false, RowHeadersVisible = false
        };

        private void LoadFileList(string filter = null)
        {
            _lstFiles.BeginUpdate();
            _lstFiles.Items.Clear();
            _results.Clear();
            if (Directory.Exists(_dropsDir))
            {
                foreach (var f in Directory.GetFiles(_dropsDir, "*.txt"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _lstFiles.Items.Add(name);
                }
            }
            _lstFiles.EndUpdate();
            _lblSummary.Text = $"掉落目录: {_dropsDir}  文件数: {_lstFiles.Items.Count}";
        }

        private DropAnalyzer.Result GetResult(string name)
        {
            if (_results.TryGetValue(name, out var r)) return r;
            string path = Path.Combine(_dropsDir, name + ".txt");
            r = DropAnalyzer.ParseFile(path, _itemNames);
            _results[name] = r;
            return r;
        }

        private int BonusPercent => (int)_numBonus.Value - 100 + (int)_numRarity.Value;
        private double DropRate => (double)_numDropRate.Value;

        private void ShowCurrent()
        {
            if (_lstFiles.SelectedItem is not string name) return;
            var r = GetResult(name);
            _gridRows.Columns.Clear();
            foreach (var h in new[] { "层级", "类型", "物品/分组", "标称", "有效爆率", "期望/千次", "备注" })
                _gridRows.Columns.Add(h, h);
            AddRows(r.Entries);

            _gridIssues.Columns.Clear();
            _gridIssues.Columns.Add("Issue", "异常");
            foreach (var issue in r.Issues) _gridIssues.Rows.Add(issue);
            _lblSummary.Text = $"{name}: {r.Entries.Count} 顶层条目, {r.Issues.Count} 异常 | " +
                               $"DropRate={_numDropRate.Value} 收益={_numBonus.Value}% 稀有度={_numRarity.Value}%";
        }

        private void AddRows(List<Entry> entries)
        {
            foreach (var e in entries)
            {
                string type = e.IsGold ? "金币" : (e.IsGroup ? "分组" : "物品");
                double rate = e.IsGroup ? 0 : DropAnalyzer.EffectiveRate(e, DropRate, BonusPercent);
                string odds = e.IsGroup ? e.GroupKind : $"1/{e.Chance}";
                string rateStr = e.QuestRequired ? "不掉(Q)" : (e.IsGroup ? "-" : $"{rate:P4}");
                string per1k = e.IsGroup ? "-" : $"{rate * 1000:F1}";
                string note = e.QuestRequired ? "任务物品，引擎跳过" : "";
                _gridRows.Rows.Add(e.Depth, type, e.IsGold ? $"Gold {e.Gold}" : e.ItemName,
                    odds, rateStr, per1k, note);
                if (e.Children.Count > 0) AddRows(e.Children);
            }
        }

        private void SimulateCurrent()
        {
            if (_lstFiles.SelectedItem is not string name) return;
            var r = GetResult(name);
            int sims = (int)_numSims.Value;
            var rng = new Random();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sims; i++) Roll(r.Entries, rng, counts);
            _gridRows.Columns.Clear();
            foreach (var h in new[] { "物品", "产出总数", "每次平均" }) _gridRows.Columns.Add(h, h);
            foreach (var kv in counts.OrderByDescending(k => k.Value))
                _gridRows.Rows.Add(kv.Key, kv.Value, ((double)kv.Value / sims).ToString("F4"));
            _lblSummary.Text = $"{name}: 模拟 {sims:N0} 次击杀，产出 {counts.Count} 种物品";
        }

        private void Roll(IEnumerable<Entry> entries, Random rng, Dictionary<string, int> counts)
        {
            foreach (var e in entries)
            {
                if (e.IsGroup)
                {
                    if (e.GroupKind == "GROUP*" || e.GroupKind == "GROUP^")
                    {
                        foreach (var c in e.Children)
                        {
                            if (rng.NextDouble() < DropAnalyzer.EffectiveRate(c, DropRate, BonusPercent))
                            {
                                AddCount(c, counts);
                                if (e.GroupKind == "GROUP^") break;
                            }
                        }
                    }
                    else Roll(e.Children, rng, counts);  // GROUP：全子项各自判定
                }
                else if (rng.NextDouble() < DropAnalyzer.EffectiveRate(e, DropRate, BonusPercent))
                    AddCount(e, counts);
            }
        }

        private static void AddCount(Entry e, Dictionary<string, int> counts)
        {
            string nm = e.IsGold ? $"Gold {e.Gold}" : e.ItemName;
            counts[nm] = counts.GetValueOrDefault(nm) + 1;
        }

        private void ScanAll()
        {
            _gridIssues.Columns.Clear();
            _gridIssues.Columns.Add("File", "文件");
            _gridIssues.Columns.Add("Issue", "异常");
            int total = 0;
            foreach (string name in _lstFiles.Items)
            {
                var r = GetResult(name);
                foreach (var issue in r.Issues) { _gridIssues.Rows.Add(name, issue); total++; }
            }
            _lblSummary.Text = $"全库体检完成: {_lstFiles.Items.Count} 文件, {total} 条异常";
        }
    }
}
