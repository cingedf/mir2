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
            double radius = Math.Max(180, n * 6.0);
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

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(18, 20, 26));

                // 边（箭头）
                using var pen = new Pen(Color.FromArgb(120, 140, 170), 1.5f);
                pen.EndCap = LineCap.ArrowAnchor;
                foreach (var edge in _f._edges)
                {
                    var a = WorldToScreen(edge.From.Pos);
                    var b = WorldToScreen(edge.To.Pos);
                    g.DrawLine(pen, a, b);
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
                        using var f = new Font("Microsoft YaHei UI", 8f * _f._zoom);
                        TextRenderer.DrawText(g, title, f, Point.Round(new PointF(r.X + 3, r.Y + 3)),
                            Color.White);
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
