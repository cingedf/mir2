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
    /// 工具入口中心：复用现成工具（地图编辑器/图库编辑器/爆率编辑器/引擎内置编辑器等）。
    /// 不重造轮子——一键启动并把当前服务端/客户端目录作为参数传入。
    /// </summary>
    public class ToolHubForm : UserControl
    {
        private readonly string _serverDir, _clientDir;
        private readonly ListView _list;
        private readonly TextBox _detail;
        private readonly List<ToolEntry> _tools = new();

        private class ToolEntry
        {
            public string Name, Category, Description, ExePath, Args;
            public bool NeedsServer, NeedsClient;
        }

        public ToolHubForm(string serverDir, string clientDir)
        {
            _serverDir = serverDir;
            _clientDir = clientDir;

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 42,
                Text = "工具入口中心 — 一键启动现成工具（复用，不重复造车）\r\n" +
                       "所有工具以当前服务端目录为工作目录启动；关闭本窗口不影响工具运行。",
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 2, 0, 0)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
            var btnRun = new Button { Text = "启动工具", Location = new Point(10, 4), Width = 100 };
            var btnRefresh = new Button { Text = "刷新状态", Location = new Point(120, 4), Width = 90 };
            btnRun.Click += (s, e) => RunSelected();
            btnRefresh.Click += (s, e) => RefreshStatus();
            toolbar.Controls.AddRange(new Control[] { btnRun, btnRefresh });

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                Columns =
                {
                    new ColumnHeader { Text = "工具", Width = 190 },
                    new ColumnHeader { Text = "分类", Width = 90 },
                    new ColumnHeader { Text = "用途", Width = 320 },
                    new ColumnHeader { Text = "状态", Width = 80 },
                }
            };
            _list.SelectedIndexChanged += (s, e) => UpdateDetail();
            _list.DoubleClick += (s, e) => RunSelected();

            _detail = new TextBox
            {
                Dock = DockStyle.Bottom, Height = 80, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Text = "选择工具查看说明。"
            };

            BuildToolEntries();
            Controls.Add(_list);
            Controls.Add(toolbar);
            Controls.Add(header);
            Controls.Add(_detail);
            RefreshStatus();
        }

        private void BuildToolEntries()
        {
            // 部署区现成工具（绝对路径，存在即显示）
            Add("客户端地图编辑器", "客户端资源", "可视化编辑地图、批量导出小地图 PNG",
                Path.Combine(_clientDir, "Map Editor.exe"), "", false, true);
            Add("源码地图编辑器 (MapEditor)", "客户端资源", "地图瓦片/小地图导出（源码构建版）",
                @"C:\1.40\mir2-angelk727\tools\MapEditor\Map Editor\bin\x64\Release\net8.0-windows\Map Editor.exe", "", false, true);
            Add("图库编辑器 (LibraryEditor)", "客户端资源", "WIL/WIX/WZL/MIX/LIB 帧浏览与导出",
                @"C:\1.40\mir2-angelk727\LibraryEditor\bin\x86\Release\net8.0-windows\LibraryEditor.exe", "", false, false);
            Add("爆率编辑器 (DropEditor)", "服务端数据", "怪物爆率 DropPath 可视化编辑",
                Path.Combine(_serverDir, "Envir", "DropEditor.exe"), "", true, false);

            // 引擎内置编辑器（Server.exe 菜单驱动；13 个 Database 表单 + VForm 可视化地图 + DropBuilder）
            Add("引擎内置编辑器", "服务端数据",
                "Server.exe 启动后从菜单进入：物品/怪物/NPC/任务/地图/技能/商城/配方/ buff/爆率/攻城/龙 etc（12+ 表单）",
                Path.Combine(_serverDir, "Server.exe"), "", true, false);
            Add("商城迁移工具", "服务端数据", "批量铺货架/迁移商城数据",
                @"C:\1.40\mir2-angelk727\tools\GameShopMigrate\bin\Release\net8.0\GameShopMigrate.exe", "", true, false);
            Add("刷怪点种子工具", "服务端数据", "批量生成刷怪点",
                @"C:\1.40\mir2-angelk727\tools\MonsterSpawnSeed\bin\Release\net8.0\MonsterSpawnSeed.exe", "", true, false);
            Add("物品种子工具", "服务端数据", "批量生成物品掉落",
                @"C:\1.40\mir2-angelk727\tools\ItemSpawnSeed\bin\Release\net8.0\ItemSpawnSeed.exe", "", true, false);
            Add("NPC Flag 修复", "服务端数据", "地图 Flag 批量修复",
                @"C:\1.40\mir2-angelk727\tools\NPCFlagFix\bin\Release\net8.0\NPCFlagFix.exe", "", true, false);
            Add("重生点修复", "服务端数据", "刷怪重生点批量修复",
                @"C:\1.40\mir2-angelk727\tools\RespawnFix\bin\Release\net8.0\RespawnFix.exe", "", true, false);
        }

        private void Add(string name, string cat, string desc, string exe, string args = "",
            bool needsServer = false, bool needsClient = false)
        {
            _tools.Add(new ToolEntry
            {
                Name = name, Category = cat, Description = desc,
                ExePath = exe, Args = args, NeedsServer = needsServer, NeedsClient = needsClient
            });
        }

        private void RefreshStatus()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var t in _tools)
            {
                var item = new ListViewItem(t.Name);
                item.SubItems.Add(t.Category);
                item.SubItems.Add(t.Description);
                item.SubItems.Add(File.Exists(t.ExePath) ? "可用" : "未找到");
                item.Tag = t;
                item.ForeColor = File.Exists(t.ExePath) ? SystemColors.WindowText : Color.Gray;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            UpdateDetail();
        }

        private void UpdateDetail()
        {
            if (_list.SelectedItems.Count == 0) return;
            var t = (ToolEntry)_list.SelectedItems[0].Tag;
            _detail.Text = $"名称: {t.Name}\r\n分类: {t.Category}\r\n路径: {t.ExePath}\r\n" +
                           $"参数: {t.Args}\r\n说明: {t.Description}\r\n" +
                           (File.Exists(t.ExePath) ? "状态: 可用" : "状态: 未找到（可检查源码构建输出目录）");
        }

        private void RunSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var t = (ToolEntry)_list.SelectedItems[0].Tag;
            if (!File.Exists(t.ExePath))
            {
                MessageBox.Show($"未找到工具：\r\n{t.ExePath}\r\n请检查构建输出目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (t.NeedsServer && !Directory.Exists(_serverDir))
            {
                MessageBox.Show("服务端目录不存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var psi = new ProcessStartInfo(t.ExePath)
                {
                    Arguments = t.Args,
                    WorkingDirectory = t.NeedsServer ? _serverDir : (_clientDir ?? ""),
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
