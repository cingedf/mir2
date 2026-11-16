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
            Controls.Add(_tree);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(new Label { Text = "命令说明 / 范例:", Location = new Point(12, 12), AutoSize = true });
            _txtDetail = new TextBox
            {
                Location = new Point(12, 38), Size = new Size(560, 520), Multiline = true,
                ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F)
            };
            var btnCopy = new Button { Text = "复制命令", Location = new Point(12, 575), Width = 100 };
            btnCopy.Click += (s, e) =>
            {
                string text = _txtDetail.Lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                if (text.Length > 0) { Clipboard.SetText(text); MessageBox.Show("已复制: " + text); }
            };
            right.Controls.AddRange(new Control[] { _txtDetail, btnCopy });
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

        private void RefreshList()
        {
            _lstBuffs.BeginUpdate();
            _lstBuffs.Items.Clear();
            foreach (var l in _lines) _lstBuffs.Items.Add(l.Split(';')[0]);
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
                "MESSAGE 已附加增益：" + name + "\r\nBREAK\r\n";
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0) { MessageBox.Show("请填写 BUFF 名称。"); return; }
            var props = _txtProps.Text.Replace("\r\n", "\n").Split('\n')
                .Select(p => p.Trim()).Where(p => p.Length > 0);
            string line = name + ";" + string.Join(";", props) + ";;";
            int idx = _lines.FindIndex(l => l.Split(';')[0] == name);
            if (idx >= 0) _lines[idx] = line; else _lines.Add(line);
            RefreshList();
            MessageBox.Show("已加入列表，记得点“保存到 SetBuffs.txt”。");
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                File.WriteAllLines(_buffsPath, _lines, new UTF8Encoding(false));
                MessageBox.Show(this, "SetBuffs.txt 已保存。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message); }
        }
    }

    /// <summary>
    /// 任务链流程图：把 QuestInfo 按 RequiredQuest 串成链，节点显示名称/等级/奖励。
    /// </summary>
    public class QuestFlowForm : Form
    {
        private readonly List<QuestInfo> _quests;
        private readonly Panel _canvas;
        private readonly Dictionary<int, Point> _positions = new();
        private QuestInfo _selected;

        public QuestFlowForm(IEnumerable<QuestInfo> quests)
        {
            _quests = quests.OrderBy(q => q.Index).ToList();
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
                using var brush = new SolidBrush(sel ? Color.FromArgb(70, 90, 130) : Color.FromArgb(48, 48, 54));
                g.FillRectangle(brush, r);
                using var pen = new Pen(sel ? Color.Yellow : Color.Gray, sel ? 2 : 1);
                g.DrawRectangle(pen, r);
                TextRenderer.DrawText(g, $"#{q.Index} {q.Name}", Font,
                    new Point(r.X + 8, r.Y + 8), Color.Wheat);
                using var f = new Font("Microsoft YaHei UI", 8F);
                string info = $"等级 {q.RequiredMinLevel}-{q.RequiredMaxLevel}  前置:{q.RequiredQuest}\r\n" +
                              $"奖励 金币{q.GoldReward} 经验{q.ExpReward}";
                TextRenderer.DrawText(g, info, f,
                    new Rectangle(r.X + 8, r.Y + 30, r.Width - 16, r.Height - 34),
                    Color.WhiteSmoke, TextFormatFlags.WordBreak);
            }

            if (_selected != null)
            {
                using var f = new Font("Microsoft YaHei UI", 9F);
                var d = _selected.Description.Count > 0 ? _selected.Description[0] : "";
                TextRenderer.DrawText(g, "选中: " + d, f, new Point(30, _canvas.Height - 40), Color.Yellow);
            }
        }
    }
}
