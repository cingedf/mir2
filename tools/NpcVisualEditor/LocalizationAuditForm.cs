using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 本地化缺失键检测：扫描 Client\Localization 与 Server\Localization 的 Chinese/English.json，
    /// 对比 Text 文案键、Enum 枚举键，列出缺键清单（只读，不修改 json）。对标 BOO/Vastopia 本地化管理。
    /// </summary>
    public class LocalizationAuditForm : Form
    {
        private readonly string _serverDir, _clientDir;
        private readonly DataGridView _grid;
        private readonly Label _lbl;

        public LocalizationAuditForm(string serverDir, string clientDir)
        {
            _serverDir = serverDir;
            _clientDir = clientDir;
            Text = "本地化缺失键检测";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 620);
            MinimumSize = new Size(880, 620);   // 顶部三按钮+说明 Label 为绝对定位，禁止缩小以免裁切

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };
            top.Controls.Add(new Button { Text = "扫描客户端（文本/枚举）", Location = new Point(10, 9), Width = 180 }.Tap(b =>
                b.Click += (s, e) => Scan(false)));
            top.Controls.Add(new Button { Text = "扫描服务端枚举 (12 类型)", Location = new Point(200, 9), Width = 190 }.Tap(b =>
                b.Click += (s, e) => Scan(true)));
            top.Controls.Add(new Button { Text = "导出报告", Location = new Point(400, 9), Width = 100 }.Tap(b =>
                b.Click += (s, e) => Export()));
            top.Controls.Add(new Label { Text = "只读检测 · 严格大小写 · 过滤中文成员名/占位符误报", Location = new Point(510, 13), AutoSize = true });
            Controls.Add(top);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            Controls.Add(_grid);

            _lbl = new Label { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Text = "  点击上方按钮开始扫描" };
            var lblTip = new ToolTip();
            _lbl.MouseHover += (s, e) => lblTip.SetToolTip(_lbl, _lbl.Text);
            Controls.Add(_lbl);
        }

        private void Scan(bool server)
        {
            try
            {
                LocalizationAudit.AuditResult r;
                if (server)
                {
                    // 12 枚举类型（与 Language.localizationEnumTypes 一致），类型在全局命名空间
                    var types = new[]
                    {
                        typeof(MirClass), typeof(RequiredClass), typeof(RequiredType), typeof(Stat),
                        typeof(BuffType), typeof(PoisonType), typeof(AwakeType), typeof(MonsterType),
                        typeof(HeroBehaviour), typeof(BindMode), typeof(SpecialItemMode), typeof(LightSetting)
                    };
                    var expected = LocalizationAudit.ExpectedEnumKeys(types);
                    r = LocalizationAudit.AuditServerEnums(Path.Combine(_serverDir, "Localization"), expected);
                }
                else
                {
                    r = LocalizationAudit.Audit(Path.Combine(_clientDir, "Localization"), "Client");
                }
                Show(r);
            }
            catch (Exception ex)
            {
                _lbl.Text = "  扫描失败: " + ex.Message;
            }
        }

        private void Show(LocalizationAudit.AuditResult r)
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            _grid.Columns.Add("Scope", "范围");
            _grid.Columns.Add("File", "文件");
            _grid.Columns.Add("Section", "节");
            _grid.Columns.Add("Key", "缺失键");
            _grid.Columns.Add("Detail", "说明");
            _grid.Columns[0].FillWeight = 60;
            _grid.Columns[1].FillWeight = 200;
            _grid.Columns[2].FillWeight = 80;
            _grid.Columns[3].FillWeight = 160;
            _grid.Columns[4].FillWeight = 400;
            foreach (var m in r.Missing.OrderBy(m => m.Section).ThenBy(m => m.Key))
                _grid.Rows.Add(r.Scope, m.File, m.Section, m.Key, m.Detail);
            string counts = string.Join("  ", r.Counts.Select(kv => kv.Key + "=" + kv.Value));
            string verdict = r.Missing.Count == 0 ? "无缺失" : "见上表";
            _lbl.Text = $"  {r.Scope} — 缺失 {r.Missing.Count} 项    条目统计: {counts}（{verdict}）";
        }

        private void Export()
        {
            if (_grid.Rows.Count == 0) { MessageBox.Show("无可导出的结果，请先扫描。"); return; }
            var lines = new List<string> { "范围\t文件\t节\t缺失键\t说明" };
            foreach (DataGridViewRow row in _grid.Rows)
                lines.Add(string.Join("\t", row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "")));
            var dlg = new SaveFileDialog { Filter = "文本文件|*.txt", FileName = "本地化缺失键报告.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllLines(dlg.FileName, lines, new System.Text.UTF8Encoding(false));
            MessageBox.Show("已导出: " + dlg.FileName);
        }
    }

    internal static class ControlTapExt
    {
        public static T Tap<T>(this T c, Action<T> act) { act(c); return c; }
    }
}
