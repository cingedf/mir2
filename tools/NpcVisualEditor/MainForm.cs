using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 主编辑器。复刻原版 FormMapGod 三个页签：世界 / 生态环境 / 关于。
    /// 世界页：地图列表 + 画布（NPC 拖放）+ 商人/固定NPC/安全区列表。
    /// </summary>
    public class MainForm : Form
    {
        private readonly Envir _envir;
        private readonly string _serverDir;
        private readonly string _clientDir;

        private TabControl _tabs;
        // 世界页
        private ListBox _lstMaps;
        private TextBox _txtMapSearch;
        private MapCanvas _canvas;
        private ListBox _lstMerchant, _lstFixed, _lstSafeZone;
        private Button _btnGo;
        // 生态页
        private ListBox _lstItems, _lstMonsters;
        private TextBox _txtItemSearch, _txtMonsterSearch;
        // 状态
        private MapInfo _currentMap;
        private bool _dirty;

        private readonly Dictionary<string, bool> _merchantCache = new(StringComparer.OrdinalIgnoreCase);

        public MainForm(Envir envir, string serverDir, string clientDir)
        {
            _envir = envir;
            _serverDir = serverDir;
            _clientDir = clientDir;

            Text = $"NPC 可视化编辑器 — {serverDir}";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(1000, 640);

            BuildTabs();
            BuildMenu();

            FormClosing += (s, e) =>
            {
                if (_dirty && MessageBox.Show(this, "有未保存的修改，确定退出？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    e.Cancel = true;
            };

            Load += (s, e) => LoadAllLists();
        }

        private void BuildMenu()
        {
            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add(new ToolStripMenuItem("保存数据库(&S)", null,
                (s, e) => SaveDatabase()) { ShortcutKeyDisplayString = "Ctrl+S" });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("退出(&X)", null, (s, e) => Close()));
            var mNpc = new ToolStripMenuItem("NPC(&N)");
            mNpc.DropDownItems.Add(new ToolStripMenuItem("新建NPC(&N)", null, (s, e) => NewNpc()));
            mNpc.DropDownItems.Add(new ToolStripMenuItem("编辑NPC(&E)", null, (s, e) => EditSelectedNpc()));
            mNpc.DropDownItems.Add(new ToolStripMenuItem("删除NPC(&D)", null, (s, e) => DeleteSelectedNpc()));
            var mHelp = new ToolStripMenuItem("帮助(&H)");
            mHelp.DropDownItems.Add(new ToolStripMenuItem("NPC命令说明(&C)", null, (s, e) => ShowNpcCommandHelp()));
            menu.Items.AddRange(new ToolStripItem[] { mFile, mNpc, mHelp });
            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildWorldTab());
            _tabs.TabPages.Add(BuildEcologyTab());
            _tabs.TabPages.Add(BuildAboutTab());
            Controls.Add(_tabs);
        }

        // ---------- 世界页 ----------
        private TabPage BuildWorldTab()
        {
            var page = new TabPage("世界");

            // 左：地图列表
            var left = new Panel { Dock = DockStyle.Left, Width = 230 };
            _txtMapSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索地图..." };
            _txtMapSearch.TextChanged += (s, e) => FillMapList(_txtMapSearch.Text);
            _btnGo = new Button { Text = "GO", Dock = DockStyle.Top, Height = 26 };
            _btnGo.Click += (s, e) => SelectMapByIndexText();
            _lstMaps = new ListBox { Dock = DockStyle.Fill };
            _lstMaps.SelectedIndexChanged += (s, e) => OnMapSelected();
            _lstMaps.DoubleClick += (s, e) => { if (_lstMaps.SelectedItem is MapInfo m) ZoomToMap(m); };
            left.Controls.Add(_lstMaps);
            left.Controls.Add(_btnGo);
            left.Controls.Add(_txtMapSearch);

            // 中：画布
            _canvas = new MapCanvas(_clientDir, this)
            {
                Dock = DockStyle.Fill,
                NpcActivated = npc => EditNpc(npc),
                NpcMoved = npc => { _dirty = true; RefreshNpcLists(); },
            };
            _canvas.NpcDeleted = npc =>
            {
                if (MessageBox.Show(this, $"确定删除 NPC「{npc.GameName}」？", "确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _envir.NPCInfoList.Remove(npc);
                    _dirty = true;
                    OnMapSelected();
                }
            };
            _canvas.NewNpcRequested = pt => NewNpcAt(pt);

            // 右：NPC / 安全区列表
            var right = new Panel { Dock = DockStyle.Right, Width = 330 };
            var rightTabs = new TabControl { Dock = DockStyle.Fill };

            var tpMerchant = new TabPage("商人NPC");
            _lstMerchant = new ListBox { Dock = DockStyle.Fill };
            _lstMerchant.DoubleClick += (s, e) => EditSelectedNpc();
            tpMerchant.Controls.Add(_lstMerchant);

            var tpFixed = new TabPage("固定NPC");
            _lstFixed = new ListBox { Dock = DockStyle.Fill };
            _lstFixed.DoubleClick += (s, e) => EditSelectedNpc();
            tpFixed.Controls.Add(_lstFixed);

            var tpSafe = new TabPage("安全区");
            _lstSafeZone = new ListBox { Dock = DockStyle.Fill };
            tpSafe.Controls.Add(_lstSafeZone);

            rightTabs.TabPages.AddRange(new[] { tpMerchant, tpFixed, tpSafe });
            right.Controls.Add(rightTabs);

            page.Controls.Add(_canvas);
            page.Controls.Add(right);
            page.Controls.Add(left);
            return page;
        }

        private TabPage BuildEcologyTab()
        {
            var page = new TabPage("生态环境");

            var left = new Panel { Dock = DockStyle.Fill };
            var right = new Panel { Dock = DockStyle.Fill };

            var splitter = new Splitter { Dock = DockStyle.Right };

            // 物品
            _txtItemSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索物品..." };
            _txtItemSearch.TextChanged += (s, e) => FillItems(_txtItemSearch.Text);
            _lstItems = new ListBox { Dock = DockStyle.Fill };
            left.Controls.Add(_lstItems);
            left.Controls.Add(_txtItemSearch);

            // 怪物
            _txtMonsterSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索怪物..." };
            _txtMonsterSearch.TextChanged += (s, e) => FillMonsters(_txtMonsterSearch.Text);
            _lstMonsters = new ListBox { Dock = DockStyle.Fill };
            right.Controls.Add(_lstMonsters);
            right.Controls.Add(_txtMonsterSearch);

            right.Dock = DockStyle.Right;
            right.Width = 420;
            left.Dock = DockStyle.Fill;

            page.Controls.Add(left);
            page.Controls.Add(splitter);
            page.Controls.Add(right);
            return page;
        }

        private TabPage BuildAboutTab()
        {
            var page = new TabPage("关于");
            var lbl = new Label
            {
                Text = "NPC 可视化编辑器 — Crystal 版 V1.0.0\r\n\r\n" +
                       "面向 Crystal（C#）引擎 MirDB 的 NPC 可视化编辑工具。\r\n" +
                       "数据经引擎原生序列化读写，保证字节级兼容。\r\n\r\n" +
                       $"服务端目录: {_serverDir}\r\n客户端目录: {_clientDir}",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter
            };
            page.Controls.Add(lbl);
            return page;
        }

        // ---------- 数据填充 ----------
        public void LoadAllLists()
        {
            FillMapList(null);
            FillItems(null);
            FillMonsters(null);
        }

        private void FillMapList(string filter)
        {
            _lstMaps.BeginUpdate();
            _lstMaps.Items.Clear();
            foreach (var m in _envir.MapInfoList.OrderBy(d => d.Index))
            {
                if (!string.IsNullOrEmpty(filter) &&
                    m.FileName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    m.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _lstMaps.Items.Add(m);
            }
            _lstMaps.EndUpdate();
        }

        private void FillItems(string filter)
        {
            _lstItems.BeginUpdate();
            _lstItems.Items.Clear();
            foreach (var it in _envir.ItemInfoList)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    it.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _lstItems.Items.Add(it);
            }
            _lstItems.EndUpdate();
        }

        private void FillMonsters(string filter)
        {
            _lstMonsters.BeginUpdate();
            _lstMonsters.Items.Clear();
            foreach (var m in _envir.MonsterInfoList)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _lstMonsters.Items.Add(m);
            }
            _lstMonsters.EndUpdate();
        }

        private void SelectMapByIndexText()
        {
            if (int.TryParse(_txtMapSearch.Text.Trim(), out int idx))
            {
                var m = _envir.MapInfoList.FirstOrDefault(d => d.Index == idx);
                if (m != null) { ZoomToMap(m); return; }
            }
            if (_lstMaps.Items.Count > 0) _lstMaps.SelectedIndex = 0;
        }

        private void ZoomToMap(MapInfo m)
        {
            _currentMap = m;
            _canvas.LoadMap(m, GetMapNpcs(m));
            RefreshNpcLists();
        }

        private void OnMapSelected()
        {
            if (_lstMaps.SelectedItem is MapInfo m) ZoomToMap(m);
        }

        private List<NPCInfo> GetMapNpcs(MapInfo m)
        {
            return _envir.NPCInfoList.Where(d => d.MapIndex == m.Index).ToList();
        }

        /// <summary>商人判定：脚本含 [TRADE] 段（结果缓存）。</summary>
        public bool IsMerchant(NPCInfo npc)
        {
            string key = npc.FileName;
            if (_merchantCache.TryGetValue(key, out bool v)) return v;
            bool merchant = false;
            try
            {
                string path = NpcScriptPath(npc);
                if (File.Exists(path))
                    merchant = File.ReadAllText(path).IndexOf("[TRADE]", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { /* 读取失败按非商人 */ }
            _merchantCache[key] = merchant;
            return merchant;
        }

        public string NpcScriptPath(NPCInfo npc)
        {
            // 引擎约定：NPCScript.LoadInfo = Path.Combine(Settings.NPCPath, FileName + ".txt")
            return Path.Combine(_serverDir, "Envir", "NPCs", npc.FileName + ".txt");
        }

        public void RefreshNpcLists()
        {
            var npcs = _currentMap == null ? new List<NPCInfo>() : GetMapNpcs(_currentMap);

            _lstMerchant.BeginUpdate();
            _lstMerchant.Items.Clear();
            _lstFixed.BeginUpdate();
            _lstFixed.Items.Clear();
            foreach (var npc in npcs.OrderBy(d => d.Name))
            {
                if (IsMerchant(npc)) _lstMerchant.Items.Add(npc);
                else _lstFixed.Items.Add(npc);
            }
            _lstMerchant.EndUpdate();
            _lstFixed.EndUpdate();

            _lstSafeZone.BeginUpdate();
            _lstSafeZone.Items.Clear();
            if (_currentMap != null)
                foreach (var sz in _currentMap.SafeZones)
                    _lstSafeZone.Items.Add(sz);
            _lstSafeZone.EndUpdate();
        }

        public void MarkDirty() => _dirty = true;

        // ---------- NPC 操作 ----------
        private void NewNpc() => NewNpcAt(new Point(100, 100));

        private void NewNpcAt(Point location)
        {
            if (_currentMap == null) return;
            var npc = new NPCInfo
            {
                Index = -1, // 由 NpcEditForm 确认时分配
                MapIndex = _currentMap.Index,
                Location = location,
                Name = "新NPC",
            };
            using var dlg = new NpcEditForm(npc, _envir, _serverDir, isNew: true);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                npc.Index = ++_envir.NPCIndex;
                _envir.NPCInfoList.Add(npc);
                _dirty = true;
                _canvas.SetNpcs(GetMapNpcs(_currentMap));
                RefreshNpcLists();
            }
        }

        private NPCInfo SelectedNpc => _lstMerchant.SelectedItem as NPCInfo ?? _lstFixed.SelectedItem as NPCInfo;

        private void EditSelectedNpc()
        {
            var npc = SelectedNpc;
            if (npc != null) EditNpc(npc);
        }

        private void EditNpc(NPCInfo npc)
        {
            using var dlg = new NpcEditForm(npc, _envir, _serverDir, isNew: false);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _dirty = true;
                if (_currentMap != null && npc.MapIndex != _currentMap.Index)
                    ZoomToMap(_currentMap);
                _canvas.SetNpcs(GetMapNpcs(_currentMap));
                RefreshNpcLists();
            }
        }

        private void DeleteSelectedNpc()
        {
            var npc = SelectedNpc;
            if (npc == null) return;
            if (MessageBox.Show(this, $"确定删除 NPC「{npc.GameName}」？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            _envir.NPCInfoList.Remove(npc);
            _dirty = true;
            _canvas.SetNpcs(GetMapNpcs(_currentMap));
            RefreshNpcLists();
        }

        private void ShowNpcCommandHelp()
        {
            string helpPath = Path.Combine(_serverDir, "Envir", "NPCs", "NPC命令说明.txt");
            if (!File.Exists(helpPath))
            {
                MessageBox.Show(this, "未找到 NPC命令说明.txt。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var form = new Form
            {
                Text = "NPC 命令说明",
                Size = new Size(800, 600),
                StartPosition = FormStartPosition.CenterParent
            };
            var txt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Text = File.ReadAllText(helpPath),
                Font = new Font("Consolas", 10F)
            };
            form.Controls.Add(txt);
            form.Show(this);
        }

        // ---------- 保存 ----------
        private void SaveDatabase()
        {
            using var dlg = new SaveForm(_serverDir);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                if (dlg.Backup)
                {
                    string bak = Path.Combine(_serverDir,
                        $"Server.MirDB.bak-{DateTime.Now:yyyyMMdd-HHmmss}");
                    string dbPath = Path.Combine(_serverDir, "Server.MirDB");
                    if (File.Exists(dbPath)) File.Copy(dbPath, bak, true);
                }
                _envir.SaveDB();
                _dirty = false;
                MessageBox.Show(this, "数据库保存成功。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { SaveDatabase(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
