using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// NPC 对话流程图编辑器：把脚本的 [@段名] 可视化为节点，
    /// &lt;文字/@目标&gt; / &lt;&lt;文字/@目标/颜色&gt;&gt; 为跳转连线。
    /// 节点可拖动布局，双击编辑段内容；保存回写 UTF-8 无 BOM 脚本。
    /// </summary>
    public class DialogueFlowForm : Form
    {
        internal enum NodeKind { Dialogue, Condition, Event, Subgraph }

        internal class Node : DialogueNodeRef
        {
            public List<string> Targets = new(); // 跳转目标段名
            public Rectangle Rect;
            public string SayText => string.Join(" ", Lines
                .Where(l => !l.StartsWith("#") && !l.TrimStart().StartsWith("<"))
                .Select(l => l.Trim()).Where(s => s.Length > 0));

            /// <summary>按段内首命令分类节点类型（类型化对话图）。</summary>
            public NodeKind Kind
            {
                get
                {
                    bool sub = Lines.Any(l => { var t = l.TrimStart(); return t.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase); });
                    if (sub) return NodeKind.Subgraph;
                    bool hasIf = Lines.Any(l => l.TrimStart().StartsWith("#IF", StringComparison.OrdinalIgnoreCase));
                    bool check = Lines.Any(l => { var t = l.TrimStart(); return t.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) || t.StartsWith("RANDOM", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ISADMIN", StringComparison.OrdinalIgnoreCase) || t.StartsWith("INGUILD", StringComparison.OrdinalIgnoreCase); });
                    if (hasIf && check) return NodeKind.Condition;
                    bool hasAct = Lines.Any(l => l.TrimStart().StartsWith("#ACT", StringComparison.OrdinalIgnoreCase));
                    bool action = Lines.Any(l => { var t = l.TrimStart(); return t.StartsWith("GOTO", StringComparison.OrdinalIgnoreCase) || t.StartsWith("CALL", StringComparison.OrdinalIgnoreCase) || t.StartsWith("GIVE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("TAKE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("MOV", StringComparison.OrdinalIgnoreCase) || t.StartsWith("CALC", StringComparison.OrdinalIgnoreCase) || t.StartsWith("SET", StringComparison.OrdinalIgnoreCase) || t.StartsWith("MONGEN", StringComparison.OrdinalIgnoreCase); });
                    if (hasAct && action) return NodeKind.Event;
                    return NodeKind.Dialogue;
                }
            }
        }

        private readonly string _scriptPath;
        private readonly List<Node> _nodes = new();
        // 段头之前的内容（注释、#INSERT 指令等）必须原样保留：
        // 00Default.txt 全篇只有 #INSERT 前言、无任何 [@段]，丢弃会导致保存后文件被清空。
        private readonly List<string> _preamble = new();
        private Node _drag;
        private Point _dragOffset;
        private Node _selected;

        private static readonly Regex JumpRx = new(
            @"<{1,2}\s*([^/@>]+?)\s*/@([A-Za-z0-9_\-\(\)]+?)(?:/[^>]*?)?>{1,2}",
            RegexOptions.Compiled);
        // GOTO/GROUPGOTO/ROLLDIE/ROLLYUT 第2词；DELAYGOTO/TIMERECALL* 第3词
        private static readonly Regex Goto2Rx = new(
            @"^\s*(GOTO|GROUPGOTO|ROLLDIE|ROLLYUT)\s+@?([A-Za-z0-9_\-\(\)]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Goto3Rx = new(
            @"^\s*(DELAYGOTO|TIMERECALL|TIMERECALLGROUP)\s+\S+\s+@?([A-Za-z0-9_\-\(\)]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public DialogueFlowForm(string scriptPath, string npcName)
        {
            _scriptPath = scriptPath;
            Text = $"对话流程图 — {npcName}";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 720);

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 38 };
            toolbar.Controls.Add(new Button { Text = "新建段", Location = new Point(10, 5), Width = 80 });
            toolbar.Controls[0].Click += (s, e) => NewNode();
            var btnEdit = new Button { Text = "编辑所选", Location = new Point(100, 5), Width = 80 };
            btnEdit.Click += (s, e) => EditSelected();
            var btnDel = new Button { Text = "删除所选", Location = new Point(190, 5), Width = 80 };
            btnDel.Click += (s, e) => DeleteSelected();
            var btnSave = new Button { Text = "保存脚本", Location = new Point(290, 5), Width = 90 };
            btnSave.Click += (s, e) => SaveScript();
            var btnFit = new Button { Text = "自动布局", Location = new Point(390, 5), Width = 90 };
            btnFit.Click += (s, e) => { AutoLayout(); Invalidate(); };
            toolbar.Controls.AddRange(new Control[] { btnEdit, btnDel, btnSave, btnFit });
            Controls.Add(toolbar);

            _canvas = new BufferedCanvasPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(30, 30, 34) };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseDoubleClick += (s, e) => EditSelected();
            Controls.Add(_canvas);

            ParseScript();
            AutoLayout();
        }

        private readonly Panel _canvas;

        private void ParseScript()
        {
            _nodes.Clear();
            _preamble.Clear();
            if (!File.Exists(_scriptPath)) return;
            string[] lines = File.ReadAllLines(_scriptPath);
            Node cur = null;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                var m = Regex.Match(line, @"^\[@([^\]]+)\]");
                if (m.Success)
                {
                    cur = new Node { Name = m.Groups[1].Value };
                    _nodes.Add(cur);
                    continue;
                }
                if (cur == null)
                {
                    // 段外内容（注释、#INSERT 等）原样保留，保存时写回文件头。
                    _preamble.Add(line);
                    continue;
                }
                cur.Lines.Add(line);
                CollectTargets(cur, line);
            }
        }

        /// <summary>
        /// 收集一行中的跳转目标（按钮 + GOTO 命令行），供解析与编辑共用，
        /// 避免编辑段后只重建按钮边、丢失 GOTO/DELAYGOTO 命令行跳转边。
        /// </summary>
        private static void CollectTargets(Node n, string line)
        {
            foreach (Match j in JumpRx.Matches(line))
            {
                string target = j.Groups[2].Value;
                if (!n.Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    n.Targets.Add(target);
            }
            var g2 = Goto2Rx.Match(line);
            var g3 = g2.Success ? Match.Empty : Goto3Rx.Match(line);
            if (g2.Success || g3.Success)
            {
                string target = (g2.Success ? g2.Groups[2] : g3.Groups[2]).Value;
                if (!n.Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    n.Targets.Add(target);
            }
        }

        /// <summary>按所有节点的实际范围扩展滚动区，避免节点拖出可视区后不可达。</summary>
        private void UpdateScrollSize()
        {
            int maxX = 400, maxY = 700;
            foreach (var n in _nodes)
            {
                maxX = Math.Max(maxX, n.Rect.Right + 240);
                maxY = Math.Max(maxY, n.Rect.Bottom + 60);
            }
            _canvas.AutoScrollMinSize = new Size(maxX, maxY);
        }

        private void AutoLayout()
        {
            int x = 30, y = 30;
            foreach (var n in _nodes)
            {
                n.Rect = new Rectangle(x, y, 200, 110);
                y += n.Rect.Height + 30;
                if (y > 600) { y = 30; x += 240; }
            }
            UpdateScrollSize();
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TranslateTransform(_canvas.AutoScrollPosition.X, _canvas.AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var drawFont = new Font("Microsoft YaHei UI", 9F);

            // 连线（贝塞尔）：按目标聚合，避免多源指向同一节点时箭头圆点与文字重叠。
            var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _nodes)
            {
                foreach (var t in n.Targets)
                {
                    var dst = _nodes.FirstOrDefault(d => d.Name.Equals(t, StringComparison.OrdinalIgnoreCase));
                    if (dst == null) continue;
                    var a = new Point(n.Rect.X + n.Rect.Width / 2, n.Rect.Bottom);
                    var b = new Point(dst.Rect.X + dst.Rect.Width / 2, dst.Rect.Top);
                    using var pen = new Pen(Color.CornflowerBlue, 2);
                    g.DrawBezier(pen, a, new Point(a.X, a.Y + 60), new Point(b.X, b.Y - 60), b);
                    incoming.TryGetValue(t, out int idx);
                    incoming[t] = idx + 1;
                    if (idx == 0) // 箭头圆点只画一次
                        g.FillEllipse(Brushes.CornflowerBlue, b.X - 4, b.Y - 4, 8, 8);
                    TextRenderer.DrawText(g, "→" + t, drawFont,
                        new Point(b.X + 6, b.Y - 8 - idx * 14), Color.LightBlue);
                }
            }

            // 节点（按类型着色）
            foreach (var n in _nodes)
            {
                bool sel = ReferenceEquals(n, _selected);
                Color kindColor = n.Kind switch
                {
                    NodeKind.Condition => Color.FromArgb(120, 85, 20),   // 条件-琥珀
                    NodeKind.Event => Color.FromArgb(30, 95, 120),      // 事件-青
                    NodeKind.Subgraph => Color.FromArgb(95, 45, 110),   // 子图-紫
                    _ => Color.FromArgb(48, 48, 54)                     // 对话-灰
                };
                using var brush = new SolidBrush(sel ? Color.FromArgb(70, 90, 130) : kindColor);
                g.FillRectangle(brush, n.Rect);
                using var pen = new Pen(sel ? Color.Yellow : Color.Gray, sel ? 2 : 1);
                g.DrawRectangle(pen, n.Rect);
                string title = "[@" + n.Name + "]";
                if (title.Length > 18) title = title[..18] + "..."; // 防止长段名与类型标签重叠
                TextRenderer.DrawText(g, title, drawFont, new Point(n.Rect.X + 8, n.Rect.Y + 6), Color.Wheat);
                // 统一使用普通中文方括号，避免特殊字形在部分字体下显示为方框。
                string tag = n.Kind switch
                {
                    NodeKind.Condition => "[条件]", NodeKind.Event => "[事件]",
                    NodeKind.Subgraph => "[子图]", _ => "[对话]"
                };
                TextRenderer.DrawText(g, tag, drawFont, new Point(n.Rect.X + n.Rect.Width - 64, n.Rect.Y + 6), Color.LightYellow);
                string say = n.SayText;
                if (say.Length > 60) say = say[..60] + "...";
                TextRenderer.DrawText(g, say, drawFont,
                    new Rectangle(n.Rect.X + 8, n.Rect.Y + 28, n.Rect.Width - 16, n.Rect.Height - 36),
                    Color.WhiteSmoke, TextFormatFlags.WordBreak);
            }
        }

        private Node NodeAt(Point p)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
                if (_nodes[i].Rect.Contains(p)) return _nodes[i];
            return null;
        }

        private Point CanvasPoint(MouseEventArgs e) =>
            new(e.X - _canvas.AutoScrollPosition.X, e.Y - _canvas.AutoScrollPosition.Y);

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            var p = CanvasPoint(e);
            var n = NodeAt(p);
            _selected = n;
            if (n != null && e.Button == MouseButtons.Left)
            {
                _drag = n;
                _dragOffset = new Point(p.X - n.Rect.X, p.Y - n.Rect.Y);
            }
            _canvas.Invalidate();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag == null || e.Button != MouseButtons.Left) return;
            var p = CanvasPoint(e);
            // 钳制到非负坐标，并随节点实际范围扩展滚动区，防止拖出可视区后无法找回。
            int nx = Math.Max(0, p.X - _dragOffset.X);
            int ny = Math.Max(0, p.Y - _dragOffset.Y);
            _drag.Rect = new Rectangle(nx, ny, _drag.Rect.Width, _drag.Rect.Height);
            UpdateScrollSize();
            _canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e) => _drag = null;

        private void NewNode()
        {
            string name = "NEW" + (_nodes.Count + 1);
            var dlg = new TextInputForm("新建段", "段名（不含@）:", name);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            name = dlg.Value.Trim().TrimStart('@');
            if (name.Length == 0) return;
            // 段名重复会导致连线只命中第一个同名段、跳转错乱，提前拒绝。
            if (_nodes.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "段名已存在，请使用其它名称。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var n = new Node { Name = name };
            n.Lines.AddRange(new[] { "#SAY", "在这里输入对话内容。", "<返回/@MAIN> <关闭/@Exit>" });
            n.Targets.Add("MAIN");
            n.Rect = new Rectangle(30, 30 + _nodes.Count * 140, 200, 110);
            _nodes.Add(n);
            _selected = n;
            UpdateScrollSize();
            // 段很多时新节点会落在视口外；滚动到让新节点可见，避免"选中了却看不见"。
            int targetY = n.Rect.Bottom - _canvas.ClientSize.Height + 60;
            if (targetY > _canvas.AutoScrollPosition.Y)
                _canvas.AutoScrollPosition = new Point(0, targetY);
            _canvas.Invalidate();
        }

        private void EditSelected()
        {
            if (_selected == null) { MessageBox.Show("请先选择一个段。"); return; }
            string oldName = _selected.Name;
            var dlg = new NodeEditForm(_selected);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string newName = _selected.Name;
                // 段名与其它段冲突则恢复原名，避免 [@段] 重复导致连线命中错误目标。
                if (_nodes.Any(x => !ReferenceEquals(x, _selected) &&
                                    x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                {
                    newName = oldName;
                    _selected.Name = oldName;
                    MessageBox.Show(this, "段名与其它段重复，已保留原名。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                // 段名变更时同步重写其它段的跳转目标，避免改名后入边静默断链。
                if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var n in _nodes)
                        for (int i = 0; i < n.Targets.Count; i++)
                            if (string.Equals(n.Targets[i], oldName, StringComparison.OrdinalIgnoreCase))
                                n.Targets[i] = newName;
                }
                // 复用与解析相同的跳转收集逻辑（按钮 + GOTO 命令行）。
                _selected.Targets.Clear();
                foreach (var line in _selected.Lines)
                    CollectTargets(_selected, line);
                UpdateScrollSize();
                _canvas.Invalidate();
            }
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            if (MessageBox.Show($"删除段 [@{_selected.Name}]？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _nodes.Remove(_selected);
                _selected = null;
                UpdateScrollSize();
                _canvas.Invalidate();
            }
        }

        private void SaveScript()
        {
            try
            {
                var sb = new StringBuilder();
                // 先写回段外前言（#INSERT 指令、注释等），原样保留，防止文件被清空。
                foreach (var l in _preamble) sb.AppendLine(l);
                if (_preamble.Count > 0 && _nodes.Count > 0) sb.AppendLine();
                foreach (var n in _nodes)
                {
                    sb.AppendLine("[@" + n.Name + "]");
                    foreach (var l in n.Lines) sb.AppendLine(l);
                    sb.AppendLine();
                }
                var dir = Path.GetDirectoryName(_scriptPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_scriptPath, sb.ToString(), new UTF8Encoding(false));
                MessageBox.Show(this, "脚本已保存。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>启用双缓冲的画布 Panel，避免拖动节点时整屏闪烁。</summary>
    internal sealed class BufferedCanvasPanel : Panel
    {
        public BufferedCanvasPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    /// <summary>通用单行文本输入对话框。</summary>
    public class TextInputForm : Form
    {
        private readonly TextBox _txt;
        public string Value => _txt.Text;
        public TextInputForm(string title, string label, string initial)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 100);
            Controls.Add(new Label { Text = label, Location = new Point(15, 16), AutoSize = true });
            _txt = new TextBox { Location = new Point(15, 38), Width = 330, Text = initial };
            var ok = new Button { Text = "确定", Location = new Point(195, 66), Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Location = new Point(280, 66), Width = 65, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { _txt, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    /// <summary>段内容编辑：段名 + 多行脚本。</summary>
    public class NodeEditForm : Form
    {
        private readonly TextBox _txtName, _txtBody;
        private readonly DialogueNodeRef _node;
        public NodeEditForm(DialogueNodeRef node)
        {
            _node = node;
            Text = "编辑段";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 560);
            Controls.Add(new Label { Text = "段名:", Location = new Point(15, 15), AutoSize = true });
            _txtName = new TextBox { Location = new Point(70, 12), Width = 500, Text = node.Name };
            _txtBody = new TextBox
            {
                Location = new Point(15, 48), Size = new Size(580, 430), Multiline = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10F),
                Text = string.Join("\r\n", node.Lines)
            };
            var ok = new Button { Text = "确定", Location = new Point(340, 490), Width = 90, DialogResult = DialogResult.OK };
            ok.Click += (s, e) =>
            {
                string name = _txtName.Text.Trim().TrimStart('@');
                if (name.Length == 0) // 空段名保持原值，避免生成空段头 [@]
                    name = node.Name;
                node.Name = name;
                node.Lines.Clear();
                node.Lines.AddRange(_txtBody.Text.Replace("\r\n", "\n").Split('\n'));
            };
            var cancel = new Button { Text = "取消", Location = new Point(450, 490), Width = 80, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { _txtName, _txtBody, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>节点适配接口（供 NodeEditForm 使用，避免暴露私有 Node）。</summary>
    public class DialogueNodeRef
    {
        public string Name { get; set; }
        public List<string> Lines { get; set; } = new();
    }
}
