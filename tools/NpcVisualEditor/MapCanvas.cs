using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;

namespace NpcVisualEditor
{
    /// <summary>
    /// 地图画布：绘制小地图 + NPC 标记，支持点选、拖动改坐标、双击编辑、右键菜单。
    /// 复刻原版 DXDraw1 画布交互（以 GDI+ 实现）。
    /// </summary>
    public class MapCanvas : Control
    {
        private readonly string _clientDir;
        private readonly MainForm _owner;

        private MapInfo _map;
        private List<NPCInfo> _npcs = new();
        private Image _minimap;          // 原图
        private Image _scaled;           // 缩放后的缓存
        private NPCInfo _hover;
        private NPCInfo _drag;
        private Point _dragOffset;
        private float _scale = 1f;

        public Action<NPCInfo> NpcActivated { get; set; }
        public Action<NPCInfo> NpcMoved { get; set; }
        public Action<NPCInfo> NpcDeleted { get; set; }
        public Action<Point> NewNpcRequested { get; set; }

        public MapCanvas(string clientDir, MainForm owner)
        {
            _clientDir = clientDir;
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 24);
            Cursor = Cursors.Hand;
        }

        public MapInfo CurrentMap => _map;

        public void LoadMap(MapInfo map, List<NPCInfo> npcs)
        {
            _map = map;
            SetNpcs(npcs);
            LoadMinimap();
            Invalidate();
        }

        public void SetNpcs(List<NPCInfo> npcs)
        {
            _npcs = npcs ?? new List<NPCInfo>();
            Invalidate();
        }

        private void LoadMinimap()
        {
            _minimap?.Dispose();
            _minimap = null;
            _scaled?.Dispose();
            _scaled = null;

            if (_map == null || string.IsNullOrEmpty(_clientDir)) return;
            string png = Path.Combine(_clientDir, "Map", "MiniMaps_Export", _map.FileName + "_MiniMap.png");
            if (!File.Exists(png)) return;
            try
            {
                using var fs = new FileStream(png, FileMode.Open, FileAccess.Read);
                _minimap = Image.FromStream(fs);
            }
            catch { _minimap = null; }
        }

        private Rectangle MapRect()
        {
            if (_minimap == null) return Rectangle.Empty;
            int pad = 8;
            int w = Width - pad * 2, h = Height - pad * 2;
            if (w <= 0 || h <= 0) return Rectangle.Empty;
            float ratio = Math.Min((float)w / _minimap.Width, (float)h / _minimap.Height);
            _scale = ratio;
            return new Rectangle(pad, pad, (int)(_minimap.Width * ratio), (int)(_minimap.Height * ratio));
        }

        private Point MapToClient(Point mapPoint)
        {
            var r = MapRect();
            if (r.IsEmpty) return Point.Empty;
            return new Point(r.X + (int)(mapPoint.X * _scale), r.Y + (int)(mapPoint.Y * _scale));
        }

        private Point ClientToMap(Point clientPoint)
        {
            var r = MapRect();
            if (r.IsEmpty) return Point.Empty;
            return new Point((int)((clientPoint.X - r.X) / _scale), (int)((clientPoint.Y - r.Y) / _scale));
        }

        private NPCInfo NpcAt(Point clientPoint)
        {
            foreach (var npc in _npcs)
            {
                var p = MapToClient(npc.Location);
                if (Math.Abs(p.X - clientPoint.X) <= 8 && Math.Abs(p.Y - clientPoint.Y) <= 8)
                    return npc;
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_map == null)
            {
                using var f = new Font("Microsoft YaHei UI", 12F);
                TextRenderer.DrawText(g, "请选择地图", f, ClientRectangle, Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var r = MapRect();

            if (_minimap != null && !r.IsEmpty)
            {
                if (_scaled == null || _scaled.Width != r.Width || _scaled.Height != r.Height)
                {
                    _scaled?.Dispose();
                    _scaled = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
                    using (var gg = Graphics.FromImage(_scaled))
                        gg.DrawImage(_minimap, 0, 0, _scaled.Width, _scaled.Height);
                }
                g.DrawImage(_scaled, r);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(40, 40, 40));
                g.FillRectangle(brush, r.IsEmpty ? ClientRectangle : r);
                TextRenderer.DrawText(g, $"（缺少小地图: {_map.FileName}_MiniMap.png）",
                    Font, r.IsEmpty ? ClientRectangle : r, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // 安全区（绿色半透明矩形）
            if (_map.SafeZones != null)
                foreach (var sz in _map.SafeZones)
                {
                    var p = MapToClient(sz.Location);
                    int size = Math.Max(8, (int)(sz.Size * _scale));
                    using var brush = new SolidBrush(Color.FromArgb(60, Color.LimeGreen));
                    g.FillRectangle(brush, p.X - size / 2, p.Y - size / 2, size, size);
                    using var pen = new Pen(Color.LimeGreen);
                    g.DrawRectangle(pen, p.X - size / 2, p.Y - size / 2, size, size);
                }

            // NPC 标记
            foreach (var npc in _npcs)
            {
                var p = MapToClient(npc.Location);
                bool selected = ReferenceEquals(npc, _hover);
                using var brush = new SolidBrush(selected ? Color.Yellow : Color.OrangeRed);
                g.FillEllipse(brush, p.X - 5, p.Y - 5, 10, 10);
                using var pen = new Pen(selected ? Color.Black : Color.White);
                g.DrawEllipse(pen, p.X - 5, p.Y - 5, 10, 10);
                TextRenderer.DrawText(g, npc.GameName, Font, new Point(p.X + 8, p.Y - 8),
                    selected ? Color.Yellow : Color.White);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != null && e.Button == MouseButtons.Left)
            {
                var mp = ClientToMap(e.Location);
                _drag.Location = new Point(mp.X - _dragOffset.X, mp.Y - _dragOffset.Y);
                Invalidate();
            }
            else
            {
                var hover = NpcAt(e.Location);
                if (!ReferenceEquals(hover, _hover))
                {
                    _hover = hover;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var npc = NpcAt(e.Location);
            if (e.Button == MouseButtons.Left && npc != null)
            {
                _drag = npc;
                var mp = ClientToMap(e.Location);
                _dragOffset = new Point(mp.X - npc.Location.X, mp.Y - npc.Location.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag != null && e.Button == MouseButtons.Left)
            {
                var moved = _drag;
                _drag = null;
                NpcMoved?.Invoke(moved);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var npc = NpcAt(e.Location);
            if (npc != null) NpcActivated?.Invoke(npc);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Right) return;

            var npc = NpcAt(e.Location);
            var menu = new ContextMenuStrip();

            if (npc != null)
            {
                menu.Items.Add("编辑NPC", null, (s, a) => NpcActivated?.Invoke(npc));
                menu.Items.Add("编辑脚本", null, (s, a) => NpcActivated?.Invoke(npc));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("删除NPC", null, (s, a) => NpcDeleted?.Invoke(npc));
            }
            else
            {
                var mp = ClientToMap(e.Location);
                menu.Items.Add("在此新建NPC", null, (s, a) => NewNpcRequested?.Invoke(mp));
            }
            menu.Show(this, e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _minimap?.Dispose();
                _scaled?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
