using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 变量管理面板：扫描全部脚本，统计玩家 NPC 变量（单字母+单数字，A1..Z9）
    /// 的定义/引用/显示分布，给出各字母段的空闲编号建议。
    /// Crystal 血统变量作用域 = player.NPCVar，显示用 &lt;$OUTPUT(A1)&gt;。
    /// 只读分析，不修改脚本。
    /// </summary>
    public class VariableManagerForm : Form
    {
        private readonly string _serverDir;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary;
        private readonly TextBox _txtFilter;

        // MOV/CALC/INC/DEC 第一个参数 = 变量定义；<$OUTPUT(A1)> = 显示引用
        private static readonly Regex RxAssign = new(@"^\s*(MOV|CALC|INC|DEC|SUM|CHECKCALC)\s+([A-Za-z][0-9])(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxVar = new(@"(?<![A-Za-z0-9])([A-Za-z][0-9])(?![A-Za-z0-9])", RegexOptions.Compiled);

        private class VarStat
        {
            public string Name;
            public int Assign, Refs, Displays;
        }

        public VariableManagerForm(string serverDir)
        {
            _serverDir = serverDir;
            Text = "变量管理面板";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(820, 640);

            var top = new Panel { Dock = DockStyle.Top, Height = 48 };
            top.Controls.Add(new Label { Text = "脚本筛选:", Location = new Point(12, 15), AutoSize = true });
            _txtFilter = new TextBox { Location = new Point(80, 11), Width = 260 };
            var btnScan = new Button { Text = "扫描变量", Location = new Point(360, 8), Width = 100, Height = 28 };
            btnScan.Click += (s, e) => Scan();
            top.Controls.AddRange(new Control[] { _txtFilter, btnScan });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            _grid.Columns.AddRange(new[] {
                new DataGridViewTextBoxColumn { HeaderText = "变量名", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "赋值次数", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "引用次数", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "显示次数", Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "建议", Width = 320 },
            });

            _lblSummary = new Label { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                Text = "就绪。扫描后可查看各变量使用情况与空闲编号建议。" };

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(_lblSummary);
        }

        private IEnumerable<string> ScriptFiles()
        {
            foreach (var d in new[] {
                Path.Combine(_serverDir, "Envir", "NPCs"),
                Path.Combine(_serverDir, "SystemScripts") })
            {
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.txt", SearchOption.AllDirectories)) yield return f;
            }
        }

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                string filter = _txtFilter.Text.Trim();
                var stats = new Dictionary<string, VarStat>(StringComparer.OrdinalIgnoreCase);
                VarStat Get(string n) { if (!stats.TryGetValue(n, out var s)) stats[n] = s = new VarStat { Name = n.ToUpperInvariant() }; return s; }

                int files = 0;
                foreach (var f in ScriptFiles())
                {
                    if (!string.IsNullOrEmpty(filter) && Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    files++;
                    foreach (var raw in File.ReadAllLines(f))
                    {
                        var am = RxAssign.Match(raw);
                        if (am.Success) Get(am.Groups[2].Value).Assign++;
                        foreach (Match m in RxVar.Matches(raw))
                        {
                            string n = m.Groups[1].Value;
                            if (raw.Contains("$OUTPUT(" + n + ")", StringComparison.OrdinalIgnoreCase))
                                Get(n).Displays++;
                            else if (raw.Contains("%" + n, StringComparison.OrdinalIgnoreCase))
                                Get(n).Refs++;
                        }
                    }
                }

                _grid.Rows.Clear();
                foreach (var s in stats.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string advice = s.Assign == 0 ? "仅引用未见赋值（可能全局/内置）"
                        : s.Assign <= 2 ? "使用较少" : "高频使用";
                    int idx = _grid.Rows.Add(s.Name, s.Assign, s.Refs, s.Displays, advice);
                    if (s.Assign == 0) _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.Gray;
                }

                // 空闲建议：按字母段统计已用数字，列出 0-9 中未出现的
                var byLetter = stats.Keys.GroupBy(k => k[0], (k, g) => new
                {
                    Letter = char.ToUpperInvariant(k),
                    Used = g.Select(x => x[1]).Distinct().OrderBy(c => c).ToArray()
                }).OrderBy(x => x.Letter);

                var freeLines = byLetter.Select(g =>
                {
                    var free = Enumerable.Range(0, 10).Select(d => d.ToString())
                        .Except(g.Used.Select(c => c.ToString())).ToArray();
                    return $"{g.Letter}系 已用[{string.Join(",", g.Used)}] 空闲[{string.Join(",", free)}]";
                }).Take(26);

                _lblSummary.Text = $"扫描 {files} 文件 | 变量 {stats.Count} | " +
                    (freeLines.Any() ? "空闲建议: " + string.Join(" ; ", freeLines) : "无变量");
            }
            finally { Cursor = Cursors.Default; }
        }
    }
}
