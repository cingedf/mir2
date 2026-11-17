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
    /// P2 #22 地图爆率可视化（只读离线聚合）：
    /// 地图 → 刷怪怪物（去重+权重）→ 掉落物（按物品名聚合有效爆率/期望）。
    /// 关键：MDB 中 MonsterInfo.Drops 恒为空，掉落全在 Envir\Drops\*.txt，
    /// 必须复用 DropAnalyzer 字符串解析 + 自建字典，不能读 Drops[].Item。
    /// </summary>
    public class MapDropViewForm : Form
    {
        private readonly string _dropsDir;
        private readonly Envir _envir;
        private readonly Dictionary<string, ItemInfo> _itemByName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DropAnalyzer.Result> _dropCache =
            new(StringComparer.OrdinalIgnoreCase);

        private TreeView _tree;
        private DataGridView _grid;
        private Label _summary;
        private NumericUpDown _numDropRate;
        private CheckBox _chkWeightByCount;

        // 当前选中的聚合上下文
        private List<MonStat> _currentMonsters = new();

        private class MonStat
        {
            public MonsterInfo Monster;
            public int SpawnCount;   // 该地图该怪的刷怪点数量（权重）
            public int TotalCount;   // 刷怪数量之和
        }

        private class DropAgg
        {
            public string ItemName;
            public string MonsterName;
            public int Sources;      // 来自几个怪物
            public double BestRate;  // 有效爆率（最高）
            public double WeightedRate; // 按刷怪权重的贡献
        }

        public MapDropViewForm(string serverDir, Envir envir)
        {
            _envir = envir;
            _dropsDir = Path.Combine(serverDir, "Envir", "Drops");
            foreach (var it in envir.ItemInfoList)
                if (!string.IsNullOrEmpty(it.Name))
                    _itemByName[it.Name.Replace(" ", "")] = it;

            Text = "地图爆率可视化 — 地图 / 怪物 / 掉落物三级聚合";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1240, 780);
            MinimumSize = new Size(960, 620);
            BuildUi();
            LoadMaps();
        }

        private void BuildUi()
        {
            // 顶部参数栏
            var top = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 0, 0) };
            top.Controls.Add(new Label { Text = "爆率倍率:", Location = new Point(8, 12), AutoSize = true });
            _numDropRate = new NumericUpDown
            {
                Location = new Point(78, 8), Width = 70, Minimum = 0.1M, Maximum = 10000M,
                DecimalPlaces = 2, Value = 1
            };
            _chkWeightByCount = new CheckBox
            {
                Text = "按刷怪数量加权", Location = new Point(170, 10), AutoSize = true, Checked = true
            };
            _chkWeightByCount.CheckedChanged += (s, e) => ShowCurrentMap();
            _numDropRate.ValueChanged += (s, e) => { _dropCache.Clear(); ShowCurrentMap(); };
            top.Controls.Add(_numDropRate);
            top.Controls.Add(_chkWeightByCount);
            top.Controls.Add(new Label
            {
                Text = "（只读分析：MDB 不含掉落明细，数据来自 Envir\\Drops\\*.txt；额外加成=0）",
                Location = new Point(320, 12), AutoSize = true, ForeColor = Color.Gray
            });

            // 左：地图树
            var left = new Panel { Dock = DockStyle.Left, Width = 300 };
            left.Controls.Add(new Label { Text = "地图 → 怪物", Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) });
            _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            _tree.AfterSelect += (s, e) => OnTreeSelect();
            left.Controls.Add(_tree);

            // 右：明细表格 + 汇总
            var right = new Panel { Dock = DockStyle.Fill };
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _summary = new Label
            {
                Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 0, 0)
            };
            var summaryTip = new ToolTip();
            _summary.MouseHover += (s, e) => summaryTip.SetToolTip(_summary, _summary.Text);
            right.Controls.Add(_grid);
            right.Controls.Add(_summary);

            // Splitter 必须在目标 Left 控件之前添加，FindTarget 才指向 left，否则拖不动
            Controls.Add(right);
            Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4, MinSize = 160 });
            Controls.Add(left);
            Controls.Add(top);
        }

        private void LoadMaps()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            foreach (var map in _envir.MapInfoList.OrderBy(m => m.Index))
            {
                var node = new TreeNode($"[{map.Index}] {map.Title} ({map.FileName})");
                node.Tag = map;
                // 二级：该地图刷怪怪物（去重聚合）
                var monStats = AggregateMonsters(map);
                foreach (var ms in monStats.OrderByDescending(m => m.TotalCount))
                {
                    var mn = new TreeNode($"{ms.Monster.Name} ×{ms.TotalCount} ({ms.SpawnCount}点)");
                    mn.Tag = ms;
                    node.Nodes.Add(mn);
                }
                _tree.Nodes.Add(node);
            }
            _tree.EndUpdate();
            if (_tree.Nodes.Count == 0)
            {
                _summary.Text = "  未加载地图：服务端无地图数据或刷怪信息，无法分析。";
                _grid.Rows.Clear();
                _grid.Columns.Clear();
            }
        }

        private List<MonStat> AggregateMonsters(MapInfo map)
        {
            var result = new List<MonStat>();
            var byIndex = new Dictionary<int, MonStat>();
            foreach (var rp in map.Respawns ?? new List<RespawnInfo>())
            {
                var mon = _envir.GetMonsterInfo(rp.MonsterIndex);
                if (mon == null) continue;
                if (!byIndex.TryGetValue(mon.Index, out var ms))
                {
                    ms = new MonStat { Monster = mon };
                    byIndex[mon.Index] = ms;
                    result.Add(ms);
                }
                ms.SpawnCount++;
                ms.TotalCount += Math.Max(1, (int)rp.Count);
            }
            return result;
        }

        private void OnTreeSelect()
        {
            var node = _tree.SelectedNode;
            if (node == null) return;
            if (node.Tag is MonStat ms)
            {
                _currentMonsters = new List<MonStat> { ms };
                ShowMonsterDrops(new List<MonStat> { ms });
            }
            else if (node.Tag is MapInfo map)
            {
                _currentMonsters = AggregateMonsters(map);
                ShowCurrentMap();
            }
        }

        private void ShowCurrentMap() => ShowMonsterDrops(_currentMonsters);

        private void ShowMonsterDrops(List<MonStat> mons)
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            _grid.Columns.Add("Item", "掉落物");
            _grid.Columns.Add("Monster", "来源怪物");
            _grid.Columns.Add("Sources", "来源数");
            _grid.Columns.Add("Rate", "有效爆率");
            _grid.Columns.Add("Per1K", "每千次期望");
            _grid.Columns[0].FillWeight = 240;
            _grid.Columns[1].FillWeight = 200;
            _grid.Columns[2].FillWeight = 80;
            _grid.Columns[3].FillWeight = 110;
            _grid.Columns[4].FillWeight = 120;

            double dropRate = (double)_numDropRate.Value;
            var aggs = new Dictionary<string, DropAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var ms in mons)
            {
                var mon = ms.Monster;
                string dropName = string.IsNullOrEmpty(mon.DropPath) ? mon.Name : mon.DropPath;
                string path = Path.Combine(_dropsDir, dropName + ".txt");
                var res = GetDropResult(path);
                double weight = _chkWeightByCount.Checked ? ms.TotalCount : 1;

                foreach (var e in DropAnalyzer.Flatten(res.Entries))
                {
                    if (e.IsGroup || e.IsGold || e.QuestRequired) continue;
                    // 解析显示名（自建物品名匹配）
                    string disp = ResolveItemName(e.ItemName);
                    string key = disp + "|" + mon.Name;
                    double rate = DropAnalyzer.EffectiveRate(e, dropRate, 0);
                    if (!aggs.TryGetValue(key, out var a))
                    {
                        a = new DropAgg { ItemName = disp, MonsterName = mon.Name };
                        aggs[key] = a;
                    }
                    a.Sources++;
                    a.BestRate = Math.Max(a.BestRate, rate);
                    a.WeightedRate += rate * weight;
                }
            }

            double totalWeight = _chkWeightByCount.Checked
                ? mons.Sum(m => m.TotalCount)
                : mons.Count;
            foreach (var a in aggs.Values.OrderByDescending(x => x.BestRate))
            {
                int idx = _grid.Rows.Add(
                    a.ItemName, a.MonsterName, a.Sources,
                    $"1/{Math.Round(1.0 / a.BestRate, 1)}",
                    Math.Round(a.WeightedRate * 1000, 2));
                _grid.Rows[idx].Tag = a;
            }

            _summary.Text = mons.Count == 0
                ? "（选择地图或怪物查看掉落聚合）"
                : $"怪物数={mons.Count}  掉落条目={aggs.Count}  按={(_chkWeightByCount.Checked ? "刷怪数量加权" : "等权")}  总权重={totalWeight}";
        }

        private DropAnalyzer.Result GetDropResult(string path)
        {
            if (_dropCache.TryGetValue(path, out var r)) return r;
            r = File.Exists(path)
                ? DropAnalyzer.ParseFile(path, null)
                : new DropAnalyzer.Result { Name = Path.GetFileNameWithoutExtension(path) };
            _dropCache[path] = r;
            return r;
        }

        private string ResolveItemName(string raw)
        {
            if (_itemByName.TryGetValue(raw.Replace(" ", ""), out var info))
                return info.Name;
            return raw;
        }
    }
}
