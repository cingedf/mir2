using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Server.MirDatabase;

namespace NpcVisualEditor
{
    /// <summary>
    /// 脚本命令向导：列出 NPC命令说明.txt 的全部分类/命令，点击生成调用范例，
    /// 并提供带实时过滤的搜索框。辅助编写脚本，避免未知命令。
    /// </summary>
    public class CommandWizardForm : Form
    {
        private readonly string _helpPath;
        private readonly TreeView _tree;
        private readonly TextBox _txtSearch, _txtDetail;
        private readonly List<(string cat, string line)> _entries = new();

        public CommandWizardForm(string serverDir)
        {
            _helpPath = Path.Combine(serverDir, "Envir", "NPCs", "NPC命令说明.txt");
            Text = "脚本命令向导";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 680);

            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "搜索命令:", Location = new Point(12, 12), AutoSize = true });
            _txtSearch = new TextBox { Location = new Point(85, 9), Width = 300 };
            _txtSearch.TextChanged += (s, e) => BuildTree(_txtSearch.Text);
            top.Controls.Add(_txtSearch);
            Controls.Add(top);

            _tree = new TreeView { Dock = DockStyle.Left, Width = 280, HideSelection = false };
            _tree.AfterSelect += (s, e) => UpdateDetail();
            // Splitter 须在目标 Left 控件之前添加，FindTarget 才指向 _tree，使其可拖动调宽
            Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4, MinSize = 150 });
            Controls.Add(_tree);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            // 说明区改 Dock 自适应：原硬编码 Location(12,38)+Size(560,520) 在窗口变窄时
            // 会越过 right 面板右边界被遮挡；Dock=Fill + 底部按钮彻底消除硬编码宽度依赖
            var btnCopy = new Button { Text = "复制命令", Dock = DockStyle.Bottom, Height = 32, Width = 110 };
            btnCopy.Click += (s, e) =>
            {
                string text = _txtDetail.Lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                if (text.Length > 0) { Clipboard.SetText(text); MessageBox.Show("已复制: " + text); }
            };
            _txtDetail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true,
                ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F)
            };
            // 停靠顺序：后加的先停。label(Top)→btn(Bottom)→txt(Fill) 的添加顺序使最终布局正确
            right.Controls.Add(_txtDetail);
            right.Controls.Add(btnCopy);
            right.Controls.Add(new Label { Text = "命令说明 / 范例:", Dock = DockStyle.Top, Height = 24 });
            Controls.Add(right);

            ParseHelp();
            BuildTree(null);
        }

        private void ParseHelp()
        {
            _entries.Clear();
            if (!File.Exists(_helpPath)) return;
            string cat = "其他";
            foreach (var raw in File.ReadAllLines(_helpPath))
            {
                var line = raw.TrimEnd('\r');
                var cm = Regex.Match(line, @"-+>([^<]+)<-+");
                if (cm.Success) { cat = cm.Groups[1].Value.Trim(); continue; }
                if (line.Trim().Length > 0) _entries.Add((cat, line));
            }
        }

        private void BuildTree(string filter)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var groups = _entries
                .Where(e => string.IsNullOrEmpty(filter) ||
                            e.line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(e => e.cat);
            foreach (var g in groups)
            {
                var node = _tree.Nodes.Add(g.Key);
                foreach (var e in g.Take(120))
                    node.Nodes.Add(e.line.Split(':')[0].Trim());
            }
            _tree.ExpandAll();
            _tree.EndUpdate();
        }

        private void UpdateDetail()
        {
            if (_tree.SelectedNode?.Parent == null) { _txtDetail.Text = ""; return; }
            string key = _tree.SelectedNode.Text;
            var hit = _entries.FirstOrDefault(e => e.line.Split(':')[0].Trim() == key);
            if (hit.line == null) return;
            // 找到该命令及其后若干行（格式/示例）
            int idx = _entries.IndexOf(hit);
            var sb = new StringBuilder();
            for (int i = idx; i < Math.Min(idx + 8, _entries.Count); i++)
            {
                if (i > idx && _entries[i].line.Contains(":") &&
                    Regex.IsMatch(_entries[i].line, @"^[A-Za-z]+:")) break;
                sb.AppendLine(_entries[i].line);
            }
            _txtDetail.Text = sb.ToString();
        }
    }

    /// <summary>
    /// 特效/BUFF 赋予模板：可视化编辑 SetBuffs.txt 行，并生成对应 GIVEBUFF 脚本。
    /// </summary>
    public class EffectBuffForm : Form
    {
        private readonly string _buffsPath;
        private readonly ListBox _lstBuffs;
        private readonly TextBox _txtName, _txtProps, _txtScript;
        private readonly List<string> _lines = new();

        public EffectBuffForm(string serverDir)
        {
            _buffsPath = Path.Combine(serverDir, "Envir", "SetBuffs.txt");
            Text = "特效 / BUFF 赋予模板";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 660);

            _lstBuffs = new ListBox { Location = new Point(15, 40), Size = new Size(300, 480) };
            _lstBuffs.SelectedIndexChanged += (s, e) =>
            {
                if (_lstBuffs.SelectedIndex < 0) return;
                var parts = _lines[_lstBuffs.SelectedIndex].Split(';');
                _txtName.Text = parts[0];
                _txtProps.Text = string.Join("\r\n", parts.Skip(1).Where(p => p.Length > 0));
                UpdateScript();
            };
            Controls.Add(new Label { Text = "现有 BUFF（SetBuffs.txt）:", Location = new Point(15, 15), AutoSize = true });
            Controls.Add(_lstBuffs);

            Controls.Add(new Label { Text = "BUFF 名称:", Location = new Point(335, 15), AutoSize = true });
            _txtName = new TextBox { Location = new Point(410, 12), Size = new Size(220, 24) };
            _txtName.TextChanged += (s, e) => UpdateScript();
            Controls.Add(_txtName);

            Controls.Add(new Label { Text = "属性（每行 属性=值）:", Location = new Point(335, 48), AutoSize = true });
            _txtProps = new TextBox
            {
                Location = new Point(335, 74), Size = new Size(295, 260), Multiline = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10F)
            };
            _txtProps.TextChanged += (s, e) => UpdateScript();
            Controls.Add(_txtProps);

            Controls.Add(new Label { Text = "生成的 GIVEBUFF 脚本:", Location = new Point(650, 15), AutoSize = true });
            _txtScript = new TextBox
            {
                Location = new Point(650, 40), Size = new Size(230, 480), Multiline = true,
                ReadOnly = true, Font = new Font("Consolas", 9F), ScrollBars = ScrollBars.Vertical
            };
            Controls.Add(_txtScript);

            var btnAdd = new Button { Text = "新增/更新 BUFF", Location = new Point(335, 355), Width = 130 };
            btnAdd.Click += BtnAdd_Click;
            var btnSave = new Button { Text = "保存到 SetBuffs.txt", Location = new Point(480, 355), Width = 150 };
            btnSave.Click += BtnSave_Click;
            var btnScript = new Button { Text = "复制脚本", Location = new Point(650, 535), Width = 100 };
            btnScript.Click += (s, e) => { if (_txtScript.Text.Length > 0) Clipboard.SetText(_txtScript.Text); };
            Controls.AddRange(new Control[] { btnAdd, btnSave, btnScript });

            LoadBuffs();
        }

        private void LoadBuffs()
        {
            _lines.Clear();
            if (File.Exists(_buffsPath))
                _lines.AddRange(File.ReadAllLines(_buffsPath).Where(l => l.Trim().Length > 0));
            RefreshList();
        }

        private void RefreshList(int selectIndex = -1)
        {
            _lstBuffs.BeginUpdate();
            _lstBuffs.Items.Clear();
            foreach (var l in _lines) _lstBuffs.Items.Add(l.Split(';')[0]);
            // 重建后显式恢复选中项：否则会回到无选中/首项，SelectedIndexChanged 用旧项回填编辑框，
            // 让用户刚新增/更新的内容被首项覆盖，看起来像"新增失败"。
            if (selectIndex >= 0 && selectIndex < _lstBuffs.Items.Count)
                _lstBuffs.SelectedIndex = selectIndex;
            _lstBuffs.EndUpdate();
        }

        private void UpdateScript()
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0) { _txtScript.Text = ""; return; }
            var props = _txtProps.Text.Replace("\r\n", "\n").Split('\n')
                .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            string suffix = props.Length > 0 ? " " + string.Join(" ", props) : "";
            _txtScript.Text =
                "; === 赋予 BUFF: " + name + " ===\r\n" +
                "[@GIVEBUFF]\r\n#IF\r\n#ACT\r\n" +
                "GIVEBUFF " + name + " 1800 TRUE FALSE FALSE" + suffix + "\r\n" +
                "LOCALMESSAGE \"已附加增益：" + name + "\" Hint\r\nBREAK\r\n";
        }

        /// <summary>把当前编辑框内容提交到 _lines（不弹框），返回所提交行的索引（-1=未提交）。
        /// 供"新增/更新"与"保存"共用，避免用户改了编辑框却没点"新增/更新"直接保存导致改动丢失。</summary>
        private int CommitCurrentEdit()
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0) return -1;
            var props = _txtProps.Text.Replace("\r\n", "\n").Split('\n')
                .Select(p => p.Trim()).Where(p => p.Length > 0);
            string line = name + ";" + string.Join(";", props) + ";;";
            int idx = _lines.FindIndex(l => l.Split(';')[0] == name);
            if (idx >= 0)
            {
                if (_lines[idx] == line) return idx; // 无变更也保持选中当前行
                _lines[idx] = line;
            }
            else { _lines.Add(line); idx = _lines.Count - 1; }
            RefreshList(idx);
            return idx;
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            if (_txtName.Text.Trim().Length == 0) { MessageBox.Show("请填写 BUFF 名称。"); return; }
            CommitCurrentEdit();
            MessageBox.Show("已加入列表，记得点“保存到 SetBuffs.txt”。");
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            CommitCurrentEdit(); // 保存前先提交当前编辑，防止编辑框未提交的改动丢失
            try
            {
                // 与其他编辑器一致：覆盖前先做时间戳备份，避免误操作丢失原有 BUFF 定义
                if (File.Exists(_buffsPath))
                {
                    var bak = _buffsPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(_buffsPath, bak, true);
                }
                File.WriteAllLines(_buffsPath, _lines, new UTF8Encoding(false));
                MessageBox.Show(this, "SetBuffs.txt 已保存（已自动备份原文件）。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message); }
        }
    }

    /// <summary>
    /// 任务链流程图：把 QuestInfo 按 RequiredQuest 串成链，节点显示名称/等级/奖励。
    /// </summary>
    public class QuestFlowForm : Form
    {
        private class QuestDetail
        {
            public HashSet<string> GoalKinds = new();
            public List<string> RewardLines = new();
        }

        private readonly List<QuestInfo> _quests;
        private readonly string _serverDir;
        private readonly Dictionary<int, QuestDetail> _details = new();
        private readonly Panel _canvas;
        private readonly Dictionary<int, Point> _positions = new();
        private QuestInfo _selected;

        public QuestFlowForm(IEnumerable<QuestInfo> quests, string serverDir = null)
        {
            _quests = quests.OrderBy(q => q.Index).ToList();
            _serverDir = serverDir;
            LoadQuestDetails();
            Text = "任务链流程图";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 720);

            int x = 30, y = 30;
            foreach (var q in _quests)
            {
                _positions[q.Index] = new Point(x, y);
                y += 90;
                if (y > 620) { y = 30; x += 260; }
            }
            _canvas = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(30, 30, 34) };
            _canvas.AutoScrollMinSize = new Size(x + 280, Math.Max(y + 40, 700));
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += (s, e) =>
            {
                var p = new Point(e.X - _canvas.AutoScrollPosition.X, e.Y - _canvas.AutoScrollPosition.Y);
                _selected = _quests.FirstOrDefault(q => Rect(q).Contains(p));
                _canvas.Invalidate();
            };
            Controls.Add(_canvas);

            var bar = new Panel { Dock = DockStyle.Top, Height = 36 };
            bar.Controls.Add(new Label
            {
                Text = $"共 {_quests.Count} 个任务（连线 = 前置任务 RequiredQuest）  |  点击节点查看",
                Location = new Point(12, 10), AutoSize = true
            });
            Controls.Add(bar);
        }

        /// <summary>
        /// 任务目标/奖励明细只在运行时由 Envir\Quests\&lt;FileName&gt;.txt 填充（MDB 中恒空），
        /// 这里只读解析这些文本以补充静态视图；无文本文件的任务标注"运行时加载"。
        /// </summary>
        private void LoadQuestDetails()
        {
            if (string.IsNullOrEmpty(_serverDir)) return;
            string dir = Path.Combine(_serverDir, "Envir", "Quests");
            if (!Directory.Exists(dir)) return;
            foreach (var q in _quests)
            {
                if (string.IsNullOrEmpty(q.FileName)) continue;
                string path = Path.Combine(dir, q.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? q.FileName : q.FileName + ".txt");
                if (!File.Exists(path)) continue;
                var d = new QuestDetail();
                string section = "";
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    var sm = System.Text.RegularExpressions.Regex.Match(line, @"^\[@([^\]]+)\]");
                    if (sm.Success) { section = sm.Groups[1].Value.ToUpperInvariant(); continue; }
                    if (line.Length == 0 || line.StartsWith(";")) continue;
                    switch (section)
                    {
                        case "KILLTASKS": d.GoalKinds.Add("杀怪"); break;
                        case "ITEMTASKS": d.GoalKinds.Add("收集"); break;
                        case "CARRYITEMS": d.GoalKinds.Add("携带"); break;
                        case "FLAGTASKS": d.GoalKinds.Add("标志"); break;
                        case "FIXEDREWARDS": case "SELECTREWARDS": case "EXPREWARD":
                        case "GOLDREWARD": case "CREDITREWARD":
                            d.RewardLines.Add(line); break;
                    }
                }
                _details[q.Index] = d;
            }
        }

        private static string TypeName(QuestInfo q)
        {
            int t = (int)q.Type;
            return t switch { 1 => "每日", 2 => "重复", 3 => "主线", _ => "一般" };
        }

        private static Color TypeColor(QuestInfo q)
        {
            return ((int)q.Type) switch
            {
                1 => Color.FromArgb(45, 105, 65),   // 每日-绿
                2 => Color.FromArgb(110, 75, 35),   // 重复-棕
                3 => Color.FromArgb(120, 40, 55),   // 主线-红
                _ => Color.FromArgb(48, 48, 54)     // 一般-灰
            };
        }

        private Rectangle Rect(QuestInfo q)
        {
            var p = _positions[q.Index];
            return new Rectangle(p.X, p.Y, 230, 76);
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TranslateTransform(_canvas.AutoScrollPosition.X, _canvas.AutoScrollPosition.Y);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // 前置任务连线
            foreach (var q in _quests)
            {
                if (q.RequiredQuest <= 0) continue;
                var pre = _quests.FirstOrDefault(p => p.Index == q.RequiredQuest);
                if (pre == null) continue;
                var a = new Point(Rect(pre).X + 230, Rect(pre).Y + 38);
                var b = new Point(Rect(q).X, Rect(q).Y + 38);
                using var pen = new Pen(Color.CornflowerBlue, 2);
                g.DrawLine(pen, a, b);
            }

            foreach (var q in _quests)
            {
                var r = Rect(q);
                bool sel = ReferenceEquals(q, _selected);
                using var brush = new SolidBrush(sel ? Color.FromArgb(70, 90, 130) : TypeColor(q));
                g.FillRectangle(brush, r);
                using var pen = new Pen(sel ? Color.Yellow : Color.Gray, sel ? 2 : 1);
                g.DrawRectangle(pen, r);
                TextRenderer.DrawText(g, $"#{q.Index} {q.Name}", Font,
                    new Point(r.X + 8, r.Y + 8), Color.Wheat);
                using var f = new Font("Microsoft YaHei UI", 8F);
                TextRenderer.DrawText(g, TypeName(q), f,
                    new Point(r.X + r.Width - 50, r.Y + 8), Color.LightYellow);
                _details.TryGetValue(q.Index, out var detail);
                string goals = detail != null && detail.GoalKinds.Count > 0
                    ? string.Join("/", detail.GoalKinds) : "运行时加载";
                string info = $"[{TypeName(q)}] 等级 {q.RequiredMinLevel}-{q.RequiredMaxLevel}  前置:{q.RequiredQuest}\r\n" +
                              $"目标: {goals}  金币{q.GoldReward} 经验{q.ExpReward}";
                TextRenderer.DrawText(g, info, f,
                    new Rectangle(r.X + 8, r.Y + 30, r.Width - 16, r.Height - 34),
                    Color.WhiteSmoke, TextFormatFlags.WordBreak);
            }

            if (_selected != null)
            {
                using var f = new Font("Microsoft YaHei UI", 9F);
                string detailText;
                if (_details.TryGetValue(_selected.Index, out var dd) && dd.RewardLines.Count > 0)
                    detailText = "奖励明细: " + string.Join(" | ", dd.RewardLines.Take(4));
                else
                {
                    var d = _selected.Description.Count > 0 ? _selected.Description[0] : "";
                    detailText = "选中: " + d + "（目标/奖励明细运行时从 Quests 文本加载）";
                }
                TextRenderer.DrawText(g, detailText, f, new Point(30, _canvas.Height - 40), Color.Yellow);
            }
        }
    }
}
