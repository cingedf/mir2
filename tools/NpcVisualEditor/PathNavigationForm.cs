using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// #6 路径导航：NPC ↔ 脚本 ↔ 地图/怪物/任务/物品 导出数据的交叉定位中心。
    /// 4 种跳转语法：&lt;文字/@段&gt;、GOTO @段、#INCLUDE [路径] @段、#INSERT &lt;路径&gt;。
    /// 对标社区工具的"路径补全/智能跳转/定义跳转"。
    /// </summary>
    public class PathNavigationForm : Form
    {
        private readonly string _serverDir;
        private readonly Envir _envir;

        private readonly TextBox _txtSearch;
        private readonly ListBox _lstNpc;
        private readonly DataGridView _gridLinks;
        private readonly TextBox _txtScript;
        private readonly Label _lblInfo;
        private List<NPCInfo> _npcs = new();

        private static readonly Regex RxJumpButton =
            new(@"<[^/>]*?/@([A-Za-z0-9_\-\(\)]+)", RegexOptions.IgnoreCase);
        private static readonly Regex RxGoto =
            new(@"^\s*(GOTO|GROUPGOTO|DELAYGOTO|TIMERECALL|TIMERECALLGROUP)\s+", RegexOptions.IgnoreCase);
        private static readonly Regex RxInclude =
            new(@"^\s*#(INCLUDE|INSERT)\s+[\[<]?([^\]>]+)[\]>]?", RegexOptions.IgnoreCase);

        public PathNavigationForm(string serverDir, Envir envir)
        {
            _serverDir = serverDir;
            _envir = envir;

            Text = "路径导航中心 — NPC / 脚本 / 地图 / 数据交叉定位";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 720);
            MinimumSize = new Size(900, 560);

            // 左：NPC 搜索 + 列表
            var left = new Panel { Dock = DockStyle.Left, Width = 320 };
            _txtSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索 NPC 名 / 文件名..." };
            _txtSearch.TextChanged += (s, e) => FillNpcs(_txtSearch.Text);
            _lstNpc = new ListBox { Dock = DockStyle.Fill };
            _lstNpc.SelectedIndexChanged += (s, e) => OnNpcSelected();
            _lstNpc.DoubleClick += (s, e) => OpenSelectedScript();
            left.Controls.Add(_lstNpc);
            left.Controls.Add(_txtSearch);

            // 右上：跳转链接表
            var rightTop = new Panel { Dock = DockStyle.Top, Height = 230 };
            var rtHeader = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Text = "  跳转链接（脚本段 / #INCLUDE / #INSERT — 双击跳转）",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _gridLinks = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _gridLinks.Columns.Add("Kind", "类型");
            _gridLinks.Columns.Add("Target", "目标");
            _gridLinks.Columns.Add("Detail", "详情");
            _gridLinks.CellDoubleClick += (s, e) => JumpLink(e.RowIndex);
            rightTop.Controls.Add(_gridLinks);
            rightTop.Controls.Add(rtHeader);

            // 右下：脚本文本预览
            var rightBottom = new Panel { Dock = DockStyle.Fill };
            var rbHeader = new Panel { Dock = DockStyle.Top, Height = 34 };
            var btnOpenScript = new Button { Text = "用编辑器打开脚本", Location = new Point(8, 4), Width = 140 };
            var btnOpenMap = new Button { Text = "查看地图行", Location = new Point(156, 4), Width = 110 };
            var btnOpenExport = new Button { Text = "打开导出数据", Location = new Point(274, 4), Width = 120 };
            btnOpenScript.Click += (s, e) => OpenSelectedScript();
            btnOpenMap.Click += (s, e) => OpenMapData();
            btnOpenExport.Click += (s, e) => OpenExportFolder();
            rbHeader.Controls.AddRange(new Control[] { btnOpenScript, btnOpenMap, btnOpenExport });
            _txtScript = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F)
            };
            _lblInfo = new Label { Dock = DockStyle.Bottom, Height = 24, Text = "  ", AutoSize = false };
            rightBottom.Controls.Add(_txtScript);
            rightBottom.Controls.Add(rbHeader);
            rightBottom.Controls.Add(_lblInfo);

            Controls.Add(rightBottom);
            Controls.Add(rightTop);
            Controls.Add(left);

            Load += (s, e) => FillNpcs(null);
        }

        private string NpcScriptPath(NPCInfo npc)
        {
            return Path.Combine(_serverDir, "Envir", "NPCs", npc.FileName + ".txt");
        }

        private void FillNpcs(string filter)
        {
            _npcs = _envir.NPCInfoList.OrderBy(d => d.Name).ToList();
            _lstNpc.BeginUpdate();
            _lstNpc.Items.Clear();
            foreach (var npc in _npcs)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    npc.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    npc.FileName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _lstNpc.Items.Add(npc);
            }
            _lstNpc.EndUpdate();
        }

        private NPCInfo SelectedNpc => _lstNpc.SelectedItem as NPCInfo;

        private void OnNpcSelected()
        {
            _gridLinks.Rows.Clear();
            _txtScript.Text = "";
            var npc = SelectedNpc;
            if (npc == null) return;

            var map = _envir.MapInfoList.FirstOrDefault(d => d.Index == npc.MapIndex);
            _lblInfo.Text = $"  NPC [{npc.Index}] {npc.Name}  脚本={npc.FileName}  地图={map?.FileName}({npc.Location.X},{npc.Location.Y})";

            string path = NpcScriptPath(npc);
            if (!File.Exists(path))
            {
                _lblInfo.Text += "  [脚本文件不存在]";
                return;
            }

            string text = File.ReadAllText(path);
            _txtScript.Text = text.Length > 20000 ? text.Substring(0, 20000) + "\r\n...(截断)" : text;

            // 段定义
            var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, @"^\s*\[@([^\]]+)\]", RegexOptions.Multiline))
                segments.Add(m.Groups[1].Value);

            // 收集跳转链接
            int lineNo = 0;
            foreach (var line in text.Split('\n'))
            {
                lineNo++;
                string t = line.Trim();
                foreach (Match m in RxJumpButton.Matches(t))
                    AddLink("段跳转", "@" + m.Groups[1].Value, $"第{lineNo}行 按钮");
                if (RxGoto.IsMatch(t))
                {
                    var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    string seg = RxGoto.Match(t).Groups[1].Value.ToUpperInvariant() switch
                    {
                        "DELAYGOTO" or "TIMERECALL" or "TIMERECALLGROUP" => parts.Length > 2 ? parts[2] : "",
                        _ => parts.Length > 1 ? parts[1] : ""
                    };
                    seg = seg.TrimStart('@');
                    if (seg.Length > 0)
                        AddLink(segments.Contains(seg) ? "段跳转" : "段引用?", "@" + seg, $"第{lineNo}行 {parts[0]}");
                }
                var inc = RxInclude.Match(t);
                if (inc.Success)
                    AddLink(inc.Groups[1].Value.ToUpperInvariant(), inc.Groups[2].Value.Trim(), $"第{lineNo}行 外部文件");
            }
        }

        private void AddLink(string kind, string target, string detail)
        {
            _gridLinks.Rows.Add(kind, target, detail);
        }

        private void JumpLink(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _gridLinks.Rows.Count) return;
            var row = _gridLinks.Rows[rowIndex];
            string target = row.Cells[1].Value?.ToString() ?? "";
            string kind = row.Cells[0].Value?.ToString() ?? "";

            if (target.StartsWith("@"))
            {
                // 定位到脚本中的 [@段] 行
                string seg = target.TrimStart('@');
                string text = _txtScript.Text;
                int idx = text.IndexOf("[ @" + seg, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) idx = text.IndexOf("[@" + seg, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    _txtScript.SelectionStart = idx;
                    _txtScript.SelectionLength = Math.Min(40, text.Length - idx);
                    _txtScript.ScrollToCaret();
                    _txtScript.Focus();
                }
                else MessageBox.Show($"未在当前脚本找到段 [@ {seg}]（可能在外部脚本）。", "提示");
            }
            else if (kind == "INCLUDE" || kind == "INSERT")
            {
                string full = Path.Combine(_serverDir, "Envir", target.Replace('/', '\\').TrimStart('\\'));
                if (File.Exists(full)) OpenText(full);
                else MessageBox.Show($"外部文件不存在:\n{full}", "提示");
            }
        }

        private void OpenSelectedScript()
        {
            var npc = SelectedNpc;
            if (npc == null) return;
            string path = NpcScriptPath(npc);
            if (File.Exists(path)) OpenText(path);
            else MessageBox.Show($"脚本文件不存在:\n{path}", "提示");
        }

        private void OpenMapData()
        {
            var npc = SelectedNpc;
            if (npc == null) return;
            string map = _envir.MapInfoList.FirstOrDefault(d => d.Index == npc.MapIndex)?.FileName ?? "";
            string path = Path.Combine(_serverDir, "Exports", "1_地图数据.txt");
            if (!File.Exists(path)) { MessageBox.Show("未找到 1_地图数据.txt", "提示"); return; }
            // 用临时文件高亮地图行（不改动原导出文件）
            var lines = File.ReadAllLines(path);
            var hit = lines.FirstOrDefault(l => l.IndexOf(map, StringComparison.OrdinalIgnoreCase) >= 0);
            string tmp = Path.Combine(Path.GetTempPath(), "maploc_" + map + ".txt");
            File.WriteAllText(tmp, $"; 地图 {map} 定位（原文件: {path}）\r\n" +
                (hit ?? "; 未找到匹配行") + "\r\n\r\n" + string.Join("\r\n", lines.Take(200)));
            OpenText(tmp);
        }

        private void OpenExportFolder()
        {
            string dir = Path.Combine(_serverDir, "Exports");
            if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            else MessageBox.Show("未找到 Exports 目录", "提示");
        }

        private static void OpenText(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("打开失败: " + ex.Message, "错误"); }
        }
    }
}
