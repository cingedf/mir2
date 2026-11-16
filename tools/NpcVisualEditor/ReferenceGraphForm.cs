using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 对象关系图谱 + 影响分析：选中对象 → 查看引用它的上游（入边闭包）或它依赖的下游（出边闭包），
    /// 每条边带类型与证据位置。节点来源 = 离线 Envir.Edit 快照 + 文本（只读，不触碰运行中服务端）。
    /// 对标 Grimoire 的关系图谱 + 影响分析。
    /// </summary>
    public class ReferenceGraphForm : Form
    {
        private readonly string _serverDir;
        private readonly Envir _envir;
        private ReferenceGraph.Graph _graph;
        private readonly List<ReferenceGraph.GraphNode> _filtered = new();
        private bool _reverse = true; // 默认：谁引用了它（影响分析）

        private readonly TextBox _txtFilter;
        private readonly ListBox _list;
        private readonly DataGridView _grid;
        private readonly Label _lblStatus;

        public ReferenceGraphForm(string serverDir, Envir envir)
        {
            _serverDir = serverDir;
            _envir = envir;
            Text = "对象关系图谱 · 影响分析";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1050, 720);

            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "  类型筛选:", Location = new Point(6, 12), AutoSize = true });
            var cmbKind = new ComboBox { Location = new Point(72, 8), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKind.Items.AddRange(new object[] { "全部", "地图", "怪物", "NPC", "任务", "物品", "脚本" });
            cmbKind.SelectedIndex = 0;
            top.Controls.Add(cmbKind);
            _txtFilter = new TextBox { Location = new Point(195, 8), Width = 180 };
            _txtFilter.PlaceholderText = "名称/ID 过滤";
            _txtFilter.TextChanged += (s, e) => ApplyFilter(cmbKind.SelectedIndex);
            top.Controls.Add(_txtFilter);
            cmbKind.SelectedIndexChanged += (s, e) => ApplyFilter(cmbKind.SelectedIndex);
            var rbIn = new RadioButton { Text = "谁引用了它(影响)", Location = new Point(390, 9), AutoSize = true, Checked = true };
            var rbOut = new RadioButton { Text = "它依赖谁(下游)", Location = new Point(540, 9), AutoSize = true };
            rbIn.CheckedChanged += (s, e) => { if (rbIn.Checked) { _reverse = true; RefreshImpact(); } };
            rbOut.CheckedChanged += (s, e) => { if (rbOut.Checked) { _reverse = false; RefreshImpact(); } };
            top.Controls.Add(rbIn);
            top.Controls.Add(rbOut);

            var left = new Panel { Dock = DockStyle.Left, Width = 280 };
            _list = new ListBox { Dock = DockStyle.Fill };
            _list.SelectedIndexChanged += (s, e) => RefreshImpact();
            left.Controls.Add(_list);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            _lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "  正在构建引用图…" };
            bottom.Controls.Add(_lblStatus);

            Controls.Add(_grid);
            Controls.Add(left);
            Controls.Add(top);
            Controls.Add(bottom);

            Load += (s, e) => Build();
        }

        private void Build()
        {
            try
            {
                _graph = ReferenceGraph.Build(_envir, _serverDir);
                _lblStatus.Text = $"  图谱就绪：节点 {_graph.Nodes.Count} · 引用边 {_graph.Edges.Count}（只读离线分析）";
                ApplyFilter(0);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "  构建失败: " + ex.Message;
            }
        }

        private void ApplyFilter(int kindIndex)
        {
            _list.Items.Clear();
            _filtered.Clear();
            if (_graph == null) return;
            string kw = _txtFilter.Text.Trim();
            foreach (var n in _graph.Nodes.Values.OrderBy(n => n.Kind).ThenBy(n => n.DisplayName))
            {
                if (kindIndex > 0 && (int)n.Kind != kindIndex - 1) continue;
                if (kw.Length > 0 && !n.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    && n.Id.ToString() != kw) continue;
                _filtered.Add(n);
                _list.Items.Add($"[{KindName(n.Kind)}] {n.DisplayName} (#{n.Id})  入{n.InDegree}/出{n.OutDegree}");
            }
            _lblStatus.Text += $"  显示 {_filtered.Count} 项";
        }

        private void RefreshImpact()
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            if (_graph == null || _list.SelectedIndex < 0 || _list.SelectedIndex >= _filtered.Count) return;
            var node = _filtered[_list.SelectedIndex];
            var edges = ReferenceGraph.ImpactClosure(_graph, node.Key, _reverse);
            _grid.Columns.Add("Dir", _reverse ? "引用方" : "被引用方");
            _grid.Columns.Add("Kind", "边类型");
            _grid.Columns.Add("Target", "对象");
            _grid.Columns.Add("Evidence", "证据位置");
            _grid.Columns.Add("Src", "来源");
            foreach (var e in edges)
            {
                string otherKey = _reverse ? e.FromKey : e.ToKey;
                _graph.Nodes.TryGetValue(otherKey, out var other);
                string otherName = other == null ? otherKey : $"[{KindName(other.Kind)}] {other.DisplayName}";
                _grid.Rows.Add(
                    otherName, EdgeName(e.Kind) + (e.IsText ? "(文本)" : ""),
                    _graph.Nodes.TryGetValue(e.ToKey, out var to) ? to.DisplayName : e.ToKey,
                    e.Evidence, e.IsText ? "文本" : "MDB");
            }
            _lblStatus.Text = $"  [{KindName(node.Kind)}] {node.DisplayName} — {(_reverse ? "影响闭包" : "依赖闭包")} {edges.Count} 条边（含递归）";
        }

        private static string KindName(ReferenceGraph.NodeKind k) => k switch
        {
            ReferenceGraph.NodeKind.Map => "地图", ReferenceGraph.NodeKind.Monster => "怪物",
            ReferenceGraph.NodeKind.NPC => "NPC", ReferenceGraph.NodeKind.Quest => "任务",
            ReferenceGraph.NodeKind.Item => "物品", _ => "脚本"
        };

        private static string EdgeName(ReferenceGraph.EdgeKind k) => k switch
        {
            ReferenceGraph.EdgeKind.MapRespawn => "地图刷怪",
            ReferenceGraph.EdgeKind.MapMovement => "传送门",
            ReferenceGraph.EdgeKind.NPCMap => "NPC所在地图",
            ReferenceGraph.EdgeKind.NPCScript => "NPC脚本",
            ReferenceGraph.EdgeKind.NPCCollectQuest => "NPC收集任务",
            ReferenceGraph.EdgeKind.NPCFinishQuest => "NPC交付任务",
            ReferenceGraph.EdgeKind.QuestRequired => "前置任务",
            ReferenceGraph.EdgeKind.QuestNpc => "任务NPC",
            ReferenceGraph.EdgeKind.MonsterDrop => "怪物掉落",
            ReferenceGraph.EdgeKind.QuestRewardItem => "任务奖励物品",
            ReferenceGraph.EdgeKind.QuestKillMonster => "任务击杀怪",
            ReferenceGraph.EdgeKind.QuestItemTask => "任务物品需求",
            _ => k.ToString()
        };
    }
}
