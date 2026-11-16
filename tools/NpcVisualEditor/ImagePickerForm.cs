using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 图库ID选择器：可视化浏览 Previews\{NPC|Monsters|Items}\ 下的 {ID}.bmp，
    /// 点选后返回外观ID，供 NPC/物品/怪物外观字段使用（图库ID桥接）。
    /// </summary>
    public class ImagePickerForm : Form
    {
        public int SelectedId { get; private set; } = -1;

        private readonly string _previewDir;
        private readonly FlowLayoutPanel _grid;
        private readonly TextBox _txtSearch;
        private readonly Label _lblInfo;
        private readonly List<(int id, string path)> _items = new();
        private readonly Dictionary<int, Image> _thumbs = new();

        public ImagePickerForm(string serverDir, string category = "NPC", int currentId = -1)
        {
            _previewDir = Path.Combine(serverDir, "Envir", "Previews", category);
            Text = $"外观选择器 — {category}";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 680);

            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "筛选ID:", Location = new Point(12, 12), AutoSize = true });
            _txtSearch = new TextBox { Location = new Point(70, 9), Width = 200 };
            _txtSearch.TextChanged += (s, e) => BuildGrid(_txtSearch.Text.Trim());
            top.Controls.Add(_txtSearch);
            _lblInfo = new Label { Location = new Point(300, 12), AutoSize = true, ForeColor = Color.SteelBlue };
            top.Controls.Add(_lblInfo);
            Controls.Add(top);

            _grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            Controls.Add(_grid);

            LoadPreviews();
            BuildGrid(null);
            if (currentId >= 0) _lblInfo.Text = $"当前ID: {currentId}";
        }

        private void LoadPreviews()
        {
            _items.Clear();
            if (!Directory.Exists(_previewDir)) return;
            foreach (var f in Directory.GetFiles(_previewDir, "*.bmp"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (int.TryParse(name, out int id)) _items.Add((id, f));
            }
            _items.Sort((a, b) => a.id.CompareTo(b.id));
        }

        private void BuildGrid(string filter)
        {
            _grid.Controls.Clear();
            var list = string.IsNullOrEmpty(filter)
                ? _items
                : _items.Where(i => i.id.ToString().Contains(filter)).ToList();
            _lblInfo.Text = $"共 {list.Count} 个外观" + (_lblInfo.Text.Contains("当前") ? "  " + _lblInfo.Text : "");

            foreach (var (id, path) in list.Take(2000))
            {
                var card = new Panel { Size = new Size(80, 86), BorderStyle = BorderStyle.FixedSingle, Tag = id };
                var pic = new PictureBox
                {
                    Size = new Size(72, 60), Location = new Point(3, 3),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                try
                {
                    if (!_thumbs.TryGetValue(id, out var img))
                    {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                        img = Image.FromStream(fs);
                        _thumbs[id] = img;
                    }
                    pic.Image = _thumbs[id];
                }
                catch { }
                var lbl = new Label
                {
                    Text = id.ToString(), Dock = DockStyle.Bottom, Height = 20,
                    TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(220, 220, 230)
                };
                card.Controls.Add(pic);
                card.Controls.Add(lbl);
                card.Click += (s, e) => { SelectedId = id; DialogResult = DialogResult.OK; Close(); };
                _grid.Controls.Add(card);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var img in _thumbs.Values) img.Dispose();
            base.Dispose(disposing);
        }
    }
}
