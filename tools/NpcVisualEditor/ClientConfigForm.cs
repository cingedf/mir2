using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 轻量 INI 读写：保留原始行/注释/节顺序，仅替换目标键值行。
    /// UTF-8 无 BOM（与客户端引擎约定一致）。
    /// </summary>
    internal sealed class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines = new();
        // (节,键) -> 行索引
        private readonly Dictionary<string, int> _index =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sections = new();

        public string Path => _path;

        public IniFile(string path)
        {
            _path = path;
            if (File.Exists(path))
            {
                _lines.AddRange(File.ReadAllLines(path, new UTF8Encoding(false)));
                Parse();
            }
        }

        private static string Key(string sec, string key) => sec + "\u0001" + key;

        private void Parse()
        {
            string sec = "";
            for (int i = 0; i < _lines.Count; i++)
            {
                string raw = _lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]") && t.Length > 2)
                {
                    sec = t[1..^1].Trim();
                    if (!_sections.Contains(sec, StringComparer.OrdinalIgnoreCase))
                        _sections.Add(sec);
                }
                else if (sec.Length > 0)
                {
                    int eq = t.IndexOf('=');
                    if (eq > 0)
                    {
                        string k = t[..eq].Trim();
                        _index[Key(sec, k)] = i;
                    }
                }
            }
        }

        public IReadOnlyList<string> Sections => _sections;

        public string[] GetKeys(string sec) => _index.Keys
            .Where(k => k.StartsWith(sec + "\u0001", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[(sec.Length + 1)..]).ToArray();

        public string GetValue(string sec, string key)
        {
            if (!_index.TryGetValue(Key(sec, key), out int i)) return "";
            string t = _lines[i].Trim();
            int eq = t.IndexOf('=');
            return eq >= 0 ? t[(eq + 1)..].Trim() : "";
        }

        public void SetValue(string sec, string key, string value)
        {
            if (_index.TryGetValue(Key(sec, key), out int i))
            {
                string raw = _lines[i];
                int eq = raw.IndexOf('=');
                // 保留键左侧空白与行内注释（';' 起始）
                string left = eq >= 0 ? raw[..(eq + 1)] : raw.TrimEnd() + "=";
                string tail = "";
                int ci = raw.IndexOf(';');
                if (ci > eq) tail = " " + raw[ci..].TrimEnd();
                _lines[i] = left + value + tail;
            }
            else
            {
                // 追加到节末尾
                int insert = _lines.Count;
                string secLine = "[" + sec + "]";
                for (int j = 0; j < _lines.Count; j++)
                    if (_lines[j].Trim().Equals(secLine, StringComparison.OrdinalIgnoreCase))
                    {
                        insert = j + 1;
                        while (insert < _lines.Count &&
                               !_lines[insert].Trim().StartsWith("[") &&
                               _lines[insert].Trim().Length > 0) insert++;
                        break;
                    }
                if (insert >= _lines.Count)
                {
                    if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
                    _lines.Add(secLine);
                    insert = _lines.Count;
                }
                _lines.Insert(insert, key + "=" + value);
                _index[Key(sec, key)] = insert;
                if (!_sections.Contains(sec, StringComparer.OrdinalIgnoreCase)) _sections.Add(sec);
                // 插入行后索引偏移修正
                for (var e = _index.ToList(); true; )
                {
                    var bad = e.Where(p => p.Value >= insert && p.Key != Key(sec, key)).ToList();
                    if (bad.Count == 0) break;
                    foreach (var p in bad) _index[p.Key] = p.Value + 1;
                    e = _index.ToList();
                }
            }
        }

        public void RemoveKey(string sec, string key)
        {
            if (!_index.TryGetValue(Key(sec, key), out int i)) return;
            _lines.RemoveAt(i);
            _index.Remove(Key(sec, key));
            foreach (var p in _index.ToList())
                if (p.Value > i) _index[p.Key] = p.Value - 1;
        }

        public void Save()
        {
            File.WriteAllLines(_path, _lines, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// 客户端配置可视化编辑器：Mir2Config.ini（通用键值）、
    /// KeyBinds.ini（键位可视化）、Localization\*.json（文案）。
    /// </summary>
    public class ClientConfigForm : Form
    {
        private readonly string _clientDir;
        private readonly TabControl _tabs = new();
        private readonly string _mir2Cfg, _keyBinds, _locDir;

        // Mir2Config 页
        private IniFile _mir2Ini;
        private DataGridView _gridConfig;
        private ComboBox _cmbConfigSection;
        // KeyBinds 页
        private IniFile _keysIni;
        private DataGridView _gridKeys;
        // Localization 页
        private ComboBox _cmbLang;
        private ComboBox _cmbJsonSection;
        private DataGridView _gridLoc;
        private Dictionary<string, Dictionary<string, string>> _locDoc;
        private string _locPath;
        // 文案未保存保护：编辑 Localization 值列后置 dirty，切换语言/重载/关闭时提示，避免静默丢失。
        private bool _locDirty;
        private int _locLangIndex = -1;      // 最近一次已提交（落盘/载入）的语言索引，用于 Cancel 还原
        private bool _locLangGuard;          // 抑制 Cancel 还原时重入 SelectedIndexChanged

        public ClientConfigForm(string clientDir)
        {
            _clientDir = clientDir;
            _mir2Cfg = Path.Combine(clientDir, "Mir2Config.ini");
            _keyBinds = Path.Combine(clientDir, "KeyBinds.ini");
            _locDir = Path.Combine(clientDir, "Localization");

            Text = "客户端配置编辑器 — 可视化读写 INI / 键位 / 本地化文案";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1060, 700);
            MinimumSize = new Size(860, 520);

            BuildMenu();
            Controls.Add(_tabs);
            _tabs.Dock = DockStyle.Fill;

            LoadMir2Config();
            LoadKeyBinds();
            LoadLocalization();
        }

        private void BuildMenu()
        {
            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add(new ToolStripMenuItem("保存当前页(&S)", null,
                (s, e) => SaveCurrent()) { ShortcutKeyDisplayString = "Ctrl+S" });
            mFile.DropDownItems.Add(new ToolStripMenuItem("重新载入(&R)", null, (s, e) => ReloadCurrent()));
            menu.Items.Add(mFile);
            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        // ---------- Mir2Config ----------
        private void LoadMir2Config()
        {
            var page = new TabPage("Mir2Config.ini");
            if (!File.Exists(_mir2Cfg))
            {
                page.Controls.Add(new Label { Text = "未找到 " + _mir2Cfg, Dock = DockStyle.Fill });
                _tabs.TabPages.Add(page);
                return;
            }
            _mir2Ini = new IniFile(_mir2Cfg);

            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label
            {
                Text = "节：", Location = new Point(10, 12), AutoSize = true
            });
            _cmbConfigSection = new ComboBox
            {
                Location = new Point(50, 8), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var s in _mir2Ini.Sections) _cmbConfigSection.Items.Add(s);
            if (_cmbConfigSection.Items.Count > 0) _cmbConfigSection.SelectedIndex = 0;
            _cmbConfigSection.SelectedIndexChanged += (s, e) => FillConfigGrid();
            top.Controls.Add(_cmbConfigSection);
            top.Controls.Add(new Label
            {
                Text = "可直接编辑「值」列；布尔值建议 True/False。",
                Location = new Point(330, 12), AutoSize = true, ForeColor = Color.Gray
            });

            _gridConfig = MakeGrid();
            _gridConfig.CellEndEdit += (s, e) =>
            {
                if (e.ColumnIndex == 1 && e.RowIndex >= 0)
                {
                    string sec = _cmbConfigSection.SelectedItem as string;
                    string key = _gridConfig.Rows[e.RowIndex].Cells[0].Value as string;
                    if (!string.IsNullOrEmpty(sec) && !string.IsNullOrEmpty(key))
                        _mir2Ini.SetValue(sec, key, _gridConfig.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "");
                }
            };
            var bar = MakeBar(_gridConfig, () => AddConfigRow(), () => DeleteGridRow(_gridConfig, 0));
            page.Controls.Add(_gridConfig);
            page.Controls.Add(bar);
            page.Controls.Add(top);
            _tabs.TabPages.Add(page);
            FillConfigGrid();
        }

        private void FillConfigGrid()
        {
            if (_mir2Ini == null || _cmbConfigSection.SelectedItem == null) return;
            string sec = _cmbConfigSection.SelectedItem as string;
            _gridConfig.Rows.Clear();
            foreach (var k in _mir2Ini.GetKeys(sec).OrderBy(x => x))
            {
                int r = _gridConfig.Rows.Add(k, _mir2Ini.GetValue(sec, k));
                _gridConfig.Rows[r].Cells[0].ReadOnly = true;
            }
        }

        private void AddConfigRow()
        {
            string sec = _cmbConfigSection?.SelectedItem as string;
            if (string.IsNullOrEmpty(sec) || _mir2Ini == null) return;
            using var dlg = new KeyValueInput("新增配置项");
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string key = dlg.KeyName;
                if (string.IsNullOrEmpty(key))
                {
                    MessageBox.Show(this, "键名不能为空。", "无效键名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_mir2Ini.GetKeys(sec).Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, "该键已存在，请先编辑现有项。", "键重复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _mir2Ini.SetValue(sec, key, dlg.KeyValue);
                FillConfigGrid();
            }
        }

        // ---------- KeyBinds ----------
        private void LoadKeyBinds()
        {
            var page = new TabPage("KeyBinds.ini (键位)");
            if (!File.Exists(_keyBinds))
            {
                page.Controls.Add(new Label { Text = "未找到 " + _keyBinds, Dock = DockStyle.Fill });
                _tabs.TabPages.Add(page);
                return;
            }
            _keysIni = new IniFile(_keyBinds);

            _gridKeys = MakeGrid();
            _gridKeys.Columns.Clear();
            _gridKeys.Columns.Add("func", "功能（节名）");
            var keyCol = new DataGridViewComboBoxColumn
            {
                Name = "key", HeaderText = "按键 RequireKey", DataPropertyName = "key"
            };
            foreach (var k in Enum.GetNames(typeof(Keys))) keyCol.Items.Add(k);
            keyCol.Items.Add("");
            _gridKeys.Columns.Add(keyCol);
            foreach (var mod in new[] { "RequireAlt", "RequireShift", "RequireTilde", "RequireCtrl" })
            {
                var c = new DataGridViewComboBoxColumn
                {
                    Name = mod, HeaderText = mod + " (0/1/2)"
                };
                c.Items.AddRange("0", "1", "2");
                _gridKeys.Columns.Add(c);
            }
            _gridKeys.Columns[0].ReadOnly = true;

            // 列宽权重：Fill 模式下 Width 无效，用 FillWeight 控制比例，避免 6 列等分导致"值/功能"列被挤窄。
            _gridKeys.Columns[0].FillWeight = 200;
            _gridKeys.Columns[1].FillWeight = 200;
            for (int ci = 2; ci < _gridKeys.Columns.Count; ci++) _gridKeys.Columns[ci].FillWeight = 85;

            // INI 中可能含 ComboBox 预置项之外的值（如历史遗留键位），先补进 Items，
            // 否则 DataGridView 在显示或提交时抛"ComboBoxCell 值无效"异常。
            foreach (DataGridViewComboBoxColumn col in _gridKeys.Columns.OfType<DataGridViewComboBoxColumn>())
            {
                string field = col.Name == "key" ? "RequireKey" : col.Name;
                foreach (string sec0 in _keysIni.Sections)
                {
                    string v0 = _keysIni.GetValue(sec0, field);
                    if (!string.IsNullOrEmpty(v0) && !col.Items.Contains(v0)) col.Items.Add(v0);
                }
            }

            _gridKeys.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
                var col = _gridKeys.Columns[e.ColumnIndex];
                if (col is not DataGridViewComboBoxColumn) return;
                string sec = _gridKeys.Rows[e.RowIndex].Cells[0].Value?.ToString();
                if (string.IsNullOrEmpty(sec)) return;
                // 只写回本次编辑的那一列，避免把同一行未编辑列（可能含外部改动）一并覆盖。
                string v = _gridKeys.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
                string field = col.Name == "key" ? "RequireKey" : col.Name;
                _keysIni.SetValue(sec, field, v);
            };
            page.Controls.Add(_gridKeys);
            page.Controls.Add(new Label
            {
                Text = "修饰键：0=不能按此键 / 1=必须按下 / 2=无关（默认）。按键值参考 System.Windows.Forms.Keys。",
                Dock = DockStyle.Top, Height = 28, ForeColor = Color.Gray,
                AutoSize = false, Padding = new Padding(6, 6, 0, 0)
            });
            _tabs.TabPages.Add(page);

            foreach (var sec in _keysIni.Sections)
            {
                if (sec.Equals("Guide", StringComparison.OrdinalIgnoreCase)) continue;
                string rk = _keysIni.GetValue(sec, "RequireKey");
                if (rk.Length == 0) continue; // 非键位节跳过
                int r = _gridKeys.Rows.Add(
                    sec, rk,
                    _keysIni.GetValue(sec, "RequireAlt"),
                    _keysIni.GetValue(sec, "RequireShift"),
                    _keysIni.GetValue(sec, "RequireTilde"),
                    _keysIni.GetValue(sec, "RequireCtrl"));
            }
        }

        // ---------- Localization ----------
        private void LoadLocalization()
        {
            var page = new TabPage("Localization (文案)");
            if (!Directory.Exists(_locDir))
            {
                page.Controls.Add(new Label { Text = "未找到 " + _locDir, Dock = DockStyle.Fill });
                _tabs.TabPages.Add(page);
                return;
            }
            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "语言：", Location = new Point(10, 12), AutoSize = true });
            _cmbLang = new ComboBox { Location = new Point(55, 8), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var f in Directory.GetFiles(_locDir, "*.json"))
                _cmbLang.Items.Add(Path.GetFileName(f));
            top.Controls.Add(_cmbLang);
            top.Controls.Add(new Label { Text = "节：", Location = new Point(210, 12), AutoSize = true });
            _cmbJsonSection = new ComboBox { Location = new Point(250, 8), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            top.Controls.Add(_cmbJsonSection);
            top.Controls.Add(new Label
            {
                Text = "可直接编辑中文文案；Enum 节为枚举显示名。",
                Location = new Point(410, 12), AutoSize = true, ForeColor = Color.Gray
            });
            _cmbLang.SelectedIndexChanged += (s, e) =>
            {
                if (_locLangGuard) return;
                // 切语言会用磁盘内容覆盖内存文档；若有未保存文案，先确认避免静默丢失。
                if (_locDirty)
                {
                    var r = MessageBox.Show(this,
                        "当前文案有未保存的修改。是否先保存再切换？\n（否=丢弃修改，取消=留在当前语言）",
                        "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (r == DialogResult.Cancel)
                    {
                        _locLangGuard = true;
                        _cmbLang.SelectedIndex = _locLangIndex;
                        _locLangGuard = false;
                        return;
                    }
                    if (r == DialogResult.Yes && !SaveLocalization()) return; // 保存失败则不切换
                }
                LoadLocFile();
            };
            _cmbJsonSection.SelectedIndexChanged += (s, e) => FillLocGrid();

            _gridLoc = MakeGrid();
            _gridLoc.Columns[0].FillWeight = 140;
            _gridLoc.Columns[1].FillWeight = 400; // 文案通常较长，值列留足宽度避免截断
            _gridLoc.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 1 || _locDoc == null) return;
                string sec = _cmbJsonSection.SelectedItem as string;
                string key = _gridLoc.Rows[e.RowIndex].Cells[0].Value as string;
                if (sec != null && key != null && _locDoc.TryGetValue(sec, out var dict))
                {
                    string val = _gridLoc.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "";
                    if (!dict.TryGetValue(key, out var old) || old != val) _locDirty = true;
                    dict[key] = val;
                }
            };
            page.Controls.Add(_gridLoc);
            page.Controls.Add(top);
            _tabs.TabPages.Add(page);

            if (_cmbLang.Items.Count > 0) _cmbLang.SelectedIndex = 0;
        }

        private void LoadLocFile()
        {
            if (_cmbLang.SelectedItem == null) return;
            _locPath = Path.Combine(_locDir, _cmbLang.SelectedItem as string);
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _locDoc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    File.ReadAllText(_locPath), opts) ?? new();
                _cmbJsonSection.Items.Clear();
                foreach (var k in _locDoc.Keys) _cmbJsonSection.Items.Add(k);
                if (_cmbJsonSection.Items.Count > 0) _cmbJsonSection.SelectedIndex = 0;
                _locDirty = false;                       // 新载入的内容与磁盘一致
                _locLangIndex = _cmbLang.SelectedIndex;  // 记录已提交索引，供 Cancel 还原
            }
            catch (Exception ex)
            {
                // 反序列化失败时清空旧文档，避免界面显示与磁盘文件不一致的陈旧内容。
                _locDoc = new();
                _cmbJsonSection.Items.Clear();
                _gridLoc?.Rows.Clear();
                _locDirty = false;
                MessageBox.Show(this, "读取文案失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FillLocGrid()
        {
            if (_locDoc == null || _cmbJsonSection.SelectedItem == null) return;
            string sec = _cmbJsonSection.SelectedItem as string;
            _gridLoc.Rows.Clear();
            if (!_locDoc.TryGetValue(sec, out var dict)) return;
            foreach (var kv in dict.OrderBy(x => x.Key))
            {
                int r = _gridLoc.Rows.Add(kv.Key, kv.Value);
                _gridLoc.Rows[r].Cells[0].ReadOnly = true;
            }
        }

        // ---------- 通用保存/载入 ----------
        private void SaveCurrent()
        {
            try
            {
                string tab = _tabs.SelectedTab?.Text ?? "";
                if (tab.StartsWith("Mir2Config"))
                {
                    if (_mir2Ini == null)
                    {
                        MessageBox.Show(this, "Mir2Config.ini 未加载，无法保存。", "未加载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _mir2Ini.Save();
                }
                else if (tab.StartsWith("KeyBinds"))
                {
                    if (_keysIni == null)
                    {
                        MessageBox.Show(this, "KeyBinds.ini 未加载，无法保存。", "未加载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _keysIni.Save();
                }
                else if (tab.StartsWith("Localization"))
                {
                    if (!SaveLocalization()) return;
                }
                else return;
                MessageBox.Show(this, "已保存：" + tab, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>保存当前 Localization 文档到磁盘；成功返回 true 并清 dirty。供保存/切换语言/关闭共用。</summary>
        private bool SaveLocalization()
        {
            if (_locPath == null || _locDoc == null)
            {
                MessageBox.Show(this, "文案文件未加载，无法保存。", "未加载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(_locDoc, opts).Replace("\n", "\r\n");
                File.WriteAllText(_locPath, json, new UTF8Encoding(false));
                _locDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存文案失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ReloadCurrent()
        {
            // 重新载入会用磁盘内容覆盖内存；若有未保存文案先确认，避免静默丢失。
            if (_locDirty)
            {
                var r = MessageBox.Show(this,
                    "当前文案有未保存的修改，重新载入将丢弃它们。是否先保存？\n（否=丢弃修改，取消=不重新载入）",
                    "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes && !SaveLocalization()) return;
            }
            // 三个 Load* 每次都会 new TabPage 并 Add；先把旧页整体释放清空，避免重复叠加页签与状态泄漏。
            int idx = _tabs.SelectedIndex;
            var pages = _tabs.TabPages.Cast<TabPage>().ToList();
            _tabs.TabPages.Clear();
            foreach (var p in pages) p.Dispose();
            _mir2Ini = null; _keysIni = null; _locDoc = null; _locPath = null;
            _gridConfig = null; _gridKeys = null; _gridLoc = null;
            _cmbConfigSection = null; _cmbLang = null; _cmbJsonSection = null;

            LoadMir2Config();
            LoadKeyBinds();
            LoadLocalization();
            if (idx >= 0 && idx < _tabs.TabPages.Count) _tabs.SelectedIndex = idx;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { SaveCurrent(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_locDirty)
            {
                var r = MessageBox.Show(this,
                    "当前文案有未保存的修改。是否先保存再关闭？\n（否=丢弃修改，取消=不关闭）",
                    "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes && !SaveLocalization()) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }

        // ---------- 控件工厂 ----------
        private static DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            g.Columns.Add("key", "键 / 名称");
            g.Columns.Add("value", "值");
            g.Columns[0].FillWeight = 160;
            g.Columns[1].FillWeight = 340; // 键名列窄、值列宽，避免值被挤 truncation
            return g;
        }

        private static Panel MakeBar(DataGridView grid, Action onAdd, Action onDelete)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 38 };
            var add = new Button { Text = "新增", Location = new Point(10, 5), Width = 70 };
            var del = new Button { Text = "删除选中", Location = new Point(90, 5), Width = 80 };
            add.Click += (s, e) => onAdd();
            del.Click += (s, e) => onDelete();
            bar.Controls.AddRange(new Control[] { add, del });
            return bar;
        }

        private void DeleteGridRow(DataGridView grid, int keyCol)
        {
            if (grid?.SelectedRows.Count == 0) return;
            if (MessageBox.Show(this, "确定删除选中项？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            var row = grid.SelectedRows[0];
            string sec = _cmbConfigSection?.SelectedItem as string;
            string key = row.Cells[keyCol].Value?.ToString();
            if (!string.IsNullOrEmpty(sec) && !string.IsNullOrEmpty(key)) _mir2Ini?.RemoveKey(sec, key);
            grid.Rows.Remove(row);
        }
    }

    /// <summary>新增键值输入对话框。</summary>
    internal sealed class KeyValueInput : Form
    {
        private readonly TextBox _k;
        private readonly TextBox _v;
        public string KeyName => _k.Text.Trim();
        public string KeyValue => _v.Text;

        public KeyValueInput(string title)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 110);
            _k = new TextBox { Location = new Point(70, 12), Width = 250 };
            _v = new TextBox { Location = new Point(70, 48), Width = 250 };
            var btnOk = new Button { Text = "确定", Location = new Point(120, 78), Width = 80, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "取消", Location = new Point(210, 78), Width = 80, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[]
            {
                new Label { Text = "键:", Location = new Point(15, 16), AutoSize = true }, _k,
                new Label { Text = "值:", Location = new Point(15, 52), AutoSize = true }, _v,
                btnOk, btnCancel,
            });
            AcceptButton = btnOk;
            CancelButton = btnCancel; // Esc 取消
        }
    }
}
