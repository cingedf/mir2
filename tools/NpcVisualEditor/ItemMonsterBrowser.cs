using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 物品/怪物可视化浏览器：左侧列表 + 右侧外观图联动 + 属性表（社区同类工具最有价值增量）。
    /// 外观图来自 Server\Envir\Previews\{Items|Monsters}\{ID}.bmp。
    /// 属性表只读——本工具是离线图鉴，无任何持久化通道，避免用户误以为编辑已写回 MirDB。
    /// </summary>
    public class ItemMonsterBrowser : UserControl
    {
        private readonly Envir _envir;
        private readonly string _serverDir;
        private readonly ComboBox _cmbKind;
        private readonly TextBox _txtSearch;
        private readonly ListBox _list;
        private readonly PictureBox _pic;
        private readonly PropertyGrid _props;
        private readonly Label _lblPreview;

        public ItemMonsterBrowser(Envir envir, string serverDir)
        {
            _envir = envir;
            _serverDir = serverDir;

            // 顶部：类型 + 搜索
            var top = new Panel { Dock = DockStyle.Top, Height = 34 };
            _cmbKind = new ComboBox
            {
                Location = new Point(6, 5), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbKind.Items.AddRange(new[] { "物品", "怪物" });
            _cmbKind.SelectedIndex = 0;
            _cmbKind.SelectedIndexChanged += (s, e) => FillList(_txtSearch.Text);
            top.Controls.Add(_cmbKind);
            _txtSearch = new TextBox { Location = new Point(104, 5), Width = 220, PlaceholderText = "搜索名称/ID..." };
            _txtSearch.TextChanged += (s, e) => FillList(_txtSearch.Text);
            top.Controls.Add(_txtSearch);

            // 左：列表（加宽 + 横向滚动条，长中文名可横向滚动查看；Splitter 设最小宽度防误拖隐藏）
            _list = new ListBox { Dock = DockStyle.Left, Width = 280, HorizontalScrollbar = true };
            _list.SelectedIndexChanged += (s, e) => ShowSelected();

            // 右：外观 + 属性
            var right = new Panel { Dock = DockStyle.Fill };
            var picPanel = new Panel { Dock = DockStyle.Left, Width = 150, Padding = new Padding(8) };
            _pic = new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(30, 30, 30)
            };
            _lblPreview = new Label
            {
                Dock = DockStyle.Bottom, Height = 44, TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false, AutoEllipsis = true, Padding = new Padding(4, 0, 4, 0),
                Text = "", ForeColor = Color.Gray
            };
            picPanel.Controls.Add(_pic);
            picPanel.Controls.Add(_lblPreview);

            _props = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false };
            right.Controls.Add(_props);
            right.Controls.Add(picPanel);

            var splitter = new Splitter { Dock = DockStyle.Left, Width = 3, MinSize = 140 };
            // Splitter 必须在目标 _list 之前添加：目标贴左边缘、Splitter 在其内侧，
            // 否则 FindTarget() 返回 null 导致完全拖不动（已用反射实证）
            Controls.Add(splitter);
            Controls.Add(_list);
            Controls.Add(right);
            Controls.Add(top);

            FillList(null);
        }

        private void FillList(string filter)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            bool items = _cmbKind.SelectedIndex == 0;
            if (items)
            {
                foreach (var it in _envir.ItemInfoList)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        it.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        !it.Index.ToString().Contains(filter)) continue;
                    _list.Items.Add(it);
                }
            }
            else
            {
                foreach (var m in _envir.MonsterInfoList)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        !m.Index.ToString().Contains(filter)) continue;
                    _list.Items.Add(m);
                }
            }
            _list.EndUpdate();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else ClearPreview(noMatch: true);
        }

        private void ClearPreview(bool noMatch = false)
        {
            _props.SelectedObject = null;
            var old = _pic.Image;
            _pic.Image = null;
            old?.Dispose();
            _lblPreview.Text = noMatch ? "无匹配项" : "";
        }

        private void ShowSelected()
        {
            var sel = _list.SelectedItem;
            if (sel == null) { ClearPreview(); return; }

            // #8 stdmode 动态字段名：物品按 ItemType 暴露属性子集 + 中文名；
            // 怪物走通用只读包装（本工具无持久化，属性表只读，避免误以为修改已生效）
            _props.SelectedObject = sel switch
            {
                ItemInfo itemSel => (object)new ItemInfoViewWrapper(itemSel),
                MonsterInfo => new ReadOnlyObjectWrapper(sel),
                _ => sel
            };

            int imageId = sel switch
            {
                ItemInfo it => it.Image,
                MonsterInfo m => (int)m.Image,
                _ => -1
            };
            string dirName = sel is ItemInfo ? "Items" : "Monsters";
            string dir = Path.Combine(_serverDir, "Envir", "Previews", dirName);

            // 释放旧图再加载新图，避免反复点选造成 GDI+ 句柄泄漏
            var oldImg = _pic.Image;
            _pic.Image = null;
            oldImg?.Dispose();

            string bmp = Path.Combine(dir, imageId + ".bmp");
            bool hasPreview = false;
            if (File.Exists(bmp))
            {
                try
                {
                    using var fs = new FileStream(bmp, FileMode.Open, FileAccess.Read);
                    using var tmp = Image.FromStream(fs);
                    _pic.Image = new Bitmap(tmp); // 深拷贝，脱离流生命周期
                    hasPreview = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"预览图加载失败 {bmp}: {ex.Message}");
                }
            }
            string kindLabel = dirName == "Items" ? "物品" : "怪物";   // 目录名保留英文，显示层汉化
            _lblPreview.Text = $"{kindLabel} 外观 ID={imageId}" + (hasPreview ? "" : "（无预览图）");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var img = _pic.Image;
                _pic.Image = null;
                img?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>通用只读对象包装：把任意对象的属性描述符全部设为只读，供无持久化通道的浏览工具使用。</summary>
    internal sealed class ReadOnlyObjectWrapper : CustomTypeDescriptor
    {
        private readonly object _obj;
        public ReadOnlyObjectWrapper(object obj) { _obj = obj; }

        public override PropertyDescriptorCollection GetProperties()
        {
            var src = TypeDescriptor.GetProperties(_obj);
            var arr = new PropertyDescriptor[src.Count];
            for (int i = 0; i < src.Count; i++) arr[i] = new ReadOnlyProp(src[i]);
            return new PropertyDescriptorCollection(arr);
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();
        public override object GetPropertyOwner(PropertyDescriptor pd) => _obj;

        private sealed class ReadOnlyProp : PropertyDescriptor
        {
            private readonly PropertyDescriptor _inner;
            public ReadOnlyProp(PropertyDescriptor inner) : base(inner) { _inner = inner; }
            public override bool IsReadOnly => true;
            public override Type ComponentType => _inner.ComponentType;
            public override Type PropertyType => _inner.PropertyType;
            public override bool CanResetValue(object c) => false;
            public override object GetValue(object c) => _inner.GetValue(c);
            public override void ResetValue(object c) { }
            public override void SetValue(object c, object v) { }
            public override bool ShouldSerializeValue(object c) => false;
        }
    }

    /// <summary>浏览器宿主窗口：作为独立工具打开。</summary>
    public class BrowserHostForm : Form
    {
        public BrowserHostForm(Envir envir, string serverDir)
        {
            Text = "物品 / 怪物可视化图鉴";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 640);
            Controls.Add(new ItemMonsterBrowser(envir, serverDir) { Dock = DockStyle.Fill });
        }
    }
}
