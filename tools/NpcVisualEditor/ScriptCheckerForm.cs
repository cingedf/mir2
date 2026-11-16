using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 脚本检查中心：批量扫描 NPC 脚本 + SystemScripts，输出标签缺失/参数异常/变量误用。
    /// 纯只读分析，不修改、不执行任何脚本。
    /// </summary>
    public class ScriptCheckerForm : Form
    {
        private readonly string _serverDir;
        private readonly TextBox _txtFilter;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary;
        private readonly HashSet<string> _commands;
        private List<ScriptChecker.FileReport> _reports = new();

        public ScriptCheckerForm(string serverDir)
        {
            _serverDir = serverDir;
            Text = "脚本检查中心";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 680);

            _commands = ScriptChecker.LoadCommands(
                Path.Combine(serverDir, "Envir", "NPCs", "NPC命令说明.txt"));

            var top = new Panel { Dock = DockStyle.Top, Height = 48 };
            top.Controls.Add(new Label { Text = "文件筛选:", Location = new Point(12, 15), AutoSize = true });
            _txtFilter = new TextBox { Location = new Point(80, 11), Width = 260 };
            var btnScanNpc = new Button { Text = "扫描 NPC 脚本", Location = new Point(360, 8), Width = 110, Height = 28 };
            var btnScanSys = new Button { Text = "扫描系统脚本", Location = new Point(480, 8), Width = 110, Height = 28 };
            var btnExport = new Button { Text = "导出报告", Location = new Point(600, 8), Width = 90, Height = 28 };
            btnScanNpc.Click += (s, e) => Scan(Path.Combine(serverDir, "Envir", "NPCs"));
            btnScanSys.Click += (s, e) => Scan(Path.Combine(serverDir, "SystemScripts"));
            btnExport.Click += (s, e) => Export();
            top.Controls.AddRange(new Control[] { _txtFilter, btnScanNpc, btnScanSys, btnExport });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
            };
            // Fill 模式下 Width 会被忽略，必须用 FillWeight 控制列宽比例，
            // 否则五列等分导致「级别/行」过宽、「问题」过窄被截断。
            _grid.Columns.AddRange(new[] {
                new DataGridViewTextBoxColumn { Name = "level", HeaderText = "级别", FillWeight = 60 },
                new DataGridViewTextBoxColumn { Name = "file", HeaderText = "文件", FillWeight = 200 },
                new DataGridViewTextBoxColumn { Name = "line", HeaderText = "行", FillWeight = 45 },
                new DataGridViewTextBoxColumn { Name = "section", HeaderText = "段", FillWeight = 100 },
                new DataGridViewTextBoxColumn { Name = "msg", HeaderText = "问题", FillWeight = 500 },
            });
            _grid.CellDoubleClick += (s, e) => OpenSource(e.RowIndex);

            _lblSummary = new Label { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                Text = "就绪。选择一个脚本目录开始扫描（NPC 脚本 / 系统脚本）。" };

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(_lblSummary);
        }

        private void Scan(string dir)
        {
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "目录不存在: " + dir, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                string filter = _txtFilter.Text.Trim();
                var files = Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories)
                    .Where(f => string.IsNullOrEmpty(filter) || Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(f => f).ToArray();

                // 单文件失败（被占用/编码异常）不应让整批扫描崩溃。
                _reports = files.AsParallel().Select(f =>
                {
                    try { return ScriptChecker.CheckFile(f, _commands); }
                    catch (Exception ex)
                    {
                        var bad = new ScriptChecker.FileReport { File = f };
                        bad.Issues.Add(new ScriptChecker.Issue
                        {
                            Level = ScriptChecker.Severity.Warning, File = f, Line = 0, Section = "",
                            Message = "无法读取该文件: " + ex.Message
                        });
                        return bad;
                    }
                }).ToList();

                var rows = _reports.SelectMany(r => r.Issues.Select(i => new
                {
                    i.Level, i.File, i.Line, i.Section, i.Message
                })).OrderBy(r => r.Level).ThenBy(r => r.File).ThenBy(r => r.Line).ToList();

                _grid.Rows.Clear();
                foreach (var r in rows)
                {
                    int idx = _grid.Rows.Add(LevelText(r.Level), Path.GetFileName(r.File), r.Line, r.Section, r.Message);
                    _grid.Rows[idx].Tag = r.File;
                    if (r.Level == ScriptChecker.Severity.Error)
                        _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.Firebrick;
                    else if (r.Level == ScriptChecker.Severity.Warning)
                        _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.DarkOrange;
                }

                int err = rows.Count(r => r.Level == ScriptChecker.Severity.Error);
                int warn = rows.Count(r => r.Level == ScriptChecker.Severity.Warning);
                int vars = _reports.Sum(r => r.Variables);
                _lblSummary.Text = $"扫描 {files.Length} 个文件 | 段 {_reports.Sum(r => r.Segments)} | 引用 {_reports.Sum(r => r.References)} | 变量 {vars} | 错误 {err} | 警告 {warn}";
            }
            finally { Cursor = Cursors.Default; }
        }

        private static string LevelText(ScriptChecker.Severity level) => level switch
        {
            ScriptChecker.Severity.Error => "错误",
            ScriptChecker.Severity.Warning => "警告",
            _ => "提示",
        };

        private void OpenSource(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            string file = _grid.Rows[rowIndex].Tag as string;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            // 用系统默认文本编辑器打开（只读查看，本工具不修改）
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开源文件：\n" + file + "\n\n" + ex.Message,
                    "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Export()
        {
            if (_reports.Count == 0) { MessageBox.Show("请先扫描。", "提示"); return; }
            using var dlg = new SaveFileDialog { Filter = "文本文件|*.txt", FileName = "脚本检查报告.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using var w = new StreamWriter(dlg.FileName);
                w.WriteLine($"脚本检查报告 {DateTime.Now}");
                w.WriteLine(_lblSummary.Text);
                w.WriteLine();
                foreach (var r in _reports)
                    foreach (var i in r.Issues)
                        w.WriteLine(i.Text);
                MessageBox.Show("已导出。", "完成");
            }
            catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误"); }
        }
    }
}
