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
        private readonly int _currentId;
        private readonly List<(int id, string path)> _items = new();
        private readonly Dictionary<int, Image> _thumbs = new();
        private readonly System.Windows.Forms.Timer _filterTimer;

        // 无预览图时的灰色占位图（进程级共享，尺寸很小）
        private static readonly Lazy<Bitmap> s_placeholder = new(() => CreatePlaceholder());

        // 单次渲染上限：Items 类别实测 2863 个，全部渲染会卡顿；超出后提示按 ID 筛选
        private const int MaxCards = 6000;

        public ImagePickerForm(string serverDir, string category = "NPC", int currentId = -1)
        {
            _currentId = currentId;
            _previewDir = Path.Combine(serverDir, "Envir", "Previews", category);
            Text = $"外观选择器 — {category}";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 680);

            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "筛选ID:", Location = new Point(12, 13), AutoSize = true });
            _txtSearch = new TextBox { Location = new Point(70, 9), Width = 200 };
            // 防抖：连续输入时只在停顿后重建卡片，避免每次按键重建最多 6000 张卡片导致卡顿闪烁
            _filterTimer = new System.Windows.Forms.Timer { Interval = 180 };
            _filterTimer.Tick += (s, e) => { _filterTimer.Stop(); BuildGrid(_txtSearch.Text.Trim()); };
            _txtSearch.TextChanged += (s, e) => { _filterTimer.Stop(); _filterTimer.Start(); };
            top.Controls.Add(_txtSearch);
            _lblInfo = new Label { Location = new Point(290, 13), AutoSize = true, ForeColor = Color.SteelBlue };
            top.Controls.Add(_lblInfo);
            Controls.Add(top);

            _grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            Controls.Add(_grid);

            LoadPreviews();
            BuildGrid(null);
        }

        private static Bitmap CreatePlaceholder()
        {
            var bmp = new Bitmap(72, 60);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(235, 235, 240));
                using var brush = new SolidBrush(Color.Gray);
                using var font = new Font("Microsoft YaHei UI", 9F);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("无预览图", font, brush, new RectangleF(0, 0, 72, 60), sf);
            }
            return bmp;
        }

        private void LoadPreviews()
        {
            _items.Clear();
            if (!Directory.Exists(_previewDir))
            {
                _lblInfo.Text = "预览目录不存在：" + _previewDir;
                return;
            }
            try
            {
                foreach (var f in Directory.GetFiles(_previewDir, "*.bmp"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (int.TryParse(name, out int id)) _items.Add((id, f));
                }
                _items.Sort((a, b) => a.id.CompareTo(b.id));
            }
            catch (Exception ex)
            {
                _lblInfo.Text = "读取预览目录失败：" + ex.Message;
            }
        }

        private void BuildGrid(string filter)
        {
            // 先释放旧卡片控件（Dispose 会从集合移除自身，故先快照 ToList 避免遍历中修改集合）
            // 注意：不释放 pic.Image —— 它指向共享的 _thumbs 缓存
            foreach (var c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
            _grid.Controls.Clear();

            var list = string.IsNullOrEmpty(filter)
                ? _items
                : _items.Where(i => i.id.ToString().Contains(filter)).ToList();
            var shown = list.Take(MaxCards).ToList();

            var info = $"共 {list.Count} 个外观";
            if (_currentId >= 0) info += $"（当前ID: {_currentId}）";
            if (list.Count > shown.Count) info += $"，仅显示前 {shown.Count} 个，请按 ID 筛选";
            _lblInfo.Text = info;

            foreach (var (id, path) in shown)
            {
                var card = new Panel { Size = new Size(80, 86), BorderStyle = BorderStyle.FixedSingle, Tag = id };
                var pic = new PictureBox
                {
                    Size = new Size(72, 60), Location = new Point(3, 3),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                pic.Image = LoadThumb(id, path) ?? s_placeholder.Value;
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

        /// <summary>加载并深拷贝缩略图，脱离 FileStream 生命周期；失败返回 null 由调用方显示占位图。</summary>
        private Image LoadThumb(int id, string path)
        {
            if (_thumbs.TryGetValue(id, out var cached)) return cached;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var tmp = Image.FromStream(fs);
                var img = new Bitmap(tmp); // 深拷贝：Image.FromStream 要求流在图像生命周期内保持打开
                _thumbs[id] = img;
                return img;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"外观缩略图加载失败 {path}: {ex.Message}");
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _filterTimer?.Dispose();
                foreach (var img in _thumbs.Values) img.Dispose();
                _thumbs.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
