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
            var mCreate = new ToolStripMenuItem("创作(&C)");
            mCreate.DropDownItems.Add(new ToolStripMenuItem("玩法模板生成器(&T)", null,
                (s, e) => new TemplateForm(_serverDir, _envir.MapInfoList).ShowDialog(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("任务链流程图(&Q)", null,
                (s, e) => new QuestFlowForm(_envir.QuestInfoList).ShowDialog(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("特效/BUFF模板(&B)", null,
                (s, e) => new EffectBuffForm(_serverDir).ShowDialog(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("脚本命令向导(&W)", null,
                (s, e) => new CommandWizardForm(_serverDir).ShowDialog(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("物品/怪物可视化图鉴(&I)", null,
                (s, e) => new BrowserHostForm(_envir, _serverDir).Show(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("客户端配置编辑器(&G)", null,
                (s, e) => OpenClientConfig()));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("工作版本管理(&V)", null,
                (s, e) => new WorkspaceManagerForm(_serverDir).ShowDialog(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("掉落表分析器(&D)", null,
                (s, e) => new DropAnalysisForm(_serverDir, _envir).Show(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("脚本检查中心(&K)", null,
                (s, e) => new ScriptCheckerForm(_serverDir).Show(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("变量管理面板(&U)", null,
                (s, e) => new VariableManagerForm(_serverDir).Show(this)));
            mCreate.DropDownItems.Add(new ToolStripMenuItem("NPC命令说明(&H)", null, (s, e) => ShowNpcCommandHelp()));
            var mHelp = new ToolStripMenuItem("帮助(&H)");
            mHelp.DropDownItems.Add(new ToolStripMenuItem("关于(&A)", null,
                (s, e) => { _tabs.SelectedIndex = _tabs.TabPages.Count - 1; }));
            menu.Items.AddRange(new ToolStripItem[] { mFile, mNpc, mCreate, mHelp });
            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildWorldTab());
            _tabs.TabPages.Add(BuildEcologyTab());

            // 工具中心：复用现成工具（地图/图库/爆率/引擎内置编辑器）
            var tpTools = new TabPage("工具中心");
            tpTools.Controls.Add(new ToolHubForm(_serverDir, _clientDir) { Dock = DockStyle.Fill });
            _tabs.TabPages.Add(tpTools);

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

            // 中：画布 + 工具模式栏
            _canvas = new MapCanvas(_clientDir, this)
            {
                Dock = DockStyle.Fill,
                NpcActivated = npc => EditNpc(npc),
                NpcMoved = npc => { _dirty = true; RefreshNpcLists(); },
                MonsterName = idx => _envir.MonsterInfoList.FirstOrDefault(d => d.Index == idx)?.Name ?? idx.ToString(),
                MapName = idx => _envir.MapInfoList.FirstOrDefault(d => d.Index == idx)?.FileName ?? idx.ToString(),
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
            _canvas.RespawnCreated = rp =>
            {
                if (_currentMap == null) return;
                using var dlg = new RespawnEditForm(rp, _envir);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _currentMap.Respawns.Add(rp);
                    _dirty = true;
                    _canvas.Refresh();
                }
            };
            _canvas.RespawnActivated = rp =>
            {
                using var dlg = new RespawnEditForm(rp, _envir);
                if (dlg.ShowDialog(this) == DialogResult.OK) { _dirty = true; _canvas.Refresh(); }
            };
            _canvas.RespawnDeleted = rp =>
            {
                if (MessageBox.Show(this, "确定删除该刷怪点？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _currentMap?.Respawns.Remove(rp);
                    _dirty = true;
                    _canvas.Refresh();
                }
            };
            _canvas.SafeZoneCreated = sz =>
            {
                if (_currentMap == null) return;
                sz.Info = _currentMap;
                using var dlg = new SafeZoneEditForm(sz);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _currentMap.SafeZones.Add(sz);
                    _dirty = true;
                    RefreshNpcLists();
                    _canvas.Refresh();
                }
            };
            _canvas.SafeZoneActivated = sz =>
            {
                using var dlg = new SafeZoneEditForm(sz);
                if (dlg.ShowDialog(this) == DialogResult.OK) { _dirty = true; RefreshNpcLists(); _canvas.Refresh(); }
            };
            _canvas.SafeZoneDeleted = sz =>
            {
                if (MessageBox.Show(this, "确定删除该安全区？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _currentMap?.SafeZones.Remove(sz);
                    _dirty = true; RefreshNpcLists(); _canvas.Refresh();
                }
            };
            _canvas.MovementCreated = mv =>
            {
                if (_currentMap == null) return;
                using var dlg = new MovementEditForm(mv, _envir);
                if (dlg.ShowDialog(this) == DialogResult.OK && mv.MapIndex > 0)
                {
                    _currentMap.Movements.Add(mv);
                    _dirty = true;
                    _canvas.Refresh();
                }
            };
            _canvas.MovementActivated = mv =>
            {
                using var dlg = new MovementEditForm(mv, _envir);
                if (dlg.ShowDialog(this) == DialogResult.OK) { _dirty = true; _canvas.Refresh(); }
            };
            _canvas.MovementDeleted = mv =>
            {
                if (MessageBox.Show(this, "确定删除该传送门？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _currentMap?.Movements.Remove(mv);
                    _dirty = true; _canvas.Refresh();
                }
            };

            var toolBar = new Panel { Dock = DockStyle.Top, Height = 34 };
            string[] modes = { "选择/拖动", "新建NPC", "刷怪点(框选)", "安全区(画矩形)", "传送门(点两点)" };
            for (int i = 0; i < modes.Length; i++)
            {
                int mode = i;
                var btn = new RadioButton
                {
                    Text = modes[i],
                    Location = new Point(8 + i * 122, 6),
                    Size = new Size(120, 24),
                    Appearance = Appearance.Button,
                    Checked = i == 0
                };
                btn.CheckedChanged += (s, e) => { if (btn.Checked) _canvas.Mode = (CanvasMode)mode; };
                toolBar.Controls.Add(btn);
            }

            page.Controls.Add(_canvas);
            page.Controls.Add(toolBar);

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

            // 可视化图鉴按钮（属性 + 外观图联动）
            var btnBrowser = new Button { Text = "打开可视化图鉴（物品/怪物属性+外观）", Dock = DockStyle.Top, Height = 30 };
            btnBrowser.Click += (s, e) => new BrowserHostForm(_envir, _serverDir).Show(this);

            page.Controls.Add(left);
            page.Controls.Add(splitter);
            page.Controls.Add(right);
            page.Controls.Add(btnBrowser);
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

        private void OpenClientConfig()
        {
            if (string.IsNullOrEmpty(_clientDir) || !Directory.Exists(_clientDir))
            {
                MessageBox.Show(this, "未设置有效的客户端目录，无法编辑客户端配置。\r\n" +
                    "请在启动器中选择客户端目录后重新进入。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            new ClientConfigForm(_clientDir).Show(this);
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
