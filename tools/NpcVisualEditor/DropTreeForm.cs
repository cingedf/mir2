using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;
using Entry = NpcVisualEditor.DropAnalyzer.Entry;

namespace NpcVisualEditor
{
    /// <summary>
    /// #19 掉落表增量视图（消费 DropAnalyzer.Result，不重复造轮子）：
    /// 「轮盘分解」——GROUP/GROUP*/GROUP^ 概率树 + 组内条件概率；
    /// 「全服产出估算」——按全地图刷怪权重外推单物品总产出（只读静态估算）。
    /// </summary>
    public class DropTreeForm : Form
    {
        private readonly string _dropsDir;
        private readonly Envir _envir;
        private readonly Dictionary<string, DropAnalyzer.Result> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _monsterNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _spawnWeight = new(); // MonsterIndex -> 全服刷怪总数

        private TreeView _tree;
        private DataGridView _grid;
        private Label _summary;
        private NumericUpDown _numDropRate, _numBonus;
        private TextBox _txtItemFilter;

        public DropTreeForm(string serverDir, Envir envir)
        {
            _dropsDir = Path.Combine(serverDir, "Envir", "Drops");
            _envir = envir;
            foreach (var m in envir.MonsterInfoList)
                if (!string.IsNullOrEmpty(m.Name)) _monsterNames.Add(m.Name);
            BuildSpawnWeight();

            Text = "掉落轮盘分解 / 全服产出估算（只读静态估算）";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 680);
            MinimumSize = new Size(800, 520);
            BuildUi();
        }

        private void BuildSpawnWeight()
        {
            foreach (var map in _envir.MapInfoList)
                foreach (var rp in map.Respawns ?? new List<RespawnInfo>())
                {
                    int idx = rp.MonsterIndex;
                    int c = 0;
                    _spawnWeight.TryGetValue(idx, out c);
                    _spawnWeight[idx] = c + Math.Max(1, (int)rp.Count);
                }
        }

        private void BuildUi()
        {
            var param = new Panel { Dock = DockStyle.Top, Height = 46 };
            int x = 12;
            AddParam(param, "爆率倍率:", x, out _numDropRate, 1, 1, 10000);
            AddParam(param, "收益%:", x + 140, out _numBonus, 100, 0, 1000);
            // 移除原"模拟次数"输入框：全服估算走解析爆率×刷怪权重加权，不跑蒙特卡洛；
            // 原输入框（宽100，位于x+322）与按钮（x+400）重叠22px，一并消除。
            var btnTree = new Button { Text = "轮盘分解", Location = new Point(x + 280, 4), Width = 92, Height = 28 };
            btnTree.Click += (s, e) => BuildTreeView();
            var btnEst = new Button { Text = "全服产出估算", Location = new Point(x + 380, 4), Width = 116, Height = 28 };
            btnEst.Click += (s, e) => EstimateServerOutput();
            param.Controls.AddRange(new Control[] { btnTree, btnEst });

            var left = new Panel { Dock = DockStyle.Left, Width = 270 };
            _txtItemFilter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "筛选怪物/物品..." };
            _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            _tree.AfterSelect += (s, e) => ShowNodeDetail(e.Node);
            left.Controls.Add(_tree);
            left.Controls.Add(_txtItemFilter);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToResizeRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _summary = new Label
            {
                Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 4, 8, 4),
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Text = "提示：轮盘分解显示 GROUP/GROUP*/GROUP^ 概率树及组内条件概率；全服产出估算按刷怪权重外推，均为静态标称值。"
            };
            var summaryTip = new ToolTip();
            _summary.MouseHover += (s, e) => summaryTip.SetToolTip(_summary, _summary.Text);

            Controls.Add(_grid);
            Controls.Add(_summary);
            Controls.Add(param);
            Controls.Add(left);
        }

        private void AddParam(Panel p, string label, int x, out NumericUpDown num, int val, int min, int max, int inc = 1)
        {
            p.Controls.Add(new Label { Text = label, Location = new Point(x, 12), AutoSize = true });
            num = new NumericUpDown
            {
                Location = new Point(x + 62, 8), Width = val >= 10000 ? 100 : 58,
                Minimum = min, Maximum = max, Value = val, Increment = inc
            };
            p.Controls.Add(num);
        }

        private double DropRate => (double)_numDropRate.Value;
        private int BonusPercent => (int)_numBonus.Value - 100;

        private DropAnalyzer.Result GetResult(string monster)
        {
            if (_cache.TryGetValue(monster, out var r)) return r;
            string path = Path.Combine(_dropsDir, monster + ".txt");
            r = File.Exists(path) ? DropAnalyzer.ParseFile(path, null) : new DropAnalyzer.Result { Name = monster };
            _cache[monster] = r;
            return r;
        }

        private void BuildTreeView()
        {
            _tree.Nodes.Clear();
            string filter = _txtItemFilter.Text.Trim();
            var names = _monsterNames.Where(n => string.IsNullOrEmpty(filter) || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                                     .OrderBy(n => n).Take(400).ToList();
            foreach (var name in names)
            {
                var r = GetResult(name);
                if (r.Entries.Count == 0) continue;
                var root = new TreeNode($"{name} ({r.Entries.Count}项)");
                root.Tag = new NodeTag { Monster = name };
                foreach (var e in r.Entries) AddEntryNode(root.Nodes, e, name);
                _tree.Nodes.Add(root);
            }
            _summary.Text = $"轮盘分解：{_tree.Nodes.Count} 个怪物掉落表（最多显示400个，可用筛选缩小范围）。";
            _grid.Columns.Clear();
        }

        private void AddEntryNode(TreeNodeCollection nodes, Entry e, string monster)
        {
            double rate = e.IsGroup ? 0 : DropAnalyzer.EffectiveRate(e, DropRate, BonusPercent);
            string label = e.IsGroup
                ? $"【{e.GroupKind}】{e.Children.Count}子项"
                : $"{e.ItemName}  {RateText(rate)}" + (e.IsGold ? $" （金币 {e.Gold}）" : "");
            var node = new TreeNode(label) { Tag = new NodeTag { Monster = monster, Entry = e } };
            // 组内条件概率：GROUP* 子项按 率/率和；GROUP^ 按首个命中概率
            if (e.Children.Count > 0)
            {
                var parent = (TreeNode)node;
                var kids = e.Children.ToList();
                double sum = kids.Where(c => !c.IsGroup).Sum(c => DropAnalyzer.EffectiveRate(c, DropRate, BonusPercent));
                foreach (var c in kids)
                {
                    AddEntryNode(parent.Nodes, c, monster);
                    if (e.GroupKind == "GROUP*" && sum > 0 && !c.IsGroup)
                    {
                        double cond = DropAnalyzer.EffectiveRate(c, DropRate, BonusPercent) / sum;
                        parent.Nodes[parent.Nodes.Count - 1].Text += $"  [组内条件 {cond:P1}]";
                    }
                }
            }
            nodes.Add(node);
        }

        private static string RateText(double rate) => rate <= 0 ? "(恒不掉)" : rate >= 1 ? "必掉" : $"1/{1 / rate:F0}";

        private class NodeTag { public string Monster; public Entry Entry; }

        private void ShowNodeDetail(TreeNode node)
        {
            if (node?.Tag is not NodeTag t) return;
            _grid.Columns.Clear();
            foreach (var h in new[] { "属性", "值" }) _grid.Columns.Add(h, h);
            _grid.Columns[0].FillWeight = 150;
            _grid.Columns[1].FillWeight = 450;
            _grid.Rows.Add("怪物", t.Monster);
            if (t.Entry == null) return;
            var e = t.Entry;
            _grid.Rows.Add("原始行", e.Raw);
            _grid.Rows.Add("类型", e.IsGroup ? "分组 " + e.GroupKind : (e.IsGold ? "金币" : "物品"));
            _grid.Rows.Add("标称分母", e.Chance > 0 ? "1/" + e.Chance : "-");
            _grid.Rows.Add("有效爆率", RateText(DropAnalyzer.EffectiveRate(e, DropRate, BonusPercent)));
            _grid.Rows.Add("QuestRequired", e.QuestRequired ? "是(引擎跳过)" : "否");
            _grid.Rows.Add("深度", e.Depth);
        }

        private void EstimateServerOutput()
        {
            _tree.Nodes.Clear();
            _grid.Columns.Clear();
            foreach (var h in new[] { "物品", "标称爆率", "全服刷怪数/次", "每次期望产出", "相对产出指数" })
                _grid.Columns.Add(h, h);
            int[] estWeights = { 220, 100, 120, 120, 140 };
            for (int i = 0; i < estWeights.Length; i++) _grid.Columns[i].FillWeight = estWeights[i];

            string filter = _txtItemFilter.Text.Trim();
            // 物品 -> (怪物名 -> 该怪掉落此物的有效爆率)，保留来源以便按刷怪权重加权
            var byItem = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            var weightByMonster = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in _spawnWeight.Where(k => k.Value > 0))
            {
                var mon = _envir.MonsterInfoList.FirstOrDefault(m => m.Index == kv.Key);
                if (mon == null || string.IsNullOrEmpty(mon.Name)) continue;
                string dropName = string.IsNullOrEmpty(mon.DropPath) ? mon.Name : mon.DropPath;
                weightByMonster[dropName] = kv.Value;
                var r = GetResult(dropName);
                foreach (var e in DropAnalyzer.Flatten(r.Entries))
                {
                    if (e.IsGroup || e.IsGold || e.QuestRequired) continue;
                    double rate = DropAnalyzer.EffectiveRate(e, DropRate, BonusPercent);
                    if (rate <= 0) continue;
                    string nm = e.ItemName;
                    if (!byItem.TryGetValue(nm, out var perMonster))
                        byItem[nm] = perMonster = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    // 同一怪物多条掉落同一物品时取最优（最大）爆率，避免重复累加
                    perMonster[dropName] = Math.Max(perMonster.GetValueOrDefault(dropName), rate);
                }
            }

            int totalWeight = weightByMonster.Values.Sum();
            // 物品 -> 加权每次期望 = Σ(该物品在怪物m的爆率 × 怪物m全服刷怪数) / 全服刷怪总数
            var weighted = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in byItem)
            {
                double sum = 0;
                foreach (var kp in kv.Value)
                    if (weightByMonster.TryGetValue(kp.Key, out int w)) sum += kp.Value * w;
                weighted[kv.Key] = totalWeight > 0 ? sum / totalWeight : 0;
            }

            var rows = weighted.Where(k => string.IsNullOrEmpty(filter) || k.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                               .OrderByDescending(k => k.Value).Take(500).ToList();
            foreach (var kv in rows)
                _grid.Rows.Add(kv.Key, "-", totalWeight, kv.Value.ToString("F8"), kv.Value.ToString("F8"));
            _summary.Text = $"全服产出估算：{rows.Count} 种物品，基于 {weightByMonster.Count} 种怪物、全服刷怪总数 {totalWeight:N0}。" +
                            " 每次期望产出与相对产出指数均为按刷怪权重加权的标称值（Σ爆率×刷怪数/总刷怪数），仅用于横向对比，非真实产出量。";
        }
    }
}
