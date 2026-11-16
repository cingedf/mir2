using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 工作版本管理器（P0 #5）。对标虾仁"多工作版本切换/打包"。
    /// 基于部署区 _backup-exe-&lt;日期&gt;[-n] / _baseline-* 的目录快照模式：
    /// 可创建当前 Server 目录快照、还原到快照、对比文件差异、删除快照。
    /// 全部操作前二次确认，绝不触碰运行中服务端以外的目录。
    /// </summary>
    public class WorkspaceManagerForm : Form
    {
        private readonly string _serverRoot;   // 部署区根（含 Server/ Client/ _backup-*/）
        private readonly string _serverDir;    // 当前 Server 目录
        private readonly ListView _list;
        private readonly Label _info;

        public WorkspaceManagerForm(string serverDir)
        {
            _serverDir = Path.GetFullPath(serverDir);
            _serverRoot = Path.GetDirectoryName(_serverDir) ?? _serverDir;

            Text = "工作版本管理器";
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(880, 560);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 420);

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 4, 0, 0),
                Text = "工作版本管理 — 对当前 Server 目录做快照 / 还原 / 对比\r\n" +
                       $"当前工作目录: {_serverDir}",
                TextAlign = ContentAlignment.MiddleLeft
            };

            var bar = new Panel { Dock = DockStyle.Top, Height = 38 };
            var btnSnapshot = new Button { Text = "创建快照", Location = new Point(12, 5), Width = 96 };
            var btnRestore = new Button { Text = "还原到选中", Location = new Point(118, 5), Width = 100 };
            var btnCompare = new Button { Text = "对比差异", Location = new Point(228, 5), Width = 96 };
            var btnOpen = new Button { Text = "打开目录", Location = new Point(334, 5), Width = 96 };
            var btnDelete = new Button { Text = "删除快照", Location = new Point(440, 5), Width = 96, ForeColor = Color.Firebrick };
            btnSnapshot.Click += (s, e) => CreateSnapshot();
            btnRestore.Click += (s, e) => RestoreSelected();
            btnCompare.Click += (s, e) => CompareSelected();
            btnOpen.Click += (s, e) => OpenSelected();
            btnDelete.Click += (s, e) => DeleteSelected();
            bar.Controls.AddRange(new Control[] { btnSnapshot, btnRestore, btnCompare, btnOpen, btnDelete });

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                Columns =
                {
                    new ColumnHeader { Text = "版本目录", Width = 260 },
                    new ColumnHeader { Text = "创建时间", Width = 150 },
                    new ColumnHeader { Text = "文件数", Width = 90 },
                    new ColumnHeader { Text = "大小", Width = 110 },
                    new ColumnHeader { Text = "说明", Width = 260 },
                }
            };
            _list.SelectedIndexChanged += (s, e) => UpdateInfo();
            _list.DoubleClick += (s, e) => OpenSelected();

            _info = new Label
            {
                Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 4, 0, 0),
                Text = "提示：还原操作会先对当前目录自动做一份快照，再覆盖。请确保服务端已停止。",
                ForeColor = Color.DarkSlateGray
            };

            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(header);
            Controls.Add(_info);
            RefreshList();
        }

        private IEnumerable<DirectoryInfo> SnapshotDirs()
        {
            if (!Directory.Exists(_serverRoot)) return Enumerable.Empty<DirectoryInfo>();
            return new DirectoryInfo(_serverRoot).GetDirectories()
                .Where(d => d.Name.StartsWith("_backup-") || d.Name.StartsWith("_baseline-") ||
                            d.Name.StartsWith("_snapshot-"))
                .OrderByDescending(d => d.LastWriteTime);
        }

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var d in SnapshotDirs())
            {
                int count = 0; long bytes = 0;
                try
                {
                    foreach (var f in d.GetFiles("*", SearchOption.AllDirectories))
                    { count++; bytes += f.Length; }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("统计快照目录失败 " + d.Name + ": " + ex.Message);
                }
                var item = new ListViewItem(d.Name);
                item.SubItems.Add(d.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(count.ToString());
                item.SubItems.Add(FormatSize(bytes));
                item.SubItems.Add(Describe(d.Name));
                item.Tag = d.FullName;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            UpdateInfo();
        }

        private static string Describe(string name)
        {
            if (name.StartsWith("_baseline")) return "基线版本（初始快照）";
            if (name.StartsWith("_backup")) return "历史备份";
            if (name.StartsWith("_snapshot")) return "套件创建的工作快照";
            return "";
        }

        private static string FormatSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("F1") + " KB";
            return (b / 1024.0 / 1024.0).ToString("F1") + " MB";
        }

        private string SelectedPath => _list.SelectedItems.Count > 0
            ? _list.SelectedItems[0].Tag as string : null;

        private void UpdateInfo()
        {
            string p = SelectedPath;
            _info.Text = p == null
                ? "提示：还原操作会先对当前目录自动做一份快照，再覆盖。请确保服务端已停止。"
                : "选中: " + p + "　（双击打开目录）";
        }

        private void CreateSnapshot()
        {
            string name = "_snapshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest = Path.Combine(_serverRoot, name);
            if (Directory.Exists(dest))
            {
                MessageBox.Show("快照目录已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                CopyDirectory(_serverDir, dest);
                Cursor = Cursors.Default;
                MessageBox.Show($"快照已创建:\r\n{dest}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshList();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("创建快照失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestoreSelected()
        {
            string src = SelectedPath;
            if (src == null || !Directory.Exists(src)) return;
            if (Path.GetFullPath(src) == _serverDir)
            {
                MessageBox.Show("不能还原到当前工作目录本身。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string confirm =
                "即将用快照覆盖当前 Server 目录：\r\n\r\n" +
                "来源: " + src + "\r\n目标: " + _serverDir + "\r\n\r\n" +
                "还原前会自动对当前目录做一份快照。请确认服务端已停止。\r\n是否继续？";
            if (MessageBox.Show(confirm, "还原确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                Cursor = Cursors.WaitCursor;
                // 先自动快照当前目录
                string auto = Path.Combine(_serverRoot, "_snapshot-自动备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                CopyDirectory(_serverDir, auto);
                // 覆盖：逐文件复制（不删除目标多余文件，降低风险）
                foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\', '/');
                    string dst = Path.Combine(_serverDir, rel);
                    var dir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(f, dst, true);
                }
                Cursor = Cursors.Default;
                MessageBox.Show($"还原完成。\r\n自动备份位于:\r\n{auto}\r\n\r\n请重新启动服务端以加载数据。",
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshList();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("还原失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CompareSelected()
        {
            string src = SelectedPath;
            if (src == null || !Directory.Exists(src)) return;
            var diffs = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\', '/');
                    string dst = Path.Combine(_serverDir, rel);
                    if (!File.Exists(dst)) { diffs.Add("仅快照有: " + rel); continue; }
                    var fi1 = new FileInfo(f); var fi2 = new FileInfo(dst);
                    if (fi1.Length != fi2.Length || fi1.LastWriteTime != fi2.LastWriteTime)
                        diffs.Add("不同: " + rel + $" ({FormatSize(fi1.Length)} ↔ {FormatSize(fi2.Length)})");
                }
                foreach (var f in Directory.GetFiles(_serverDir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(_serverDir.Length).TrimStart('\\', '/');
                    string s = Path.Combine(src, rel);
                    if (!File.Exists(s)) diffs.Add("仅当前有: " + rel);
                }
            }
            catch (Exception ex) { diffs.Add("对比出错: " + ex.Message); }

            var form = new Form { Text = "版本对比 — " + Path.GetFileName(src), Size = new Size(800, 560),
                StartPosition = FormStartPosition.CenterParent };
            var txt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10F),
                Text = diffs.Count == 0 ? "（无差异）" : string.Join("\r\n", diffs.Take(2000)) +
                       (diffs.Count > 2000 ? $"\r\n... 共 {diffs.Count} 项，仅显示前 2000 项" : "") };
            form.Controls.Add(txt);
            form.ShowDialog(this);
        }

        private void OpenSelected()
        {
            string p = SelectedPath;
            if (p == null || !Directory.Exists(p)) return;
            try { using var proc = Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void DeleteSelected()
        {
            string p = SelectedPath;
            if (p == null || !Directory.Exists(p)) return;
            if (Path.GetFullPath(p) == _serverDir) return;
            if (MessageBox.Show("确定删除快照目录？\r\n" + p, "删除确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Directory.Delete(p, true); RefreshList(); }
            catch (Exception ex) { MessageBox.Show("删除失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\', '/');
                string d = Path.Combine(dst, rel);
                var dir = Path.GetDirectoryName(d);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(f, d, true);
            }
        }
    }
}
