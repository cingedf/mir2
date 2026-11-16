using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 爆率文件格式化器（#25）。现网 Drops\*.txt 为 UTF-8 无 BOM/CRLF 扁平结构（编码自适应）。
    /// 双模式：仅排版（保真，默认）/树重建（需人工确认）。保存前自动备份。
    /// </summary>
    public class DropFormatForm : Form
    {
        private readonly string _dropsDir;
        private ListBox _lstFiles;
        private TextBox _txtFilter;
        private RadioButton _rbLayout, _rbRebuild;
        private TextBox _txtBefore, _txtAfter;
        private Label _lblSummary;
        private string _currentFile;
        private DropFormatter.FormatResult _lastResult;

        public DropFormatForm(string serverDir)
        {
            _dropsDir = Path.Combine(serverDir, "Envir", "Drops");
            Text = "爆率文件格式化器";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(1100, 700);

            if (!Directory.Exists(_dropsDir))
            {
                Controls.Add(new Label { Text = "未找到目录: " + _dropsDir, Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter });
                return;
            }

            BuildUI();
            LoadFiles(null);
        }

        private void BuildUI()
        {
            // 左：文件列表
            var left = new Panel { Dock = DockStyle.Left, Width = 280 };
            _txtFilter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "筛选文件..." };
            _txtFilter.TextChanged += (s, e) => LoadFiles(_txtFilter.Text);
            _lstFiles = new ListBox { Dock = DockStyle.Fill };
            _lstFiles.SelectedIndexChanged += (s, e) => PreviewSelected();
            left.Controls.Add(_lstFiles);
            left.Controls.Add(_txtFilter);

            // 右：预览对比
            var right = new Panel { Dock = DockStyle.Fill };
            var modeBar = new Panel { Dock = DockStyle.Top, Height = 34 };
            _rbLayout = new RadioButton { Text = "仅排版（保真，推荐）", Location = new System.Drawing.Point(10, 8),
                AutoSize = true, Checked = true };
            _rbRebuild = new RadioButton { Text = "树重建（完整重排，需核对）", Location = new System.Drawing.Point(200, 8),
                AutoSize = true };
            _rbLayout.CheckedChanged += (s, e) => PreviewSelected();
            _rbRebuild.CheckedChanged += (s, e) => PreviewSelected();
            var btnApply = new Button { Text = "应用并保存", Location = new System.Drawing.Point(430, 4), Width = 100 };
            btnApply.Click += BtnApply_Click;
            modeBar.Controls.AddRange(new Control[] { _rbLayout, _rbRebuild, btnApply });

            _lblSummary = new Label { Dock = DockStyle.Top, Height = 28, Text = "选择文件查看格式化预览",
                Padding = new System.Windows.Forms.Padding(6) };

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            _txtBefore = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F) };
            _txtAfter = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F) };
            split.Panel1.Controls.Add(_txtBefore);
            split.Panel2.Controls.Add(_txtAfter);
            split.Panel1.Controls.Add(new Label { Text = "原始（保持原编码）", Dock = DockStyle.Top });
            split.Panel2.Controls.Add(new Label { Text = "格式化预览（保存时按原编码写回）", Dock = DockStyle.Top });

            right.Controls.Add(split);
            right.Controls.Add(_lblSummary);
            right.Controls.Add(modeBar);

            Controls.Add(right);
            Controls.Add(left);
        }

        private void LoadFiles(string filter)
        {
            _lstFiles.BeginUpdate();
            _lstFiles.Items.Clear();
            foreach (var f in Directory.GetFiles(_dropsDir, "*.txt").OrderBy(x => x))
            {
                string name = Path.GetFileName(f);
                if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _lstFiles.Items.Add(f);
            }
            _lstFiles.EndUpdate();
            if (_lstFiles.Items.Count > 0) _lstFiles.SelectedIndex = 0;
        }

        private void PreviewSelected()
        {
            _currentFile = _lstFiles.SelectedItem as string;
            if (string.IsNullOrEmpty(_currentFile) || !File.Exists(_currentFile))
            {
                _txtBefore.Text = _txtAfter.Text = "";
                return;
            }
            try
            {
                string text = DropFormatter.ReadText(_currentFile);
                var mode = _rbRebuild.Checked ? DropFormatter.FormatMode.Rebuild : DropFormatter.FormatMode.LayoutOnly;
                _lastResult = DropFormatter.Format(text, mode);
                // 统一换行后再显示：Original 可能是 CRLF，直接 Replace("\n","\r\n") 会产生 \r\r\n 重复空行
                _txtBefore.Text = NormCrlf(_lastResult.Original);
                _txtAfter.Text = NormCrlf(_lastResult.Formatted);
                _lblSummary.Text = $"{Path.GetFileName(_currentFile)} — 变更 {_lastResult.Changes.Count} 处，行数 {_lastResult.LinesBefore}→{_lastResult.LinesAfter}";
            }
            catch (Exception ex)
            {
                _lblSummary.Text = "读取失败: " + ex.Message;
            }
        }

        private static string NormCrlf(string s) =>
            string.IsNullOrEmpty(s) ? s : s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFile) || _lastResult == null) return;
            if (_lastResult.Changes.Count == 0)
            {
                MessageBox.Show(this, "无变更需要保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var mode = _rbRebuild.Checked ? "树重建" : "仅排版";
            if (MessageBox.Show(this,
                    $"将按【{mode}】格式化 {Path.GetFileName(_currentFile)}，\n共 {_lastResult.Changes.Count} 处变更。\n" +
                    "保存前会自动备份（.bak-时间戳）。确定继续？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            try
            {
                string bak = _currentFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(_currentFile, bak, true);
                DropFormatter.WriteText(_currentFile, _lastResult.Formatted);
                MessageBox.Show(this, $"已保存并备份：\n{bak}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                PreviewSelected();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
