using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 自定义命令导入导出器（#26）。
    /// 核心文件 CustomCommands.txt 实证为 UTF-8 BOM + LF（非 GBK/CRLF）：
    ///   ;;注释
    ///   [@_CUSTOMCOMMAND(命令名)]
    ///   #INCLUDE [相对Envir路径] @Main
    /// 注册链：NPCScript.ParseDefault 扫 [@_CUSTOMCOMMAND(...)]，玩家喊话触发。
    /// </summary>
    public class CustomCommandForm : Form
    {
        private readonly string _serverDir;
        private readonly string _cmdFile;
        private readonly string _helpFile;
        private DataGridView _grid;
        private Label _lblStatus;

        private class Entry
        {
            public string Comment = "";
            public string Name = "";
            public string IncludePath = "";
            public string Page = "Main";
            public bool Valid => !string.IsNullOrWhiteSpace(Name);
        }
        private List<Entry> _entries = new();

        public CustomCommandForm(string serverDir)
        {
            _serverDir = serverDir;
            _cmdFile = Path.Combine(serverDir, "Envir", "SystemScripts", "00Default", "CustomCommands.txt");
            _helpFile = Path.Combine(serverDir, "Envir", "NPCs", "NPC命令说明.txt");

            Text = "自定义命令导入导出器";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(960, 620);

            BuildUI();
            LoadCommands();
        }

        private void BuildUI()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 38 };
            var btnLoad = new Button { Text = "重新载入", Location = new System.Drawing.Point(10, 5), Width = 90 };
            var btnExport = new Button { Text = "导出到文件...", Location = new System.Drawing.Point(110, 5), Width = 110 };
            var btnImport = new Button { Text = "从文件导入...", Location = new System.Drawing.Point(230, 5), Width = 110 };
            var btnSave = new Button { Text = "保存到 CustomCommands.txt", Location = new System.Drawing.Point(350, 5), Width = 190 };
            var btnCheck = new Button { Text = "与命令说明对账", Location = new System.Drawing.Point(550, 5), Width = 130 };
            btnLoad.Click += (s, e) => LoadCommands();
            btnExport.Click += BtnExport_Click;
            btnImport.Click += BtnImport_Click;
            btnSave.Click += BtnSave_Click;
            btnCheck.Click += BtnCheck_Click;
            bar.Controls.AddRange(new Control[] { btnLoad, btnExport, btnImport, btnSave, btnCheck });

            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 26,
                Text = "", Padding = new System.Windows.Forms.Padding(6) };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Comment", HeaderText = "注释", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "命令名", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "IncludePath", HeaderText = "#INCLUDE 路径", Width = 380 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Page", HeaderText = "页", Width = 90 });

            Controls.Add(_grid);
            Controls.Add(bar);
            Controls.Add(_lblStatus);
        }

        private string ReadCmdFile(string path)
        {
            // 实证：UTF-8 BOM + LF。StreamReader detectEncodingFromByteOrderMarks 自动吃 BOM。
            using var sr = new StreamReader(path, new UTF8Encoding(false), true);
            return sr.ReadToEnd();
        }

        private void LoadCommands()
        {
            _entries.Clear();
            if (!File.Exists(_cmdFile))
            {
                _lblStatus.Text = "未找到 " + _cmdFile;
                Bind();
                return;
            }
            var lines = ReadCmdFile(_cmdFile).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            Entry cur = null;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith(";;")) { cur = new Entry { Comment = line.TrimStart(';').Trim() }; _entries.Add(cur); continue; }
                if (line.StartsWith("[@_CUSTOMCOMMAND(", StringComparison.OrdinalIgnoreCase))
                {
                    int a = line.IndexOf('('), b = line.IndexOf(')');
                    if (a >= 0 && b > a)
                    {
                        cur ??= new Entry();
                        cur.Name = line[(a + 1)..b].Trim();
                        if (!_entries.Contains(cur)) _entries.Add(cur);
                    }
                    continue;
                }
                if (line.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                {
                    // #INCLUDE [相对Envir路径] @Main
                    var m = System.Text.RegularExpressions.Regex.Match(line,
                        @"#INCLUDE\s+\[([^\]]+)\]\s+@?(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        cur ??= new Entry();
                        cur.IncludePath = m.Groups[1].Value.Trim();
                        cur.Page = m.Groups[2].Value.Trim();
                        if (!_entries.Contains(cur)) _entries.Add(cur);
                    }
                }
            }
            _entries.RemoveAll(e => !e.Valid);
            Bind();
            _lblStatus.Text = $"已载入 {_entries.Count} 条自定义命令（{_cmdFile}）";
        }

        private void Bind()
        {
            _grid.DataSource = null;
            _grid.DataSource = _entries;
        }

        private List<Entry> CollectFromGrid()
        {
            var list = new List<Entry>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                var e = new Entry
                {
                    Comment = row.Cells["Comment"].Value?.ToString() ?? "",
                    Name = row.Cells["Name"].Value?.ToString() ?? "",
                    IncludePath = row.Cells["IncludePath"].Value?.ToString() ?? "",
                    Page = row.Cells["Page"].Value?.ToString() ?? "Main"
                };
                if (e.Valid) list.Add(e);
            }
            return list;
        }

        private string Serialize(IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Comment)) sb.AppendLine(";;" + e.Comment.TrimStart(';'));
                sb.AppendLine($"[@_CUSTOMCOMMAND({e.Name})]");
                sb.AppendLine($"#INCLUDE [{e.IncludePath}] @{e.Page}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void WriteCmdFile(string path, string text)
        {
            string lf = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\n");
            // 实证：UTF-8 BOM + LF
            File.WriteAllText(path, lf, new UTF8Encoding(true));
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var list = CollectFromGrid();
            if (list.Count == 0) { MessageBox.Show(this, "没有可保存的命令。", "提示"); return; }
            // 重名检测
            var dup = list.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0)
            {
                MessageBox.Show(this, "存在重名命令: " + string.Join(", ", dup), "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, $"将保存 {list.Count} 条命令到 CustomCommands.txt（自动备份）。确定？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            try
            {
                string bak = _cmdFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                if (File.Exists(_cmdFile)) File.Copy(_cmdFile, bak, true);
                WriteCmdFile(_cmdFile, Serialize(list));
                MessageBox.Show(this, $"已保存并备份：\n{bak}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadCommands();
            }
            catch (Exception ex) { MessageBox.Show(this, "保存失败: " + ex.Message, "错误"); }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "文本文件|*.txt", FileName = "CustomCommands_export.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { WriteCmdFile(dlg.FileName, Serialize(CollectFromGrid())); MessageBox.Show(this, "已导出。", "完成"); }
            catch (Exception ex) { MessageBox.Show(this, "导出失败: " + ex.Message, "错误"); }
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog { Filter = "文本文件|*.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string text = ReadCmdFile(dlg.FileName);
                // 复用载入解析逻辑：临时替换后解析
                string orig = _cmdFile;
                var tmp = Path.Combine(Path.GetTempPath(), "ccimport_" + Guid.NewGuid().ToString("N") + ".txt");
                WriteCmdFile(tmp, text);
                var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                var imported = new List<Entry>();
                Entry cur = null;
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.StartsWith(";;")) { cur = new Entry { Comment = line.TrimStart(';').Trim() }; imported.Add(cur); }
                    else if (line.StartsWith("[@_CUSTOMCOMMAND(", StringComparison.OrdinalIgnoreCase))
                    {
                        int a = line.IndexOf('('), b = line.IndexOf(')');
                        if (a >= 0 && b > a) { cur ??= new Entry(); cur.Name = line[(a + 1)..b].Trim();
                            if (!imported.Contains(cur)) imported.Add(cur); }
                    }
                    else if (line.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line,
                            @"#INCLUDE\s+\[([^\]]+)\]\s+@?(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success) { cur ??= new Entry(); cur.IncludePath = m.Groups[1].Value.Trim();
                            cur.Page = m.Groups[2].Value.Trim(); if (!imported.Contains(cur)) imported.Add(cur); }
                    }
                }
                imported.RemoveAll(x => !x.Valid);
                File.Delete(tmp);
                _entries.AddRange(imported);
                Bind();
                _lblStatus.Text = $"已导入 {imported.Count} 条（未保存到磁盘，请核对后点保存）";
            }
            catch (Exception ex) { MessageBox.Show(this, "导入失败: " + ex.Message, "错误"); }
        }

        private void BtnCheck_Click(object sender, EventArgs e)
        {
            if (!File.Exists(_helpFile)) { MessageBox.Show(this, "未找到 NPC命令说明.txt", "提示"); return; }
            string help = File.ReadAllText(_helpFile, new UTF8Encoding(false)); // 实证：UTF-8 无BOM（非GBK）
            var names = CollectFromGrid().Select(x => x.Name).ToList();
            var conflicts = names.Where(n => System.Text.RegularExpressions.Regex.IsMatch(help,
                @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(n) + @"\s*[:：]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            MessageBox.Show(this, conflicts.Count == 0
                    ? $"{names.Count} 条自定义命令与内置命令说明无重名冲突。"
                    : $"发现 {conflicts.Count} 条与内置命令重名（可能覆盖）：\n{string.Join("\n", conflicts)}",
                "对账结果", MessageBoxButtons.OK,
                conflicts.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
