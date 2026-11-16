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
        // 完整脚本文本与当前显示窗口偏移：预览按 20000 字符截断，
        // 段跳转必须用完整文本定位，超出显示窗口时重设窗口，避免"截断后搜索失败"。
        private string _fullScript = "";
        private int _scriptOffset = 0;

        private static readonly Regex RxJumpButton =
            new(@"<[^/>]*?/@([A-Za-z0-9_\-\(\)]+)", RegexOptions.IgnoreCase);
        private static readonly Regex RxGoto =
            new(@"^\s*(GOTO|GROUPGOTO|DELAYGOTO|TIMERECALL|TIMERECALLGROUP)\s+", RegexOptions.IgnoreCase);
        // 捕获：1=INCLUDE/INSERT，2=路径，3=可选的 "] @段名"
        private static readonly Regex RxInclude =
            new(@"^\s*#(INCLUDE|INSERT)\s+[\[<]?([^\]>\s]+)[\]>]?(?:\s+@([^\s]+))?", RegexOptions.IgnoreCase);

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
            _gridLinks.Columns["Kind"].FillWeight = 60;
            _gridLinks.Columns["Target"].FillWeight = 180;
            _gridLinks.Columns["Detail"].FillWeight = 420; // 详情最长（第N行 命令），给足权重防截断
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
                ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F),
                WordWrap = false // 等宽脚本关折行，保持列对齐
            };
            _lblInfo = new Label
            {
                Dock = DockStyle.Bottom, Height = 24, Text = "  ",
                AutoSize = false, AutoEllipsis = true, Padding = new Padding(4, 0, 4, 0)
            };
            var infoTip = new ToolTip();
            infoTip.SetToolTip(_lblInfo, "悬停查看完整信息");
            _lblInfo.MouseHover += (s, e) =>
                infoTip.SetToolTip(_lblInfo, string.IsNullOrEmpty(_lblInfo.Text) ? "无信息" : _lblInfo.Text);
            rightBottom.Controls.Add(_txtScript);
            rightBottom.Controls.Add(_lblInfo);
            rightBottom.Controls.Add(new Splitter { Dock = DockStyle.Bottom, Height = 4 });
            rightBottom.Controls.Add(rbHeader);

            Controls.Add(rightBottom);
            Controls.Add(new Splitter { Dock = DockStyle.Top, Height = 4 }); // 上下列表/预览可调
            Controls.Add(rightTop);
            Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4, MinSize = 180 }); // 左右栏可调
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
            _fullScript = "";
            _scriptOffset = 0;
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

            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { _lblInfo.Text += "  [读取失败: " + ex.Message + "]"; return; }
            _fullScript = text;
            _scriptOffset = 0;
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
                {
                    // 段名随链接保存，双击外部文件时一并定位到 @段
                    string seg3 = inc.Groups[3].Success ? inc.Groups[3].Value.Trim() : "";
                    AddLink(inc.Groups[1].Value.ToUpperInvariant(), inc.Groups[2].Value.Trim(),
                        $"第{lineNo}行 外部文件" + (seg3.Length > 0 ? " @" + seg3 : ""));
                }
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
            string detail = row.Cells[2].Value?.ToString() ?? "";

            if (target.StartsWith("@"))
            {
                // 定位到脚本中的 [@段] 行——必须用完整文本，且段名精确匹配（避免 @main 命中 @main-1）
                string seg = target.TrimStart('@');
                var segRx = new Regex(@"^\s*\[@" + Regex.Escape(seg) + @"\]", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var m = segRx.Match(_fullScript);
                if (!m.Success)
                {
                    MessageBox.Show($"未在当前脚本找到段 [@{seg}]（可能在外部脚本）。", "提示");
                    return;
                }
                int idx = m.Index;
                // 段落在显示窗口之外：重设窗口到段附近，再定位
                int winLen = 20000;
                if (idx < _scriptOffset || idx >= _scriptOffset + winLen)
                {
                    _scriptOffset = Math.Max(0, idx - 5000);
                    int take = Math.Min(winLen, _fullScript.Length - _scriptOffset);
                    _txtScript.Text = _fullScript.Substring(_scriptOffset, take) +
                                      (_scriptOffset + take < _fullScript.Length ? "\r\n...(截断)" : "");
                }
                int viewIdx = Math.Max(0, idx - _scriptOffset);
                _txtScript.SelectionStart = viewIdx;
                _txtScript.SelectionLength = Math.Min(40, _txtScript.Text.Length - viewIdx);
                _txtScript.ScrollToCaret();
                _txtScript.Focus();
            }
            else if (kind == "INCLUDE" || kind == "INSERT")
            {
                string full = Path.Combine(_serverDir, "Envir", target.Replace('/', '\\').TrimStart('\\'));
                if (!File.Exists(full)) { MessageBox.Show($"外部文件不存在:\n{full}", "提示"); return; }
                // 详情形如 "第N行 外部文件 @段名"，提取段名生成带定位标记的临时文件
                string seg = "";
                var dm = Regex.Match(detail, @"@(\S+)\s*$");
                if (dm.Success) seg = dm.Groups[1].Value;
                if (seg.Length == 0) { OpenText(full); return; }
                try
                {
                    var extLines = File.ReadAllLines(full);
                    int targetLine = Array.FindIndex(extLines,
                        l => Regex.IsMatch(l, @"^\s*\[@" + Regex.Escape(seg) + @"\]", RegexOptions.IgnoreCase));
                    if (targetLine < 0) { OpenText(full); return; }
                    var marked = extLines.Select((l, i) => i == targetLine ? "; >>> 跳转目标 @" + seg + " <<<\r\n" + l : l);
                    string tmp = Path.Combine(Path.GetTempPath(), "incloc_" + Path.GetFileName(full));
                    File.WriteAllText(tmp, string.Join("\r\n", marked));
                    OpenText(tmp);
                }
                catch (Exception ex) { MessageBox.Show("打开外部文件失败: " + ex.Message, "错误"); }
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
            // 精确匹配地图段头 "^\s*\[<地图号>\s"——原用子串 IndexOf，
            // map="1" 会命中首行 MINIMAP(1)，map="" 会命中首行（IndexOf("")==0），均定位错误。
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex) { MessageBox.Show("读取地图数据失败: " + ex.Message, "错误"); return; }
            string hit = null;
            if (!string.IsNullOrEmpty(map))
            {
                var headRx = new Regex(@"^\s*\[\s*" + Regex.Escape(map) + @"\s", RegexOptions.IgnoreCase);
                hit = lines.FirstOrDefault(l => headRx.IsMatch(l));
            }
            string tmp = Path.Combine(Path.GetTempPath(), "maploc_" + (string.IsNullOrEmpty(map) ? "none" : map) + ".txt");
            try
            {
                File.WriteAllText(tmp, $"; 地图 {map} 定位（原文件: {path}）\r\n" +
                    (hit ?? "; 未找到匹配行") + "\r\n\r\n" + string.Join("\r\n", lines.Take(200)));
            }
            catch (Exception ex) { MessageBox.Show("生成定位文件失败: " + ex.Message, "错误"); return; }
            OpenText(tmp);
        }

        private void OpenExportFolder()
        {
            string dir = Path.Combine(_serverDir, "Exports");
            if (Directory.Exists(dir)) OpenText(dir); // 复用带 try/catch 的打开封装
            else MessageBox.Show("未找到 Exports 目录", "提示");
        }

        private static void OpenText(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("打开失败: " + ex.Message, "错误"); }
        }
    }
}
