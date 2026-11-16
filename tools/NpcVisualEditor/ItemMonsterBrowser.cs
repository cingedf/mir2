using System;
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
            _txtSearch = new TextBox { Location = new Point(104, 5), Width = 220, PlaceholderText = "搜索名称/ID..." };
            _txtSearch.TextChanged += (s, e) => FillList(_txtSearch.Text);
            top.Controls.AddRange(new Control[] { _cmbKind, _txtSearch });

            // 左：列表
            _list = new ListBox { Dock = DockStyle.Left, Width = 260 };
            _list.SelectedIndexChanged += (s, e) => ShowSelected();

            // 右：外观 + 属性
            var right = new Panel { Dock = DockStyle.Fill };
            var picPanel = new Panel { Dock = DockStyle.Left, Width = 140, Padding = new Padding(8) };
            _pic = new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(30, 30, 30)
            };
            _lblPreview = new Label
            {
                Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter,
                Text = "", ForeColor = Color.Gray
            };
            picPanel.Controls.Add(_pic);
            picPanel.Controls.Add(_lblPreview);

            _props = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false };
            right.Controls.Add(_props);
            right.Controls.Add(picPanel);

            var splitter = new Splitter { Dock = DockStyle.Left, Width = 3 };
            Controls.Add(_list);
            Controls.Add(splitter);
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
            else { _props.SelectedObject = null; _pic.Image = null; _lblPreview.Text = ""; }
        }

        private void ShowSelected()
        {
            var sel = _list.SelectedItem;
            if (sel == null) return;
            _props.SelectedObject = sel;

            int imageId = sel switch
            {
                ItemInfo it => it.Image,
                MonsterInfo m => (int)m.Image,
                _ => -1
            };
            string dirName = sel is ItemInfo ? "Items" : "Monsters";
            string dir = Path.Combine(_serverDir, "Envir", "Previews", dirName);
            _pic.Image = null;
            string bmp = Path.Combine(dir, imageId + ".bmp");
            if (File.Exists(bmp))
            {
                try
                {
                    using var fs = new FileStream(bmp, FileMode.Open, FileAccess.Read);
                    _pic.Image = Image.FromStream(fs);
                }
                catch { }
            }
            _lblPreview.Text = $"{dirName} 外观 ID={imageId}" +
                (File.Exists(bmp) ? "" : "（无预览图）");
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
