using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;

namespace NpcVisualEditor
{
    public enum CanvasMode { Select, Npc, Respawn, SafeZone, Movement }

    /// <summary>
    /// 地图画布：小地图底图 + NPC/刷怪点/安全区/传送门的空间可视化编辑。
    /// 工具模式：选择拖动 / 新建NPC / 框选刷怪点 / 画安全区 / 两点传送门。
    /// </summary>
    public class MapCanvas : Control
    {
        private readonly string _clientDir;
        private readonly MainForm _owner;
        private readonly MinimapCatalog _catalog;

        private MapInfo _map;
        private List<NPCInfo> _npcs = new();
        private Image _minimap;
        private Image _scaled;
        private object _hover;
        private NPCInfo _dragNpc;
        private Point _dragOffset;
        private float _scaleX = 1f, _scaleY = 1f;
        private int _logicalW, _logicalH;   // 地图逻辑格尺寸（来自对照表）

        private Point _dragStart;
        private bool _dragging;
        private Point _moveStart;
        private bool _movePending;

        public CanvasMode Mode { get; set; } = CanvasMode.Select;
        public Func<int, string> MonsterName { get; set; }
        public Func<int, string> MapName { get; set; }

        public Action<NPCInfo> NpcActivated { get; set; }
        public Action<NPCInfo> NpcMoved { get; set; }
        public Action<NPCInfo> NpcDeleted { get; set; }
        public Action<Point> NewNpcRequested { get; set; }
        public Action<RespawnInfo> RespawnCreated { get; set; }
        public Action<RespawnInfo> RespawnActivated { get; set; }
        public Action<RespawnInfo> RespawnDeleted { get; set; }
        public Action<SafeZoneInfo> SafeZoneCreated { get; set; }
        public Action<SafeZoneInfo> SafeZoneActivated { get; set; }
        public Action<SafeZoneInfo> SafeZoneDeleted { get; set; }
        public Action<MovementInfo> MovementCreated { get; set; }
        public Action<MovementInfo> MovementActivated { get; set; }
        public Action<MovementInfo> MovementDeleted { get; set; }

        public MapCanvas(string clientDir, MainForm owner)
        {
            _clientDir = clientDir;
            _owner = owner;
            _catalog = new MinimapCatalog(clientDir);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 24);
            Cursor = Cursors.Cross;
        }

        public MapInfo CurrentMap => _map;

        public void LoadMap(MapInfo map, List<NPCInfo> npcs)
        {
            _map = map;
            // 优先用对照表逻辑格尺寸；缺失时退回 PNG 像素尺寸
            if (map != null && _catalog.TryGet(map.FileName, out int lw, out int lh, out _))
            {
                _logicalW = lw; _logicalH = lh;
            }
            else
            {
                _logicalW = _minimap?.Width ?? 0;
                _logicalH = _minimap?.Height ?? 0;
            }
            SetNpcs(npcs);
            LoadMinimap();
            if (_logicalW <= 0 && _minimap != null)
            {
                _logicalW = _minimap.Width; _logicalH = _minimap.Height;
            }
            Invalidate();
        }

        public void SetNpcs(List<NPCInfo> npcs)
        {
            _npcs = npcs ?? new List<NPCInfo>();
            Invalidate();
        }

        public new void Refresh() => Invalidate();

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
            int pad = 8;
            int w = Width - pad * 2, h = Height - pad * 2;
            if (w <= 0 || h <= 0) return Rectangle.Empty;
            // 底图以逻辑宽高决定纵横比，PNG 拉伸填充该矩形（坐标归一化到格）
            float ratio = _logicalW > 0 && _logicalH > 0
                ? Math.Min((float)w / _logicalW, (float)h / _logicalH)
                : (_minimap != null ? Math.Min((float)w / _minimap.Width, (float)h / _minimap.Height) : 1f);
            int rw = _logicalW > 0 ? (int)(_logicalW * ratio) : (_minimap?.Width ?? w);
            int rh = _logicalH > 0 ? (int)(_logicalH * ratio) : (_minimap?.Height ?? h);
            _scaleX = _logicalW > 0 ? (float)rw / _logicalW : 1f;
            _scaleY = _logicalH > 0 ? (float)rh / _logicalH : 1f;
            return new Rectangle(pad, pad, rw, rh);
        }

        private Point MapToClient(Point mapPoint)
        {
            var r = MapRect();
            if (r.IsEmpty) return Point.Empty;
            return new Point(r.X + (int)(mapPoint.X * _scaleX), r.Y + (int)(mapPoint.Y * _scaleY));
        }

        private Point ClientToMap(Point clientPoint)
        {
            var r = MapRect();
            if (r.IsEmpty) return Point.Empty;
            return new Point(Math.Max(0, (int)((clientPoint.X - r.X) / _scaleX)),
                             Math.Max(0, (int)((clientPoint.Y - r.Y) / _scaleY)));
        }

        private NPCInfo NpcAt(Point c)
        {
            foreach (var npc in _npcs)
            {
                var p = MapToClient(npc.Location);
                if (Math.Abs(p.X - c.X) <= 8 && Math.Abs(p.Y - c.Y) <= 8) return npc;
            }
            return null;
        }

        private RespawnInfo RespawnAt(Point c)
        {
            if (_map == null) return null;
            foreach (var r in _map.Respawns)
            {
                var p = MapToClient(r.Location);
                if (Math.Abs(p.X - c.X) <= 9 && Math.Abs(p.Y - c.Y) <= 9) return r;
            }
            return null;
        }

        private SafeZoneInfo SafeZoneAt(Point c)
        {
            if (_map == null) return null;
            foreach (var s in _map.SafeZones)
            {
                var p = MapToClient(s.Location);
                int half = Math.Max(8, (int)(s.Size / 2 * _scaleX));
                if (Math.Abs(p.X - c.X) <= half && Math.Abs(p.Y - c.Y) <= half) return s;
            }
            return null;
        }

        private MovementInfo MovementAt(Point c)
        {
            if (_map == null) return null;
            foreach (var m in _map.Movements)
            {
                var p = MapToClient(m.Source);
                if (Math.Abs(p.X - c.X) <= 9 && Math.Abs(p.Y - c.Y) <= 9) return m;
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
                    using var gg = Graphics.FromImage(_scaled);
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

            // 传送门（青色连线）
            foreach (var m in _map.Movements)
            {
                var a = MapToClient(m.Source);
                var b = MapToClient(m.Destination);
                using var pen = new Pen(ReferenceEquals(m, _hover) ? Color.Yellow : Color.Cyan, 2);
                g.DrawLine(pen, a, b);
                g.FillEllipse(Brushes.Cyan, a.X - 4, a.Y - 4, 8, 8);
                TextRenderer.DrawText(g, MapName?.Invoke(m.MapIndex) ?? m.MapIndex.ToString(),
                    Font, new Point(b.X + 5, b.Y - 14), Color.Cyan);
            }

            // 安全区（绿色矩形）
            foreach (var s in _map.SafeZones)
            {
                var p = MapToClient(s.Location);
                int sz = Math.Max(8, (int)(s.Size * _scaleX));
                using var brush = new SolidBrush(Color.FromArgb(60, Color.LimeGreen));
                using var pen = new Pen(ReferenceEquals(s, _hover) ? Color.Yellow : Color.LimeGreen);
                g.FillRectangle(brush, p.X - sz / 2, p.Y - sz / 2, sz, sz);
                g.DrawRectangle(pen, p.X - sz / 2, p.Y - sz / 2, sz, sz);
            }

            // 刷怪点（红菱形 + 数量）
            foreach (var rp in _map.Respawns)
            {
                var p = MapToClient(rp.Location);
                bool hot = ReferenceEquals(rp, _hover);
                using var brush = new SolidBrush(hot ? Color.Yellow : Color.Red);
                var pts = new[] { new Point(p.X, p.Y - 7), new Point(p.X + 7, p.Y),
                                  new Point(p.X, p.Y + 7), new Point(p.X - 7, p.Y) };
                g.FillPolygon(brush, pts);
                TextRenderer.DrawText(g, $"{MonsterName?.Invoke(rp.MonsterIndex) ?? rp.MonsterIndex.ToString()}×{rp.Count}",
                    Font, new Point(p.X + 9, p.Y - 9), hot ? Color.Yellow : Color.Salmon);
            }

            // NPC（橙点）
            foreach (var npc in _npcs)
            {
                var p = MapToClient(npc.Location);
                bool hot = ReferenceEquals(npc, _hover);
                using var brush = new SolidBrush(hot ? Color.Yellow : Color.OrangeRed);
                g.FillEllipse(brush, p.X - 5, p.Y - 5, 10, 10);
                using var pen = new Pen(hot ? Color.Black : Color.White);
                g.DrawEllipse(pen, p.X - 5, p.Y - 5, 10, 10);
                TextRenderer.DrawText(g, npc.GameName, Font, new Point(p.X + 8, p.Y - 8),
                    hot ? Color.Yellow : Color.White);
            }

            // 正在框选/连线的橡皮筋
            if (_dragging && (Mode == CanvasMode.Respawn || Mode == CanvasMode.SafeZone))
            {
                using var pen = new Pen(Color.Yellow, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(pen, Rectangle.FromLTRB(
                    Math.Min(_dragStart.X, _dragStart.X), Math.Min(_dragStart.Y, _dragStart.Y),
                    _dragStart.X, _dragStart.Y));
                // 使用当前鼠标位置的临时矩形由 OnMouseMove 更新 _dragStart 终点，见 _dragEnd
                var rc = Rectangle.FromLTRB(Math.Min(_dragStart.X, _dragEnd.X), Math.Min(_dragStart.Y, _dragEnd.Y),
                                            Math.Max(_dragStart.X, _dragEnd.X), Math.Max(_dragStart.Y, _dragEnd.Y));
                g.DrawRectangle(pen, rc);
            }
            if (_movePending)
            {
                using var pen = new Pen(Color.Cyan, 2);
                g.DrawLine(pen, _moveStart, _currentMouse);
                g.FillEllipse(Brushes.Cyan, _moveStart.X - 4, _moveStart.Y - 4, 8, 8);
            }
        }

        private Point _dragEnd;
        private Point _currentMouse;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _currentMouse = e.Location;

            if (_dragging)
            {
                _dragEnd = e.Location;
                Invalidate();
                return;
            }

            if (_dragNpc != null && e.Button == MouseButtons.Left)
            {
                var mp = ClientToMap(e.Location);
                _dragNpc.Location = new Point(mp.X - _dragOffset.X, mp.Y - _dragOffset.Y);
                Invalidate();
                return;
            }

            object hover = null;
            if (Mode == CanvasMode.Select || Mode == CanvasMode.Npc)
            {
                hover = (object)NpcAt(e.Location) ?? RespawnAt(e.Location);
                if (hover == null) hover = SafeZoneAt(e.Location);
                if (hover == null) hover = MovementAt(e.Location);
            }
            if (!ReferenceEquals(hover, _hover)) { _hover = hover; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_map == null) return;

            if (e.Button == MouseButtons.Left)
            {
                switch (Mode)
                {
                    case CanvasMode.Npc:
                        NewNpcRequested?.Invoke(ClientToMap(e.Location));
                        break;
                    case CanvasMode.Respawn:
                    case CanvasMode.SafeZone:
                        _dragging = true;
                        _dragStart = e.Location;
                        _dragEnd = e.Location;
                        Capture = true;
                        break;
                    case CanvasMode.Movement:
                        if (!_movePending)
                        {
                            _movePending = true;
                            _moveStart = ClientToMap(e.Location);
                        }
                        else
                        {
                            var m = new MovementInfo
                            {
                                MapIndex = 0, // 由属性窗选择目标地图
                                Source = _moveStart,
                                Destination = ClientToMap(e.Location)
                            };
                            _movePending = false;
                            MovementCreated?.Invoke(m);
                        }
                        Invalidate();
                        break;
                    default:
                        var npc = NpcAt(e.Location);
                        if (npc != null)
                        {
                            _dragNpc = npc;
                            var mp = ClientToMap(e.Location);
                            _dragOffset = new Point(mp.X - npc.Location.X, mp.Y - npc.Location.Y);
                        }
                        break;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
                var a = ClientToMap(_dragStart);
                var b = ClientToMap(_dragEnd);
                var rect = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                var center = new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);

                if (rect.Width < 5 && rect.Height < 5)
                {
                    center = ClientToMap(e.Location);
                    rect = new Rectangle(center.X, center.Y, 0, 0);
                }

                if (Mode == CanvasMode.Respawn)
                {
                    var rp = new RespawnInfo
                    {
                        MonsterIndex = 1,
                        Location = center,
                        Count = 1,
                        Spread = (ushort)Math.Min(ushort.MaxValue, Math.Max(rect.Width, rect.Height) / 2),
                        Delay = 5,
                    };
                    RespawnCreated?.Invoke(rp);
                }
                else if (Mode == CanvasMode.SafeZone)
                {
                    var sz = new SafeZoneInfo
                    {
                        Location = center,
                        Size = (ushort)Math.Min(ushort.MaxValue, Math.Max(8, Math.Max(rect.Width, rect.Height) / 2)),
                    };
                    SafeZoneCreated?.Invoke(sz);
                }
                Invalidate();
            }

            if (_dragNpc != null && e.Button == MouseButtons.Left)
            {
                var moved = _dragNpc;
                _dragNpc = null;
                NpcMoved?.Invoke(moved);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Mode != CanvasMode.Select) return;
            var npc = NpcAt(e.Location);
            if (npc != null) { NpcActivated?.Invoke(npc); return; }
            var rp = RespawnAt(e.Location);
            if (rp != null) { RespawnActivated?.Invoke(rp); return; }
            var sz = SafeZoneAt(e.Location);
            if (sz != null) { SafeZoneActivated?.Invoke(sz); return; }
            var m = MovementAt(e.Location);
            if (m != null) { MovementActivated?.Invoke(m); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Right || _map == null) return;
            var menu = new ContextMenuStrip();
            var pt = e.Location;

            var npc = NpcAt(pt);
            if (npc != null)
            {
                menu.Items.Add("编辑NPC", null, (s, a) => NpcActivated?.Invoke(npc));
                menu.Items.Add("删除NPC", null, (s, a) => NpcDeleted?.Invoke(npc));
            }
            var rp = RespawnAt(pt);
            if (rp != null)
            {
                menu.Items.Add("编辑刷怪点", null, (s, a) => RespawnActivated?.Invoke(rp));
                menu.Items.Add("删除刷怪点", null, (s, a) => RespawnDeleted?.Invoke(rp));
            }
            var sz = SafeZoneAt(pt);
            if (sz != null)
            {
                menu.Items.Add("编辑安全区", null, (s, a) => SafeZoneActivated?.Invoke(sz));
                menu.Items.Add("删除安全区", null, (s, a) => SafeZoneDeleted?.Invoke(sz));
            }
            var mv = MovementAt(pt);
            if (mv != null)
            {
                menu.Items.Add("编辑传送门", null, (s, a) => MovementActivated?.Invoke(mv));
                menu.Items.Add("删除传送门", null, (s, a) => MovementDeleted?.Invoke(mv));
            }
            if (menu.Items.Count == 0)
                menu.Items.Add("在此新建NPC", null, (s, a) => NewNpcRequested?.Invoke(ClientToMap(pt)));
            menu.Show(this, pt);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _minimap?.Dispose(); _scaled?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
