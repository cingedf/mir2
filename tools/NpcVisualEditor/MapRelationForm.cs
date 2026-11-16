using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 地图关系图：以 MapInfoList 的 Movements（传送门）为边，地图为节点，
    /// 绘制连通关系网。GDI+ 绘制，支持滚轮缩放/中键平移/点击定位节点。
    /// 对标银河脚本插件的“地图关系图”（交互式节点 + 双向箭头）。
    /// </summary>
    public class MapRelationForm : Form
    {
        private readonly Envir _envir;
        private readonly List<MapNode> _nodes = new();
        private readonly List<RelEdge> _edges = new();

        private float _zoom = 1f;
        private Point _pan;
        private bool _panning;
        private Point _panStart;
        private MapNode _selected;
        private MapNode _hover;

        private class MapNode
        {
            public MapInfo Info;
            public PointF Pos;
            public int OutDegree, InDegree;
            public RectangleF Rect => new(Pos.X - 46, Pos.Y - 18, 92, 36);
        }

        private class RelEdge
        {
            public MapNode From, To;
        }

        public MapRelationForm(Envir envir)
        {
            _envir = envir;
            Text = "地图关系图 — 传送门连通性";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 760);

            var top = new Panel { Dock = DockStyle.Top, Height = 36 };
            top.Controls.Add(new Label
            {
                Text = "  滚轮缩放 · 中键拖动平移 · 单击节点定位 · 双击打开地图详情",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            });

            // 布局：用简单圆周 + 力导向微调，避免重叠
            BuildGraph();
            LayoutGraph();

            Controls.Add(new RelationCanvas(this) { Dock = DockStyle.Fill });
            Controls.Add(top);
        }

        private void BuildGraph()
        {
            var byIndex = new Dictionary<int, MapNode>();
            foreach (var m in _envir.MapInfoList)
            {
                var node = new MapNode { Info = m };
                _nodes.Add(node);
                byIndex[m.Index] = node;
            }
            // 只连接有传送门目标的地图，避免 800+ 孤立节点全部绘制
            var connected = new HashSet<MapNode>();
            foreach (var m in _envir.MapInfoList)
            {
                if (m.Movements == null || m.Movements.Count == 0) continue;
                if (!byIndex.TryGetValue(m.Index, out var from)) continue;
                foreach (var mv in m.Movements)
                {
                    if (!byIndex.TryGetValue(mv.MapIndex, out var to)) continue;
                    if (ReferenceEquals(from, to)) continue;
                    _edges.Add(new RelEdge { From = from, To = to });
                    from.OutDegree++; to.InDegree++;
                    connected.Add(from); connected.Add(to);
                }
            }
            // 裁剪到连通子图（含孤立但无传送的地图不画，性能与可读性）
            if (connected.Count > 0)
                _nodes.RemoveAll(n => !connected.Contains(n));
        }

        private void LayoutGraph()
        {
            // 圆周布局 + 分层（按出度排序），足够可视化连通关系
            int n = _nodes.Count;
            if (n == 0) return;
            var ordered = _nodes.OrderByDescending(x => x.OutDegree + x.InDegree).ToList();
            // 圆周周长需容纳所有节点（节点宽92px + 最小间隔18px），r ≈ n*(92+18)/(2π) ≈ n*17.5。
            // 原 n*6 半径过小，节点大面积重叠；留余量取 n*18。
            double radius = Math.Max(220, n * 18.0);
            for (int i = 0; i < n; i++)
            {
                double ang = 2 * Math.PI * i / n;
                ordered[i].Pos = new PointF(
                    (float)(Math.Cos(ang) * radius),
                    (float)(Math.Sin(ang) * radius));
            }
        }

        private class RelationCanvas : Control
        {
            private readonly MapRelationForm _f;
            public RelationCanvas(MapRelationForm f)
            {
                _f = f;
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
            }

            private PointF WorldToScreen(PointF p)
            {
                var pan = _f._pan;   // 复制到局部，避免 CS1690（封送类值类型字段成员访问）
                float zoom = _f._zoom;
                return new PointF(
                    Width / 2f + (p.X + pan.X) * zoom,
                    Height / 2f + (p.Y + pan.Y) * zoom);
            }

            /// <summary>计算边的起止屏幕点：裁到节点矩形边缘，留 3px 让箭头落在节点外而非被节点填充覆盖。</summary>
            private static (PointF from, PointF to) EdgeEnds(PointF a, PointF b, float hw, float hh)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 1f) return (a, b);
                float ux = dx / len, uy = dy / len;
                // 矩形边界交点参数 t：min(hw/|ux|, hh/|uy|)
                float tx = Math.Abs(ux) > 1e-4f ? hw / Math.Abs(ux) : float.MaxValue;
                float ty = Math.Abs(uy) > 1e-4f ? hh / Math.Abs(uy) : float.MaxValue;
                float t = Math.Min(tx, ty) + 3f;
                return (
                    new PointF(a.X + ux * t, a.Y + uy * t),
                    new PointF(b.X - ux * t, b.Y - uy * t));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(18, 20, 26));

                // 边（箭头）——端点裁到节点边缘，箭头落在目标节点外；双向边垂直错开避免重合
                using var pen = new Pen(Color.FromArgb(120, 140, 170), 1.5f);
                var edgeSet = new HashSet<(MapNode from, MapNode to)>(_f._edges.Select(x => (x.From, x.To)));
                float hw = 46f * _f._zoom, hh = 18f * _f._zoom;
                foreach (var edge in _f._edges)
                {
                    var a = WorldToScreen(edge.From.Pos);
                    var b = WorldToScreen(edge.To.Pos);
                    var (from, to) = EdgeEnds(a, b, hw, hh);
                    bool reverse = edgeSet.Contains((edge.To, edge.From));
                    // 双向边：两条线向相反法向偏移，避免完全重合看不出方向
                    float ox = 0f, oy = 0f;
                    if (reverse)
                    {
                        float dx = to.X - from.X, dy = to.Y - from.Y;
                        float len = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (len > 1f)
                        {
                            float sign = edge.From.Info.Index < edge.To.Info.Index ? 1f : -1f;
                            const float gap = 7f;
                            ox = -dy / len * gap * sign;
                            oy = dx / len * gap * sign;
                        }
                    }
                    from = new PointF(from.X + ox, from.Y + oy);
                    to = new PointF(to.X + ox, to.Y + oy);
                    if (reverse) using (var bp = new Pen(Color.FromArgb(150, 170, 200), 1.2f)) g.DrawLine(bp, from, to);
                    else g.DrawLine(pen, from, to);
                    // 箭头三角：顶点在 to，沿 from→to 方向
                    float hx = to.X - from.X, hy = to.Y - from.Y;
                    float hlen = (float)Math.Sqrt(hx * hx + hy * hy);
                    if (hlen > 1f)
                    {
                        float ux = hx / hlen, uy = hy / hlen;
                        const float alen = 9f, awid = 5f;
                        var p1 = new PointF(to.X - ux * alen + (-uy) * awid, to.Y - uy * alen + ux * awid);
                        var p2 = new PointF(to.X - ux * alen - (-uy) * awid, to.Y - uy * alen - ux * awid);
                        using var arrow = new SolidBrush(reverse ? Color.FromArgb(150, 170, 200) : Color.FromArgb(120, 140, 170));
                        g.FillPolygon(arrow, new[] { to, p1, p2 });
                    }
                }

                // 节点
                foreach (var node in _f._nodes)
                {
                    var c = WorldToScreen(node.Pos);
                    float w = 92 * _f._zoom, h = 36 * _f._zoom;
                    var r = new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
                    bool sel = ReferenceEquals(node, _f._selected);
                    bool hov = ReferenceEquals(node, _f._hover);
                    using var brush = new SolidBrush(sel ? Color.Goldenrod : (hov ? Color.SteelBlue : Color.DimGray));
                    g.FillRectangle(brush, r);
                    using var border = new Pen(sel ? Color.Yellow : Color.Silver);
                    g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
                    if (_f._zoom >= 0.5f)
                    {
                        string title = string.IsNullOrEmpty(node.Info.Title)
                            ? node.Info.FileName : node.Info.Title;
                        using var f = new Font("Microsoft YaHei UI", Math.Max(7f, 8f * _f._zoom));
                        // 按节点框宽度裁剪长名，居中绘制，避免溢出遮挡相邻节点
                        TextRenderer.DrawText(g, title, f, Rectangle.Round(r), Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    }
                }

                // 图例
                string legend = $"地图节点 {_f._nodes.Count} · 传送门边 {_f._edges.Count} · 缩放 {_f._zoom:P0}";
                TextRenderer.DrawText(g, legend, Font, new Point(10, Height - 24), Color.LightGray);
            }

            private MapNode HitTest(Point client)
            {
                foreach (var node in _f._nodes)
                {
                    var c = WorldToScreen(node.Pos);
                    float w = 92 * _f._zoom, h = 36 * _f._zoom;
                    if (Math.Abs(c.X - client.X) <= w / 2 && Math.Abs(c.Y - client.Y) <= h / 2)
                        return node;
                }
                return null;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Middle)
                {
                    _f._panning = true; _f._panStart = e.Location; Capture = true;
                }
                else if (e.Button == MouseButtons.Left)
                {
                    _f._selected = HitTest(e.Location);
                    Invalidate();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_f._panning && e.Button == MouseButtons.Middle)
                {
                    var pan = _f._pan;
                    var ps = _f._panStart;
                    float zoom = _f._zoom;
                    _f._pan = new Point(
                        pan.X + (int)((e.X - ps.X) / zoom),
                        pan.Y + (int)((e.Y - ps.Y) / zoom));
                    _f._panStart = e.Location;
                    Invalidate();
                }
                else
                {
                    var hov = HitTest(e.Location);
                    if (!ReferenceEquals(hov, _f._hover)) { _f._hover = hov; Invalidate(); }
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Middle) { _f._panning = false; Capture = false; }
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                var node = HitTest(e.Location);
                if (node != null)
                    MessageBox.Show(_f, $"地图 [{node.Info.Index}] {node.Info.FileName}\r\n" +
                        $"名称: {node.Info.Title}\r\n出度(传出传送门): {node.OutDegree}\r\n入度(传入传送门): {node.InDegree}",
                        "地图详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _f._zoom = Math.Max(0.2f, Math.Min(4f, _f._zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f)));
                Invalidate();
            }
        }
    }
}
