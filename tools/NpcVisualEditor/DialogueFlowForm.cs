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
        internal class Node : DialogueNodeRef
        {
            public List<string> Targets = new(); // 跳转目标段名
            public Rectangle Rect;
            public string SayText => string.Join(" ", Lines
                .Where(l => !l.StartsWith("#") && !l.TrimStart().StartsWith("<"))
                .Select(l => l.Trim()).Where(s => s.Length > 0));
        }

        private readonly string _scriptPath;
        private readonly List<Node> _nodes = new();
        private Node _drag;
        private Point _dragOffset;
        private Node _selected;

        private static readonly Regex JumpRx = new(
            @"<{1,2}\s*([^/@>]+?)\s*/@([A-Za-z0-9_]+?)(?:/[^>]*?)?>{1,2}",
            RegexOptions.Compiled);

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

            _canvas = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(30, 30, 34) };
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
                if (cur == null) continue; // 文件头注释等
                cur.Lines.Add(line);
                foreach (Match j in JumpRx.Matches(line))
                {
                    string target = j.Groups[2].Value;
                    if (!cur.Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
                        cur.Targets.Add(target);
                }
            }
        }

        private void AutoLayout()
        {
            int x = 30, y = 30, colH = 0;
            foreach (var n in _nodes)
            {
                n.Rect = new Rectangle(x, y, 200, 110);
                colH = Math.Max(colH, n.Rect.Height + 30);
                y += n.Rect.Height + 30;
                if (y > 600) { y = 30; x += 240; }
            }
            _canvas.AutoScrollMinSize = new Size(x + 240, Math.Max(colH, 700));
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TranslateTransform(_canvas.AutoScrollPosition.X, _canvas.AutoScrollPosition.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 连线（贝塞尔）
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
                    // 箭头
                    g.FillEllipse(Brushes.CornflowerBlue, b.X - 4, b.Y - 4, 8, 8);
                    using var font = new Font("Microsoft YaHei UI", 8F);
                    TextRenderer.DrawText(g, "→" + t, font, new Point(b.X + 6, b.Y - 8), Color.LightBlue);
                }
            }

            // 节点
            foreach (var n in _nodes)
            {
                bool sel = ReferenceEquals(n, _selected);
                using var brush = new SolidBrush(sel ? Color.FromArgb(70, 90, 130) : Color.FromArgb(48, 48, 54));
                g.FillRectangle(brush, n.Rect);
                using var pen = new Pen(sel ? Color.Yellow : Color.Gray, sel ? 2 : 1);
                g.DrawRectangle(pen, n.Rect);
                TextRenderer.DrawText(g, "[@" + n.Name + "]", Font, new Point(n.Rect.X + 8, n.Rect.Y + 6), Color.Wheat);
                using var f2 = new Font("Microsoft YaHei UI", 8F);
                string say = n.SayText;
                if (say.Length > 60) say = say[..60] + "...";
                TextRenderer.DrawText(g, say, f2,
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
            _drag.Rect = new Rectangle(p.X - _dragOffset.X, p.Y - _dragOffset.Y, _drag.Rect.Width, _drag.Rect.Height);
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
            var n = new Node { Name = name };
            n.Lines.AddRange(new[] { "#SAY", "在这里输入对话内容。", "<返回/@MAIN> <关闭/@Exit>" });
            n.Targets.Add("MAIN");
            n.Rect = new Rectangle(30, 30 + _nodes.Count * 140, 200, 110);
            _nodes.Add(n);
            _selected = n;
            _canvas.Invalidate();
        }

        private void EditSelected()
        {
            if (_selected == null) { MessageBox.Show("请先选择一个段。"); return; }
            var dlg = new NodeEditForm(_selected);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _selected.Targets.Clear();
                foreach (var line in _selected.Lines)
                    foreach (Match j in JumpRx.Matches(line))
                    {
                        string t = j.Groups[2].Value;
                        if (!_selected.Targets.Contains(t, StringComparer.OrdinalIgnoreCase))
                            _selected.Targets.Add(t);
                    }
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
                _canvas.Invalidate();
            }
        }

        private void SaveScript()
        {
            try
            {
                var sb = new StringBuilder();
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
                node.Name = _txtName.Text.Trim().TrimStart('@');
                node.Lines.Clear();
                node.Lines.AddRange(_txtBody.Text.Replace("\r\n", "\n").Split('\n'));
            };
            var cancel = new Button { Text = "取消", Location = new Point(450, 490), Width = 80, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { _txtName, _txtBody, ok, cancel });
        }
    }

    /// <summary>节点适配接口（供 NodeEditForm 使用，避免暴露私有 Node）。</summary>
    public class DialogueNodeRef
    {
        public string Name { get; set; }
        public List<string> Lines { get; set; } = new();
    }
}
