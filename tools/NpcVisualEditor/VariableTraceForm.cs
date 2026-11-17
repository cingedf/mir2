using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 变量引用链追踪：跨文件反向索引，点选变量列出所有赋值/引用/显示位置，双击跳转源文件。
    /// 对标传奇F1助手的变量追踪（跨文件引用链/树状展示/跳转）。
    /// </summary>
    public class VariableTraceForm : Form
    {
        private readonly string _serverDir;
        private readonly TextBox _txtFilter;
        private readonly ListBox _lstVars;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary;
        private VariableTrace.Index _index;
        private VariableTrace.VarInfo _selected;

        public VariableTraceForm(string serverDir)
        {
            _serverDir = serverDir;
            Text = "变量引用链追踪";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1150, 700);

            // 顶部工具条
            var top = new Panel { Dock = DockStyle.Top, Height = 48 };
            top.Controls.Add(new Label { Text = "变量名筛选:", Location = new Point(12, 15), AutoSize = true });
            _txtFilter = new TextBox { Location = new Point(95, 11), Width = 160 };
            _txtFilter.TextChanged += (s, e) => FillVars();
            var btnScan = new Button { Text = "扫描全部脚本", Location = new Point(270, 8), Width = 120, Height = 28 };
            btnScan.Click += (s, e) => Scan();
            var btnExport = new Button { Text = "导出引用报告", Location = new Point(400, 8), Width = 120, Height = 28 };
            btnExport.Click += (s, e) => Export();
            top.Controls.AddRange(new Control[] { _txtFilter, btnScan, btnExport });

            // 左：变量列表
            var leftPanel = new Panel { Dock = DockStyle.Left, Width = 260 };
            leftPanel.Controls.Add(new Label { Text = "变量（赋值/引用/显示）", Dock = DockStyle.Top,
                Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) });
            _lstVars = new ListBox { Dock = DockStyle.Fill };
            _lstVars.SelectedIndexChanged += (s, e) =>
            {
                _selected = _lstVars.SelectedItem as VariableTrace.VarInfo;
                FillOccurrences();
            };
            leftPanel.Controls.Add(_lstVars);

            // 右：引用明细
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
            };
            // Fill 模式下 Width 被忽略，须用 FillWeight 控制列宽比例
            _grid.Columns.AddRange(new[] {
                new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "类型", FillWeight = 50 },
                new DataGridViewTextBoxColumn { Name = "file", HeaderText = "文件", FillWeight = 260 },
                new DataGridViewTextBoxColumn { Name = "line", HeaderText = "行", FillWeight = 45 },
                new DataGridViewTextBoxColumn { Name = "section", HeaderText = "段", FillWeight = 110 },
                new DataGridViewTextBoxColumn { Name = "text", HeaderText = "内容", FillWeight = 520 },
            });
            _grid.CellDoubleClick += (s, e) => OpenSource(e.RowIndex);

            _lblSummary = new Label { Dock = DockStyle.Bottom, Height = 28,
                TextAlign = ContentAlignment.MiddleLeft, Text = "就绪。点击「扫描全部脚本」建立引用链索引。" };

            // Splitter 须在目标 Left 控件之前添加，FindTarget 才指向 leftPanel
            Controls.Add(_grid);
            Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4, MinSize = 160 });
            Controls.Add(leftPanel);
            Controls.Add(top);
            Controls.Add(_lblSummary);

            Load += (s, e) => Scan();
        }

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var files = VariableTrace.ScanFiles(_serverDir).ToList();
                _index = VariableTrace.Build(files);
                FillVars();
                int totalOcc = _index.Vars.Values.Sum(v => v.Occurrences.Count);
                _lblSummary.Text = $"扫描 {_index.Files} 个文件 | 变量 {_index.Vars.Count} 个 | 出现 {totalOcc} 处 | " +
                                   $"赋值 {_index.Vars.Values.Sum(v => v.Assigns)} | 引用 {_index.Vars.Values.Sum(v => v.Refs)} | 显示 {_index.Vars.Values.Sum(v => v.Displays)}";
            }
            finally { Cursor = Cursors.Default; }
        }

        private void FillVars()
        {
            if (_index == null) return;
            string filter = _txtFilter.Text.Trim();
            _lstVars.BeginUpdate();
            _lstVars.Items.Clear();
            foreach (var v in _index.Vars.Values
                         .Where(v => string.IsNullOrEmpty(filter) ||
                                     v.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
                _lstVars.Items.Add(v);
            _lstVars.EndUpdate();
        }

        private void FillOccurrences()
        {
            _grid.Rows.Clear();
            if (_selected == null) return;
            foreach (var o in _selected.Occurrences.OrderBy(o => o.File).ThenBy(o => o.Line))
            {
                int idx = _grid.Rows.Add(o.KindText, Path.GetFileName(o.File), o.Line, o.Section, o.LineText);
                _grid.Rows[idx].Tag = o.File;
                if (o.Kind == VariableTrace.UseKind.Assign)
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.ForestGreen;
                else if (o.Kind == VariableTrace.UseKind.Display)
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.SteelBlue;
            }
        }

        private void OpenSource(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            string file = _grid.Rows[rowIndex].Tag as string;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            try { using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开源文件：\n" + file + "\n\n" + ex.Message,
                    "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Export()
        {
            if (_index == null || _index.Vars.Count == 0) { MessageBox.Show("请先扫描。", "提示"); return; }
            using var dlg = new SaveFileDialog { Filter = "文本文件|*.txt", FileName = "变量引用链报告.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using var w = new StreamWriter(dlg.FileName);
                w.WriteLine($"变量引用链报告 {DateTime.Now}");
                w.WriteLine(_lblSummary.Text);
                w.WriteLine();
                foreach (var v in _index.Vars.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
                {
                    w.WriteLine($"===== {v.Name}  赋值{v.Assigns} 引用{v.Refs} 显示{v.Displays} =====");
                    foreach (var o in v.Occurrences.OrderBy(o => o.File).ThenBy(o => o.Line))
                        w.WriteLine($"  [{o.KindText}] {o.File}:{o.Line} ({o.Section}) {o.LineText}");
                }
                MessageBox.Show("已导出。", "完成");
            }
            catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误"); }
        }
    }
}
