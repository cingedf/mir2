using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 反向索引主窗体（规范 §10）。
    /// 打开 lib → 自动扫描源码 → 以"人话 + 可编辑帧号"列出引用 → 选帧/改帧号 →
    /// 保存（按载体写源码/lib 帧表 + 编译 + 换件）→ 状态矩阵展示结果。
    /// 全程不打开代码编辑器。
    /// </summary>
    public sealed class ReverseIndexForm : Form
    {
        private readonly ReverseIndexConfig _cfg;
        private readonly LibraryMap _map;
        private readonly AliasManager _aliasMgr;
        private string _libPath;
        private MLibraryV2 _lib;
        private LibraryResolveResult _resolved;
        private List<Reference> _refs = new List<Reference>();
        // T3.2: Indirect 歧义库手工指定编号
        private Reference _ambigRef;
        private bool _suppressComboEvent;
        private bool _busy; // T6：保存/编译/部署进行中标志，防重复点击（重复编译/重复换件）
        private string _lastBackupSession; // P0-2：最近一次保存的备份会话目录（「回到本次备份」用）

        // T5 直观性：筛选/操作指引/悬浮说明/实时状态（零基础可用）
        private readonly TextBox _txtFilter = new TextBox();
        private readonly CheckBox _chkChangedOnly = new CheckBox();
        private readonly Label _lblHint = new Label();
        private readonly ToolTip _tips = new ToolTip();
        private List<Reference> _filtered = new List<Reference>();

        // UI 控件
        private readonly Label _lblLib = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Panel _frameBand = new Panel();
        private readonly TextBox _txtResult = new TextBox();
        private readonly Button _btnRescan = new Button();
        private readonly Button _btnSave = new Button();
        private readonly Button _btnCompile = new Button();
        private readonly Button _btnDeploy = new Button();
        private readonly Button _btnDiff = new Button();
        private readonly Button _btnUndo = new Button();
        private readonly Button _btnAlias = new Button();
        private readonly Button _btnMapSetup = new Button();
        private readonly Button _btnRestore = new Button(); // P0-2：回到本次备份
        private readonly ComboBox _cmboLib = new ComboBox();
        private readonly Label _lblStatus = new Label();

        private const string COL_LABEL = "人话说明";
        private const string COL_EXPR = "原表达式";
        private const string COL_VALUE = "当前值";
        private const string COL_GROUP = "组帧范围"; // T6：一组图从第几帧到第几帧、共几张
        private const string COL_SRC = "来源";
        private const string COL_KIND = "类型";
        private const string COL_TARGET = "载体";
        private const string COL_FLAG = "标记";

        public ReverseIndexForm()
        {
            _cfg = ReverseIndexConfig.Instance;
            _map = new LibraryMap();
            _map.Load(_cfg);
            _aliasMgr = new AliasManager(_cfg);
            // T3.3: 应用持久化映射覆盖（原变量→覆盖变量）。
            ApplyMapOverrides();
            BuildUi();
        }

        // T3.3: 把用户配置的映射覆盖应用到内存映射表（不修改源码常量）。
        // key 一律用解析原始名 SourceVariable：覆盖生效会改写 Variable，若按 Variable 查，
        // 下次打开弹窗/还原时就会找不到已生效的覆盖（旧缺陷：覆盖隐身、还原写错 key）。
        // 数组库只换基名保留 [] 结构；用户粘贴带 [编号] 的完整形式时自动剥掉。
        private void ApplyMapOverrides()
        {
            if (_aliasMgr.MapOverrides == null || _aliasMgr.MapOverrides.Count == 0) return;
            foreach (var e in _map.Entries)
            {
                string key = "Libraries." + e.SourceVariable;
                string target;
                if (!_aliasMgr.MapOverrides.TryGetValue(key, out target) || string.IsNullOrEmpty(target)) continue;
                string name = target.StartsWith("Libraries.") ? target.Substring("Libraries.".Length) : target;
                int bracket = name.IndexOf('[');
                if (bracket >= 0) name = name.Substring(0, bracket).Trim(); // 数组结构保护
                if (!string.IsNullOrEmpty(name)) e.Variable = name;
            }
        }

        private void BuildUi()
        {
            this.Text = "源码帧号反向索引";
            this.StartPosition = FormStartPosition.CenterParent;
            this.WindowState = FormWindowState.Maximized;
            this.MinimumSize = new Size(980, 640);

            // 顶部：库信息
            _lblLib.Dock = DockStyle.Top;
            _lblLib.Height = 30;
            _lblLib.Padding = new Padding(8, 6, 8, 0);
            _lblLib.Text = "未打开 lib";

            // 映射下拉（歧义库手工指定编号用）
            _cmboLib.Dock = DockStyle.Top;
            _cmboLib.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmboLib.Width = 320;
            _cmboLib.Visible = false;
            _cmboLib.SelectedIndexChanged += ComboLib_SelectedIndexChanged; // T3.2

            // 帧带时间轴 + 图例（T6 第2问：中间彩色指示是什么——直接把颜色含义画在帧带旁，
            // 与 FrameBand_Paint 的四色完全一致，零基础用户不用猜色块含义）。
            var bandHost = new Panel { Dock = DockStyle.Top, Height = 74, AutoScroll = true, Padding = new Padding(8, 4, 8, 4) };
            _frameBand.Dock = DockStyle.Fill;
            _frameBand.Paint += FrameBand_Paint;
            _frameBand.MouseClick += FrameBand_MouseClick;
            var legend = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 24,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(2, 2, 2, 0),
                BackColor = Color.White
            };
            var legendItems = new[] {
                Tuple.Create("橙＝玩家全局帧表", Color.DarkOrange),
                Tuple.Create("蓝＝表达式基址(动态)", Color.RoyalBlue),
                Tuple.Create("紫＝间接引用", Color.MediumPurple),
                Tuple.Create("绿＝常量帧号", Color.SeaGreen),
            };
            foreach (var kv in legendItems)
            {
                var swatch = new Label { AutoSize = false, Width = 12, Height = 12, BackColor = kv.Item2, Margin = new Padding(2, 6, 2, 0) };
                var chip = new Label { AutoSize = true, Text = kv.Item1, Margin = new Padding(2, 3, 12, 0) };
                legend.Controls.Add(swatch);
                legend.Controls.Add(chip);
            }
            bandHost.Controls.Add(_frameBand);
            bandHost.Controls.Add(legend); // 后挂靠边：legend 视觉在上，帧带 Fill 在下

            // 引用列表
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.SelectionChanged += Grid_SelectionChanged; // T3.2: 选中歧义库行时显示编号下拉
            _grid.DataError += (s, e) => { e.ThrowException = false; };

            // 底部结果/状态区
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190 };
            _txtResult.Multiline = true;
            _txtResult.ReadOnly = true;
            _txtResult.ScrollBars = ScrollBars.Both;
            _txtResult.Dock = DockStyle.Fill;
            _txtResult.Font = new Font("Consolas", 9f);
            bottom.Controls.Add(_txtResult);
            _lblStatus.Dock = DockStyle.Bottom;
            _lblStatus.Height = 24;
            _lblStatus.Padding = new Padding(8, 4, 8, 0);
            bottom.Controls.Add(_lblStatus);

            // 按钮栏
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _btnRescan.Text = "扫描/刷新";
            _btnSave.Text = "保存改动";
            _btnCompile.Text = "重编译";
            _btnDeploy.Text = "部署";
            _btnDiff.Text = "查看差异";
            _btnUndo.Text = "撤销";
            _btnAlias.Text = "别名管理";       // T3.3
            _btnMapSetup.Text = "映射设置";    // T3.3
            _btnRestore.Text = "回到备份（无）"; // P0-2
            _btnRestore.Enabled = false;
            _btnRescan.Click += (s, e) => Rescan();
            _btnSave.Click += (s, e) => SaveAll();
            _btnCompile.Click += (s, e) => CompileOnly();
            _btnDeploy.Click += (s, e) => DeployOnly();
            _btnDiff.Click += (s, e) => ShowDiff();
            _btnUndo.Click += (s, e) => Undo();
            _btnAlias.Click += (s, e) => ManageAliases();   // T3.3
            _btnMapSetup.Click += (s, e) => ManageMap();     // T3.3
            _btnRestore.Click += (s, e) => RestoreLastBackup(); // P0-2
            bar.Controls.Add(_btnRescan);
            bar.Controls.Add(_btnSave);
            bar.Controls.Add(_btnCompile);
            bar.Controls.Add(_btnDeploy);
            bar.Controls.Add(_btnDiff);
            bar.Controls.Add(_btnUndo);
            bar.Controls.Add(_btnAlias);        // T3.3
            bar.Controls.Add(_btnMapSetup);     // T3.3
            bar.Controls.Add(_btnRestore);      // P0-2
            // T5 布局：停靠控件统一移到 BuildUi 末尾按序挂载（见下方“T5 布局”块）

            // T5 直观性：筛选栏（搜索 + 只看已改动 + 一句话指引），零基础也能找到条目
            var filterBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 4) };
            _txtFilter.Width = 280;
            _txtFilter.PlaceholderText = "筛选：输入名字/表达式/帧号…";
            // P2-14：输入节流——每字符重建整表在 400+ 行时卡顿，停顿 300ms 后才真正筛选
            var filterTimer = new System.Windows.Forms.Timer { Interval = 300 };
            filterTimer.Tick += (s, e) => { filterTimer.Stop(); ApplyFilter(); };
            this.FormClosed += (s, e) => filterTimer.Dispose(); // 防计时器泄漏
            _txtFilter.TextChanged += (s, e) => { filterTimer.Stop(); filterTimer.Start(); };
            _chkChangedOnly.Text = "只看已改动";
            _chkChangedOnly.AutoSize = true;
            _chkChangedOnly.Padding = new Padding(8, 6, 0, 0);
            _chkChangedOnly.CheckedChanged += (s, e) => ApplyFilter();
            _lblHint.Text = "双击「当前值」格＝弹出选图；改完点「保存改动」自动写回源码并部署。";
            _lblHint.ForeColor = Color.DimGray;
            _lblHint.AutoSize = true;
            _lblHint.Padding = new Padding(12, 8, 0, 0);
            filterBar.Controls.Add(_txtFilter);
            filterBar.Controls.Add(_chkChangedOnly);
            filterBar.Controls.Add(_lblHint);

            // T5 直观性：悬浮说明（鼠标停 1 秒即见，不占界面空间）
            _tips.SetToolTip(_btnRescan, "重新扫描源码和当前 lib，刷新列表（不会改任何文件）。");
            _tips.SetToolTip(_btnSave, "把表中改过的帧号写回源码文件，自动编译并部署到游戏目录（部署前需关闭游戏客户端）。");
            _tips.SetToolTip(_btnCompile, "只重新编译客户端验证能否通过，不改源码、不部署。");
            _tips.SetToolTip(_btnDeploy, "把编译好的 7 个文件复制到 mir2-deploy\\Client（需先关闭游戏客户端）。");
            _tips.SetToolTip(_btnDiff, "预览本次改动会怎么改源码，不实际写入。");
            _tips.SetToolTip(_btnUndo, "放弃本界面未保存的修改，恢复为打开时的值（已保存到磁盘的不受影响）。");
            _tips.SetToolTip(_btnAlias, "给看不懂的条目起中文名，保存后列表里直接显示这个名字。");
            _tips.SetToolTip(_btnMapSetup, "当 lib 文件对应不上源码变量（反查失败）时，在这里手工指定对应关系。");
            _tips.SetToolTip(_btnRestore, "把最近一次「保存改动」之前的源码/lib/部署文件还原（保存失败、改错了想反悔时用）。");
            _tips.SetToolTip(_grid, "双击行＝弹出缩略图选帧；也可以直接在「当前值」格里输入数字。改过的行会变黄。");
            _tips.SetToolTip(bandHost, "帧号时间轴：每根竖线＝一个帧号引用，点击竖线跳到对应行；颜色含义见上方图例（橙色=玩家全局帧表，改前会二次确认）。");
            _tips.SetToolTip(_txtFilter, "输入名字、表达式或帧号片段，列表即时筛选；清空恢复全部。");
            _tips.SetToolTip(_chkChangedOnly, "只显示改过的行；想快速核对本次改动时用。");

            // T5 便利性：关窗保护——有未保存改动时先确认，防止点 X 静默丢失。
            this.FormClosing += ReverseIndexForm_FormClosing;

            // T5 布局（已用探针实测：多个 Dock=Top 时"后挂的靠边"）。
            // 挂载顺序：Fill 网格最先 → Top 区倒序挂（视觉从上到下 = 库信息→映射下拉→按钮栏→筛选栏→帧带）→ Bottom 状态区贴底。
            this.Controls.Add(_grid);       // Fill，占剩余全部空间
            this.Controls.Add(bandHost);    // Top，视觉最下的 Top 区
            this.Controls.Add(filterBar);   // Top，筛选栏
            this.Controls.Add(bar);         // Top，按钮栏
            this.Controls.Add(_cmboLib);    // Top（平时隐藏，歧义库选中行才显示）
            this.Controls.Add(_lblLib);     // Top 最后挂 = 视觉最上
            this.Controls.Add(bottom);      // Bottom，底部状态区
        }

        /// <summary>由 LMain 打开 lib 时调用，自动反查并刷新。</summary>
        public void OpenLibrary(string libPath, MLibraryV2 lib)
        {
            // P1-8：切库保护——旧库还有未保存改动时先确认，防止静默丢失。
            // （保存走 SaveAll 全链路；取消则不切库，用户可先处理完再换。）
            if (_refs.Any(r => r.Changed))
            {
                var c = MessageBox.Show(
                    "当前库还有 " + _refs.Count(r => r.Changed) + " 处未保存的改动。\n\n"
                    + "「是」＝先保存再切换（写回源码并编译部署）\n"
                    + "「否」＝放弃这些改动，直接切换\n"
                    + "「取消」＝留在当前库",
                    "切换库确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (c == DialogResult.Cancel) return;
                if (c == DialogResult.Yes) { SaveAll(); if (_busy) return; }
                else foreach (var r in _refs) r.EditedValue = r.BaseValue;
            }
            _libPath = libPath;
            _lib = lib;
            Rescan();
        }

        private void Rescan()
        {
            if (string.IsNullOrEmpty(_libPath) || !File.Exists(_libPath))
            {
                _lblLib.Text = "未打开有效的 lib 文件";
                return;
            }

            // T5 便利性：重扫会重建全部 Reference 对象，先按稳定 Id 记住未保存的编辑，
            // 防止用户改完顺手点「扫描/刷新」时改动被静默清掉。
            var pending = new Dictionary<string, int[]>();
            foreach (var r in _refs)
                if (r.Changed) pending[r.Id] = new[] { r.EditedValue, r.BaseValue };

            _resolved = _map.Resolve(_libPath, _cfg);
            string varText = _resolved == null ? "（未识别到源码变量，将列出全部引用；可点「映射设置」手工指定）" : _resolved.VariableText;
            _lblLib.Text = string.Format("库：{0}   变量：{1}   帧数：{2}", _libPath, varText, _lib == null ? 0 : _lib.Count);

            List<string> errors;
            // T3.3: 传入持久化别名，让用户自定义的人话标签在重扫后仍生效。
            var scanner = new ReferenceScanner(_cfg, new LabelGenerator(_aliasMgr.Aliases));
            var all = scanner.ScanAll(out errors);
            _refs = _resolved == null ? all : ReferenceScanner.ForLibrary(all, _resolved);

            // P0-1：lib 自带帧表（v3 的 Frames 段）生成 LibFrames 引用。
            // 必须放在 pending 恢复之前：libframe 的 Id 稳定（libPath|动作|参数序），
            // 先追加才能被下面的恢复循环覆盖到，否则重扫会静默丢失 lib 帧表的未保存编辑。
            try { AppendLibFrameRefs(); } catch (Exception exLib) { AppendResult("[lib帧表] 生成失败: " + exLib.Message); }

            AppendResult("扫描完成：全部引用 " + all.Count + " 条，本库相关 " + _refs.Count + " 条，错误 " + errors.Count + " 条");
            foreach (var e in errors) AppendResult("  [扫描错误] " + e);

            // T5：恢复未保存改动——仅当磁盘上该 token 仍是旧值（BaseValue 未变）才恢复；
            // 源码确实被外部改过的不恢复，按磁盘新值显示。
            if (pending.Count > 0)
            {
                int restored = 0, stale = 0;
                foreach (var r in _refs)
                {
                    int[] pe;
                    if (!pending.TryGetValue(r.Id, out pe)) continue;
                    if (r.BaseValue == pe[1]) { r.EditedValue = pe[0]; restored++; }
                    else stale++;
                }
                if (restored > 0) AppendResult("[保护] 已恢复 " + restored + " 条未保存改动（黄色行），保存前不会丢失");
                if (stale > 0) AppendResult("[提示] 另有 " + stale + " 条源码已被外部修改，按磁盘新值显示，原编辑不再适用");
            }

            BindGrid();
            _frameBand.Invalidate();
        }

        // P0-1：为 lib 自带 Frames 段的每个 (MirAction, 8 参数) 生成 LibFrames 引用。
        // 这些引用不来自源码：Target="LibFrames"，TokenStart 无效（-1），保存时直接改
        // _lib.Frames[action] 属性再由 LibFramesWriter 原子写回 lib 文件。
        private void AppendLibFrameRefs()
        {
            if (_lib == null || _lib.Frames == null || _lib.Frames.Keys.Count == 0) return;
            int libCount = _lib.Count;
            var libVar = _resolved != null ? _resolved.VariableText : System.IO.Path.GetFileNameWithoutExtension(_libPath);

            // 稳定 Id：libPath|动作名|参数序号（lib 路径天然唯一，无同表达式多语句问题）
            foreach (var kv in _lib.Frames)
            {
                var action = kv.Key;
                var f = kv.Value;
                if (f == null) continue;
                string actionName = action.ToString();
                int[] vals = { f.Start, f.Count, f.Skip, f.Interval, f.EffectStart, f.EffectCount, f.EffectSkip, f.EffectInterval };
                FrameArgKind[] kinds = { FrameArgKind.FrameStart, FrameArgKind.Count, FrameArgKind.Skip,
                    FrameArgKind.Interval, FrameArgKind.EffectStart, FrameArgKind.EffectCount,
                    FrameArgKind.EffectSkip, FrameArgKind.EffectInterval };
                string groupId = "libframe|" + _libPath + "|" + actionName;
                for (int i = 0; i < vals.Length; i++)
                {
                    var r = new Reference
                    {
                        Id = "libframe|" + _libPath + "|" + actionName + "|#" + i,
                        LibVariable = libVar,
                        FilePath = "[lib自带帧表] " + actionName,
                        Line = 0,
                        Column = 0,
                        TokenStart = -1,
                        TokenLength = 0,
                        TokenOrdinal = i,
                        Expression = actionName + " (start=" + vals[0] + ", count=" + vals[1] + ", skip=" + vals[2] + ")",
                        NormalizedExpr = Reference.Normalize(actionName),
                        BaseValue = vals[i],
                        EditedValue = vals[i],
                        DynamicPart = "",
                        Kind = RefKind.LibFrames,
                        ArgKind = kinds[i],
                        Target = "LibFrames",
                        LibFrameAction = actionName,
                        LibFrameActionValue = (int)action,
                        HumanLabel = "帧表-" + actionName + "-" + ArgKindText(kinds[i]),
                        LabelSource = "lib帧表",
                        GroupId = groupId,
                        GroupStart = vals[0],
                        GroupCount = vals[1],
                        GroupSkip = vals[2],
                    };
                    // 帧号参数超界（≥ lib 帧数）标记为不可编辑，防止写回坏帧表
                    if (kinds[i] == FrameArgKind.FrameStart || kinds[i] == FrameArgKind.EffectStart)
                        r.IsFrameNumberLibCheck = vals[i] >= 0 && vals[i] < libCount;
                    _refs.Add(r);
                }
            }
            if (_lib.Frames.Keys.Count > 0)
                AppendResult("[lib帧表] 本库自带帧表 " + _lib.Frames.Keys.Count + " 个动作，已加入列表（改后保存自动写回 lib）。");
        }

        // P0-1：帧参数角色的中文名（与 KindText 的 arg 一致，供 lib 帧表标签用）
        private static string ArgKindText(FrameArgKind k)
        {
            switch (k)
            {
                case FrameArgKind.FrameStart: return "起始帧";
                case FrameArgKind.Count: return "帧数";
                case FrameArgKind.Skip: return "跳帧";
                case FrameArgKind.Interval: return "间隔毫秒";
                case FrameArgKind.EffectStart: return "特效起始帧";
                case FrameArgKind.EffectCount: return "特效帧数";
                case FrameArgKind.EffectSkip: return "特效跳帧";
                case FrameArgKind.EffectInterval: return "特效间隔毫秒";
                default: return "";
            }
        }

        // T5：保存成功后把内存编辑标记为已保存（BaseValue=EditedValue），
        // 随后的 Rescan 才不会把刚落盘的新值误报成"源码被外部修改"。
        private void MarkAllSaved()
        {
            foreach (var r in _refs)
                if (r.Changed) r.BaseValue = r.EditedValue;
        }

        private void BindGrid()
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            _grid.Columns.Add(COL_LABEL, COL_LABEL);
            _grid.Columns.Add(COL_EXPR, COL_EXPR);
            _grid.Columns.Add(COL_VALUE, COL_VALUE);
            _grid.Columns.Add(COL_GROUP, COL_GROUP); // T6：组帧范围列
            _grid.Columns.Add(COL_SRC, COL_SRC);
            _grid.Columns.Add(COL_KIND, COL_KIND);
            _grid.Columns.Add(COL_TARGET, COL_TARGET);
            _grid.Columns.Add(COL_FLAG, COL_FLAG);

            // T5 直观性：先按筛选条件算出要显示的行（保存/撤销仍作用于全部引用，不受筛选影响）。
            _filtered = _refs.Where(PassFilter).ToList();
            _refsStatic = _refs; // T6：GroupText 静态方法用的快照

            foreach (var r in _filtered)
            {
                // T3.1：表达式行标注动态项，提示"实际帧=基址+动态项"，防止误改动态部分（规范§11.9）。
                string exprDisplay = r.Expression;
                if (r.Kind == RefKind.Expression && !string.IsNullOrEmpty(r.DynamicPart))
                    exprDisplay = r.Expression + "  （实际帧=基址" + r.DynamicPart + "；动态项勿动）";
                int row = _grid.Rows.Add(
                    r.HumanLabel,
                    exprDisplay,
                    r.EditedValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    GroupText(r), // T6：组帧范围
                    r.FilePath + ":" + r.Line,
                    KindText(r),
                    TargetText(r.Target),
                    BuildFlags(r));
                _grid.Rows[row].Tag = r;
                // 非帧号字段仍可编辑（count/skip/interval 等整数），但标注参数名。
                if (!r.LabelQualified)
                    _grid.Rows[row].Cells[COL_LABEL].Style.ForeColor = Color.Gray;
                if (r.PlayerGlobal)
                    _grid.Rows[row].Cells[COL_FLAG].Style.ForeColor = Color.DarkOrange;
                // T5：筛选/重扫会重建行实例，已改动项必须恢复黄色高亮，否则未保存状态不可见。
                if (r.Changed)
                    _grid.Rows[row].DefaultCellStyle.BackColor = Color.LightYellow;
            }

            _grid.Columns[COL_LABEL].Width = 240;
            _grid.Columns[COL_EXPR].Width = 300;
            _grid.Columns[COL_VALUE].Width = 80;
            _grid.Columns[COL_GROUP].Width = 210; // T6
            _grid.Columns[COL_SRC].Width = 260;
            _grid.Columns[COL_KIND].Width = 210;
            _grid.Columns[COL_TARGET].Width = 90;
            _grid.Columns[COL_FLAG].Width = 160;
            _grid.Columns[COL_VALUE].ReadOnly = false;
            UpdateStatus();
        }

        // T5 直观性：筛选条件判断（只影响显示；保存/撤销/差异始终针对全部引用）。
        private bool PassFilter(Reference r)
        {
            if (_chkChangedOnly.Checked && !r.Changed) return false;
            string q = (_txtFilter.Text ?? "").Trim();
            if (q.Length == 0) return true;
            return (r.HumanLabel != null && r.HumanLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (r.Expression != null && r.Expression.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (r.FilePath != null && r.FilePath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || r.EditedValue.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(q);
        }

        // T5 直观性：按当前筛选条件重建列表（输入即筛，无需按钮）。
        private void ApplyFilter()
        {
            if (_refs.Count == 0) return;
            BindGrid();
        }

        // T5 直观性：类型列中文化——枚举英文名（Constant/PlayerFrameArg…）零基础看不懂。
        private static string KindText(Reference r)
        {
            string kind;
            switch (r.Kind)
            {
                case RefKind.Constant: kind = "常量帧号"; break;
                case RefKind.Expression: kind = "表达式基址"; break;
                case RefKind.PlayerFrameArg: kind = "玩家动作帧表"; break;
                case RefKind.DefaultFrameArg: kind = "默认帧表(NPC/怪)"; break;
                case RefKind.LibFrames: kind = "lib自带帧表"; break;
                case RefKind.Indirect: kind = "间接引用"; break;
                default: kind = r.Kind.ToString(); break;
            }
            string arg;
            switch (r.ArgKind)
            {
                case FrameArgKind.FrameStart: arg = "起始帧"; break;
                case FrameArgKind.Count: arg = "帧数"; break;
                case FrameArgKind.Skip: arg = "跳帧"; break;
                case FrameArgKind.Interval: arg = "间隔毫秒"; break;
                case FrameArgKind.EffectStart: arg = "特效起始帧"; break;
                case FrameArgKind.EffectCount: arg = "特效帧数"; break;
                case FrameArgKind.EffectSkip: arg = "特效跳帧"; break;
                case FrameArgKind.EffectInterval: arg = "特效间隔毫秒"; break;
                default: arg = ""; break;
            }
            return arg.Length == 0 ? kind : kind + "-" + arg;
        }

        // T5 直观性：载体列中文化（Source/LibFrames → 源码/lib帧表）。
        private static string TargetText(string target)
        {
            if (target == "Source") return "源码";
            if (target == "LibFrames") return "lib帧表";
            return target;
        }

        // T6：组帧范围列文本。三档：
        // ① 有精确范围（new Frame 的 start/count/skip 或 Draw 连续串）→"共 N 帧：s–e（换/补图需 N 张）"
        // ② 只有组员（对象初始化器等多态组）→"同组 N 处：1903/1904/1905"
        // ③ 未分组 → 空串（单帧引用无需提示）。
        private static string GroupText(Reference r)
        {
            if (string.IsNullOrEmpty(r.GroupId)) return "";
            if (r.GroupStart >= 0 && r.GroupCount > 1)
            {
                int end = r.GroupStart + (r.GroupCount - 1) * (r.GroupSkip + 1);
                return string.Format("共 {0} 帧：{1}–{2}（换/补图需 {0} 张）",
                    r.GroupCount,
                    r.GroupStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    end.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            var members = GroupMembers(_refsStatic, r.GroupId);
            if (members == null || members.Count <= 1) return "";
            var vals = members.Select(m => m.EditedValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
                              .Distinct().ToList();
            return "同组 " + members.Count + " 处：" + string.Join("/", vals);
        }

        // T6：组员查询（GroupText 静态化需要引用列表穿透）。
        private static List<Reference> _refsStatic;
        private static List<Reference> GroupMembers(List<Reference> refs, string groupId)
        {
            if (refs == null || string.IsNullOrEmpty(groupId)) return null;
            return refs.Where(x => x.GroupId == groupId).ToList();
        }

        // T5 直观性：底部状态栏实时汇总（原 _lblStatus 从未赋值，是死控件）。
        private void UpdateStatus()
        {
            int changed = _refs.Count(r => r.Changed);
            _lblStatus.Text = string.Format(
                "本库引用 {0} 条 | 当前显示 {1} 条 | 已改动 {2} 条（黄色行=未保存）| 改完点「保存改动」一键写回+编译+部署",
                _refs.Count, _filtered.Count, changed);
            // T5 便利性：保存按钮联动——无改动置灰，有改动显示数量，一眼知道"还有没有没存的"。
            _btnSave.Enabled = changed > 0;
            _btnSave.Text = changed > 0 ? string.Format("保存改动({0})", changed) : "保存改动";
        }

        // T5 便利性：关窗保护——有未保存改动时先确认，防止点 X 静默丢失。
        private void ReverseIndexForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.ApplicationExitCall || e.CloseReason == CloseReason.FormOwnerClosing) return;
            int changed = _refs.Count(r => r.Changed);
            if (changed == 0) return;
            var confirm = MessageBox.Show(
                "还有 " + changed + " 处改动没有保存，直接关闭会丢失这些修改。\n确定要关闭吗？（要保留请先点「保存改动」）",
                "未保存的改动", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) e.Cancel = true;
        }

        private static string BuildFlags(Reference r)
        {
                        var parts = new List<string>();
            if (r.AmbiguousLib) parts.Add("歧义库");
            if (r.PlayerGlobal) parts.Add("玩家全局");
            if (r.GlobalFallback) parts.Add("全局兜底");
            if (r.SpecialMonsterList != null) parts.Add("特殊怪物");
            if (!string.IsNullOrEmpty(r.GroupId)) parts.Add("组"); // T6：说明该引用属于一组
            if (!r.LabelQualified) parts.Add("未命名(禁保存)");
            return string.Join("/", parts);
        }

        // T3.2: 选中歧义库(Indirect)引用时显示 lib 编号下拉，供用户手工指定具体库编号。
        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_grid.CurrentRow == null) { _cmboLib.Visible = false; return; }
            var r = _grid.CurrentRow.Tag as Reference;
            if (r == null || !r.AmbiguousLib) { _cmboLib.Visible = false; return; }
            _ambigRef = r;
            _suppressComboEvent = true;
            _cmboLib.Items.Clear();
            int count = _lib == null ? 0 : _lib.Count;
            if (count == 0) count = 256;
            for (int i = 0; i < count; i++) _cmboLib.Items.Add(i.ToString("D3"));
            _cmboLib.SelectedIndex = -1;
            _suppressComboEvent = false;
            _cmboLib.Visible = true;
        }

        // T3.2: 用户选定编号后改写 LibVariable 并解除歧义标记。
        private void ComboLib_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressComboEvent || _ambigRef == null || _cmboLib.SelectedIndex < 0) return;
            string varText = string.IsNullOrEmpty(_ambigRef.LibVariable)
                ? (_resolved == null ? "Libraries.Unknown" : _resolved.VariableText)
                : _ambigRef.LibVariable;
            int bracket = varText.IndexOf('[');
            if (bracket >= 0) varText = varText.Substring(0, bracket);
            _ambigRef.LibVariable = varText + "[" + _cmboLib.SelectedIndex + "]";
            _ambigRef.AmbiguousLib = false;
            _cmboLib.Visible = false;
            BindGrid();
            AppendResult("[歧义解除] " + _ambigRef.HumanLabel + " → " + _ambigRef.LibVariable);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != COL_VALUE) return;
            var r = _grid.Rows[e.RowIndex].Tag as Reference;
            if (r == null) return;
            int val;
            if (!int.TryParse(Convert.ToString(_grid.Rows[e.RowIndex].Cells[COL_VALUE].Value), out val))
            {
                AppendResult("[输入错误] 当前值必须是整数");
                _grid.Rows[e.RowIndex].Cells[COL_VALUE].Value = r.EditedValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
            r.EditedValue = val;
            _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = r.Changed ? Color.LightYellow : _grid.DefaultCellStyle.BackColor;
            // T6：值变化后同组行的"组帧范围"文本要实时刷新，否则显示的是上次绑定时的旧值列表。
            if (!string.IsNullOrEmpty(r.GroupId))
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var other = row.Tag as Reference;
                    if (other == null || other.GroupId != r.GroupId) continue;
                    row.Cells[COL_GROUP].Value = GroupText(other);
                }
            }
            UpdateStatus(); // T5：改动数实时刷新
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var r = _grid.Rows[e.RowIndex].Tag as Reference;
            if (r == null) return;
            // T6：双击「当前值」列 = 选单帧（原行为）；双击其他列 = 组图预览（第4问）。
            if (_grid.Columns[e.ColumnIndex].Name == COL_VALUE)
            {
                if (!r.IsFrameNumber)
                {
                    AppendResult("该项不是帧号（是帧数/跳帧/毫秒等），请用键盘输入整数。");
                    return;
                }
                var picker = new FramePickerForm(_lib, r.EditedValue);
                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    r.EditedValue = picker.SelectedFrame;
                    _grid.Rows[e.RowIndex].Cells[COL_VALUE].Value = r.EditedValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LightYellow;
                    // T6：选帧后同组行组文本同步刷新（与手输同一逻辑）。
                    if (!string.IsNullOrEmpty(r.GroupId))
                        foreach (DataGridViewRow row in _grid.Rows)
                        {
                            var other = row.Tag as Reference;
                            if (other != null && other.GroupId == r.GroupId)
                                row.Cells[COL_GROUP].Value = GroupText(other);
                        }
                }
                return;
            }
            ShowGroupPreview(r);
        }

        // T6（第4问）：组图预览——展开该引用所属组的全部帧（含真实帧号），
        // 直观看到"这个表达式背后是一组几张图、分别在哪几帧"，与"组帧范围"列联动。
        private void ShowGroupPreview(Reference r)
        {
            if (_lib == null) { AppendResult("尚未打开 lib，无法预览。"); return; }
            var frames = BuildGroupFrames(r);
            if (frames.Count == 0)
            {
                AppendResult("该引用是单帧（未发现同组帧），双击「当前值」列可单独选帧。");
                return;
            }
            using (var dlg = new GroupPreviewForm(_lib, frames, r.EditedValue, this, r))
            {
                // T7：预览里右键「跳到表格对应行」→ 关预览并定位来源行（DialogResult.Retry 约定）。
                if (dlg.ShowDialog(this) == DialogResult.Retry)
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        if (ReferenceEquals(row.Tag, r))
                        {
                            _grid.CurrentCell = row.Cells[COL_VALUE];
                            row.Selected = true;
                            break;
                        }
                    }
            }
        }

        // T6：计算某条引用的组帧列表（去重升序；有精确范围时按 start/skip 展开）。
        // 引用自身值不在范围内时（如改过帧号），也并入——用户关心的"当前指向"必须出现。
        private List<int> BuildGroupFrames(Reference r)
        {
            var set = new SortedSet<int>();
            if (r.IsFrameNumber && r.EditedValue >= 0 && r.EditedValue < _lib.Count) set.Add(r.EditedValue);
            if (!string.IsNullOrEmpty(r.GroupId))
            {
                if (r.GroupStart >= 0 && r.GroupCount > 1)
                {
                    for (int i = 0; i < r.GroupCount; i++)
                    {
                        int f = r.GroupStart + i * (r.GroupSkip + 1);
                        if (f >= 0 && f < _lib.Count) set.Add(f);
                    }
                }
                else
                {
                    var members = GroupMembers(_refsStatic ?? _refs, r.GroupId);
                    if (members != null)
                        foreach (var m in members)
                            if (m.IsFrameNumber && m.EditedValue >= 0 && m.EditedValue < _lib.Count)
                                set.Add(m.EditedValue);
                }
            }
            return set.ToList();
        }

        private void FrameBand_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            if (_lib == null || _refs.Count == 0) return;
            int count = Math.Max(1, _lib.Count);
            int w = Math.Max(1, _frameBand.ClientSize.Width);
            int h = _frameBand.ClientSize.Height;
            foreach (var r in _refs)
            {
                if (!r.IsFrameNumber) continue;
                int v = r.EditedValue;
                if (v < 0 || v >= count) continue;
                int x = (int)((long)v * w / count);
                int bw = Math.Max(2, (int)((long)w / count));
                Color c = r.PlayerGlobal ? Color.DarkOrange
                        : r.Kind == RefKind.Expression ? Color.RoyalBlue
                        : r.Kind == RefKind.Indirect ? Color.MediumPurple
                        : Color.SeaGreen;
                using (var brush = new SolidBrush(c))
                    g.FillRectangle(brush, x, 8, bw, h - 16);
            }
        }

        private void FrameBand_MouseClick(object sender, MouseEventArgs e)
        {
            if (_lib == null || _refs.Count == 0) return;
            int count = Math.Max(1, _lib.Count);
            int w = Math.Max(1, _frameBand.ClientSize.Width);
            int bw = Math.Max(2, (int)((long)w / count)); // 与 FrameBand_Paint 的标记宽度一致
            // T5：优先命中光标处"看得见的色块"（Paint 画在 x0=v*w/count、宽 bw），
            // 消除像素→帧号二次取整方向相反导致的"点中 1903 的格子却定位到 1901 行"错位。
            Reference hit = null;
            int hitDist = int.MaxValue;
            foreach (var r in _refs)
            {
                if (!r.IsFrameNumber) continue;
                int v = r.EditedValue;
                if (v < 0 || v >= count) continue;
                int x0 = (int)((long)v * w / count);
                if (e.X < x0 || e.X >= x0 + bw) continue;
                int d = e.X - x0; // 同一像素叠多个标记时取左缘最近的，保持确定性
                if (d < hitDist) { hitDist = d; hit = r; }
            }
            if (hit == null)
            {
                // 空白区回退：按像素反推帧号取第一条匹配引用（原行为）。
                int frame = (int)((long)e.X * count / w);
                hit = _refs.FirstOrDefault(r => r.IsFrameNumber && r.EditedValue == frame);
            }
            if (hit == null) return;
            // T5：列表可能处于筛选状态，行下标与 _refs 不再一一对应，必须按 Tag 找行。
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (ReferenceEquals(row.Tag, hit))
                {
                    _grid.CurrentCell = row.Cells[COL_VALUE];
                    row.Selected = true;
                    return;
                }
            }
        }

        // T6：长耗时操作（保存/编译/部署）期间禁用操作按钮、锁定网格并显示进行中提示，
        // 防止零基础用户以为界面卡死而重复点击（导致重复编译/重复换件）。
        private void SetBusy(bool busy, string phase)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            foreach (var b in new[] { _btnRescan, _btnSave, _btnCompile, _btnDeploy, _btnDiff, _btnUndo, _btnAlias, _btnMapSetup, _btnRestore })
                b.Enabled = !busy;
            _grid.ReadOnly = busy; // 保存期间禁止再编辑（写回用的是改前快照，编辑会丢失）
            if (busy) _lblStatus.Text = "正在" + phase + "……请勿重复点击，完成后会自动显示结果。";
            else UpdateStatus();
        }

        /// <summary>保存改动：门禁 → 备份 → 写源码/lib → [有源码变更才]编译+换件 → 状态矩阵。</summary>
        private void SaveAll()
        {
            if (_busy) { AppendResult("[进行中] 已有保存/编译/部署操作在执行，请等它完成。"); return; }
            var changed = _refs.Where(r => r.Changed).ToList();
            if (changed.Count == 0)
            {
                AppendResult("没有改动需要保存。");
                return;
            }

            // 标签质量门禁（§11.7）：未命名标签禁止保存
            var unqualified = changed.Where(r => !r.LabelQualified).ToList();
            if (unqualified.Count > 0)
            {
                AppendResult("[拒绝保存] 有 " + unqualified.Count + " 项没有合格人话说明，请先在别名管理中命名：");
                foreach (var r in unqualified.Take(10)) AppendResult("  " + r.FilePath + ":" + r.Line + "  " + r.Expression);
                return;
            }

            // P1-5：帧号范围校验——帧号超出 lib 帧数（或为负）写进源码/lib 都会导致游戏画不出图
            int maxFrame = _lib != null ? _lib.Count : int.MaxValue;
            var outOfRange = changed
                .Where(r => r.IsFrameNumber && r.Target == "Source" &&
                            (r.EditedValue < 0 || r.EditedValue >= maxFrame))
                .ToList();
            if (outOfRange.Count > 0)
            {
                AppendResult("[拒绝保存] 有 " + outOfRange.Count + " 项帧号超出范围（本库帧数 " + maxFrame + "，合法 0–" + (maxFrame - 1) + "）：");
                foreach (var r in outOfRange.Take(10)) AppendResult("  " + r.HumanLabel + " = " + r.EditedValue);
                return;
            }

            // P0-1：lib 帧表帧号参数的界内校验（lib 帧表不受上面 Source 校验覆盖）
            var libOutOfRange = changed
                .Where(r => r.Target == "LibFrames" && !r.IsFrameNumberLibCheck)
                .ToList();
            if (libOutOfRange.Count > 0)
            {
                AppendResult("[拒绝保存] 有 " + libOutOfRange.Count + " 项 lib 帧表帧号超出范围（本库帧数 " + maxFrame + "）：");
                foreach (var r in libOutOfRange.Take(10)) AppendResult("  " + r.HumanLabel + " = " + r.EditedValue);
                return;
            }

            // 缺陷②：玩家全局帧表二次确认
            if (changed.Any(r => r.PlayerGlobal))
            {
                var confirm = MessageBox.Show(
                    "本次改动包含「玩家全局帧表」（Player.Add），会被所有玩家装备库共用，影响全部玩家。\n确认继续？",
                    "玩家全局帧表确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) { AppendResult("已取消保存。"); return; }
            }

            // 缺陷④：客户端进程门禁必须在任何写操作之前
            if (Deployer.ClientRunning())
            {
                AppendResult("[拒绝保存] 客户端 Client.exe 正在运行，请先关闭客户端再保存（lib 写回与换件都需要）。");
                return;
            }

            SetBusy(true, "保存改动（写回源码 → 编译 → 部署换件）");
            AppendResult("正在保存改动……（写回源码 → 编译 → 部署换件，约 1 分钟；请勿重复点击）");
            Application.DoEvents();

            var backup = new BackupManager(_cfg);
            var matrix = new StringBuilder();
            matrix.AppendLine("==== 保存状态矩阵 ====");

            try
            {
                // 1) 备份所有将改动的源码
                var srcChanged = changed.Where(r => r.Target == "Source").ToList();
                var distinctFiles = srcChanged.Select(r => r.FilePath).Distinct().ToList();
                foreach (var f in distinctFiles) backup.BackupSource(f);
                matrix.AppendLine("备份：源码 " + distinctFiles.Count + " 个文件 → " + backup.SessionDir);

                // 2) lib 帧表写回（本库自带 Frames，若有 LibFrames 类引用被改）
                var libChanged = changed.Where(r => r.Target == "LibFrames").ToList();
                if (libChanged.Count > 0)
                {
                    // P0-1：先把表格里的编辑落到内存帧表（LibFramesWriter 只负责序列化写盘）
                    ApplyLibFrameEdits(libChanged);
                    var lw = new LibFramesWriter(_cfg, backup);
                    var lr = lw.Write(_lib);
                    matrix.AppendLine("lib 帧表：" + (lr.Success ? "✓ " : "✗ ") + lr.Message + "（写入 " + matrixLibApplied + " 个参数）");
                    if (!lr.Success) { RecordBackupSession(backup); AppendResult(matrix.ToString()); Rescan(); return; }
                }
                else
                {
                    matrix.AppendLine("lib 帧表：跳过（无本库帧表改动）");
                }

                // 3) 源码写回
                if (srcChanged.Count > 0)
                {
                    var sw = new SourceWriter(_cfg);
                    var wr = sw.Write(srcChanged);
                    matrix.AppendLine("源码写回：" + (wr.Ok ? "✓" : "✗") + " 文件 " + wr.FilesModified.Count + " / token " + wr.TokensReplaced);
                    foreach (var err in wr.Errors) matrix.AppendLine("  [写回错误] " + err);
                    // P1-9：写回失败（可能部分文件已写入）必须立即重扫，让界面与磁盘一致，
                    // 否则内存里的旧值与磁盘新值互相覆盖，用户反复保存也无法脱离不一致状态。
                    if (!wr.Ok) { RecordBackupSession(backup); AppendResult(matrix.ToString()); Rescan(); return; }
                }
                else
                {
                    matrix.AppendLine("源码写回：跳过（无源码改动）");
                }

                // 缺陷⑤：仅 LibFrames 变更时跳过编译与换件
                if (srcChanged.Count == 0)
                {
                    matrix.AppendLine("编译：跳过（无源码变更）");
                    matrix.AppendLine("部署：跳过（无源码变更）");
                    matrix.AppendLine("结论：lib 帧表已写入，请重启客户端。");
                    MarkAllSaved(); // T5：lib 写入成功，清除未保存标记，随后重扫不会误报
                    RecordBackupSession(backup); // P0-2：lib 备份在会话内，可一键还原
                    AppendResult(matrix.ToString());
                    Rescan();
                    return;
                }

                // 4) 编译
                var compiler = new Compiler(_cfg);
                var cr = compiler.Build();
                matrix.AppendLine("编译：" + (cr.Success ? "✓ 成功" : "✗ 失败")
                    + " 错误 " + cr.ErrorCount + " / 警告 " + cr.WarningCount + " / 新增警告 " + cr.NewWarnings
                    + "（退出码 " + cr.ExitCode + "）" + CompileCompareSuffix(cr.ErrorCount, cr.WarningCount));
                SaveCompileCounts(cr.ErrorCount, cr.WarningCount); // T4.3:记录本次基线供下次对比
                if (!string.IsNullOrEmpty(cr.Message)) matrix.AppendLine("  " + cr.Message);
                if (!cr.Success)
                {
                    // P0-2：撤销只回内存值，源码已被改写——必须如实告知并提供"回到备份"按钮。
                    matrix.AppendLine("部署：未执行（编译失败）。源码已改动，可点「回到备份」还原本次保存前的文件，或修正错误后重新保存。");
                    RecordBackupSession(backup);
                    AppendResult(matrix.ToString() + cr.RawOutput);
                    UpdateRestoreButton();
                    return;
                }

                // 5) 部署换件
                var deployer = new Deployer(_cfg, backup);
                var dr = deployer.Deploy();
                matrix.AppendLine("部署：" + (dr.Success ? "✓ " : "✗ ") + dr.Message);

                MarkAllSaved(); // T5：保存成功，清除未保存标记，随后的重扫不会把新值误报成"外部修改"
                RecordBackupSession(backup); // P0-2：记录会话供一键还原
                UpdateRestoreButton();
                AppendResult(matrix.ToString());
                AppendResult("----- 编译原始输出 -----");
                AppendResult(cr.RawOutput);
                Rescan();
            }
            catch (Exception ex)
            {
                AppendResult("[保存异常] " + ex);
            }
            finally { SetBusy(false, null); }
        }

        // P0-1：把 LibFrames 类引用的编辑值写回内存帧表（保存链路的 lib 侧落点）。
        private void ApplyLibFrameEdits(List<Reference> libChanged)
        {
            if (_lib == null || _lib.Frames == null) return;
            int applied = 0;
            foreach (var grp in libChanged.GroupBy(r => r.LibFrameAction))
            {
                MirAction action;
                if (!Enum.TryParse(grp.Key, out action)) continue;
                Frame f;
                if (!_lib.Frames.TryGetValue(action, out f) || f == null) continue;
                foreach (var r in grp)
                {
                    switch (r.ArgKind)
                    {
                        case FrameArgKind.FrameStart: f.Start = r.EditedValue; break;
                        case FrameArgKind.Count: f.Count = r.EditedValue; break;
                        case FrameArgKind.Skip: f.Skip = r.EditedValue; break;
                        case FrameArgKind.Interval: f.Interval = r.EditedValue; break;
                        case FrameArgKind.EffectStart: f.EffectStart = r.EditedValue; break;
                        case FrameArgKind.EffectCount: f.EffectCount = r.EditedValue; break;
                        case FrameArgKind.EffectSkip: f.EffectSkip = r.EditedValue; break;
                        case FrameArgKind.EffectInterval: f.EffectInterval = r.EditedValue; break;
                    }
                    applied++;
                }
            }
            matrixLibApplied = applied;
        }

        private int matrixLibApplied; // 保存矩阵展示用（本次写入 lib 帧表的参数个数）

        // P0-2：记录最近备份会话目录，供「回到备份」按钮使用。
        private void RecordBackupSession(BackupManager backup)
        {
            try
            {
                if (backup != null && Directory.Exists(backup.SessionDir)) _lastBackupSession = backup.SessionDir;
            }
            catch { }
            UpdateRestoreButton();
        }

        // P0-2：根据是否有可用备份会话，更新「回到备份」按钮的可用性与标题。
        private void UpdateRestoreButton()
        {
            if (_btnRestore == null) return;
            bool has = !string.IsNullOrEmpty(_lastBackupSession) && Directory.Exists(_lastBackupSession);
            _btnRestore.Enabled = has;
            _btnRestore.Text = has ? "回到备份" : "回到备份（无）";
        }

        // P0-2：「回到本次备份」——把最近一次保存会话备份的源码文件全部还原到磁盘，然后重扫。
        // 部署 7 件与 lib 备份一并还原（若会话中存在），保证源码/产物/运行目录三者一致。
        private void RestoreLastBackup()
        {
            if (_busy) { AppendResult("[进行中] 已有操作在执行，请稍候。"); return; }
            if (string.IsNullOrEmpty(_lastBackupSession) || !Directory.Exists(_lastBackupSession))
            {
                AppendResult("没有可用的本次备份（只有点过「保存改动」后才有）。");
                return;
            }
            if (Deployer.ClientRunning())
            {
                AppendResult("[拒绝恢复] 客户端 Client.exe 正在运行，请先关闭。");
                return;
            }
            var confirm = MessageBox.Show(
                "把最近一次保存（" + Path.GetFileName(_lastBackupSession) + "）之前的文件还原？\n"
                + "将还原：本次改动的源码 / lib / 部署文件，界面未保存编辑会丢弃。\n确定继续？",
                "回到备份确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) { AppendResult("已取消还原。"); return; }

            SetBusy(true, "还原备份");
            int restored = 0, failed = 0;
            try
            {
                var backup = new BackupManager(_cfg); // 仅借用 Restore 的白名单校验
                var session = _lastBackupSession;

                // 源码备份：SessionDir 下与 ScanRoot 相对路径同构的 .cs 文件
                foreach (var bf in Directory.GetFiles(session, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = bf.Substring(session.Length).TrimStart('\\', '/');
                    try { backup.Restore(bf, Path.Combine(_cfg.RepoRoot, rel)); restored++; }
                    catch (Exception ex) { failed++; AppendResult("  [还原失败] " + rel + ": " + ex.Message); }
                }
                // lib 备份：libs\<名>.bak → 还原到原 lib 路径
                string libsDir = Path.Combine(session, "libs");
                if (Directory.Exists(libsDir))
                {
                    foreach (var bf in Directory.GetFiles(libsDir, "*.bak"))
                    {
                        string libName = Path.GetFileNameWithoutExtension(bf); // 去掉 .bak
                        string target = Path.Combine(Path.GetDirectoryName(_libPath ?? "") ?? "", libName);
                        if (File.Exists(target)) { File.Copy(bf, target, true); restored++; }
                    }
                }
                // 部署文件备份：deploy\* → 还原到部署目录
                string deployDir = Path.Combine(session, "deploy");
                if (Directory.Exists(deployDir))
                {
                    foreach (var bf in Directory.GetFiles(deployDir))
                    {
                        try { backup.Restore(bf, Path.Combine(_cfg.DeployRoot, Path.GetFileName(bf))); restored++; }
                        catch (Exception ex) { failed++; AppendResult("  [还原失败] " + Path.GetFileName(bf) + ": " + ex.Message); }
                    }
                }

                AppendResult("[还原] 已还原 " + restored + " 个文件" + (failed > 0 ? "，失败 " + failed + " 个" : "") + "。正在重新扫描……");
                // 还原后内存编辑全部失效：清空未保存标记并重扫
                foreach (var r in _refs) { r.EditedValue = r.BaseValue; }
                _lastBackupSession = null;
                UpdateRestoreButton();
                Rescan();
            }
            catch (Exception ex)
            {
                AppendResult("[还原异常] " + ex);
            }
            finally { SetBusy(false, null); }
        }

        private void CompileOnly()
        {
            if (_busy) { AppendResult("[进行中] 已有保存/编译/部署操作在执行，请等它完成。"); return; }
            if (Deployer.ClientRunning())
            {
                AppendResult("[提示] 客户端正在运行；编译可执行，但换件需先关闭客户端。");
            }
            SetBusy(true, "编译（约 1 分钟）");
            try
            {
                AppendResult("正在编译，请稍候……");
                Application.DoEvents();
                var cr = new Compiler(_cfg).Build();
                AppendResult("编译：" + (cr.Success ? "成功" : "失败") + " 错误 " + cr.ErrorCount + " / 警告 " + cr.WarningCount + " / 新增 " + cr.NewWarnings + " / 退出码 " + cr.ExitCode + CompileCompareSuffix(cr.ErrorCount, cr.WarningCount)); // T4.3:对比上次编译
                SaveCompileCounts(cr.ErrorCount, cr.WarningCount); // T4.3:记录本次基线供下次对比
                if (!string.IsNullOrEmpty(cr.Message)) AppendResult(cr.Message);
                foreach (var iss in cr.Issues)
                    AppendResult("  " + (iss.IsError ? "错误" : "警告") + " " + iss.Code + " " + iss.File + "(" + iss.Line + "," + iss.Column + "): " + iss.Message + (iss.IsKnownBaseline ? " [既有基线]" : ""));
                AppendResult("----- 原始输出 -----\n" + cr.RawOutput);
            }
            finally { SetBusy(false, null); }
        }

        // P2-15：部署前新鲜度校验——BuildOut 产物必须都不旧于源码，否则会把陈旧产物换进运行目录。
        // 返回 null=通过；否则返回提示文本（含最旧产物与最新源码时间）。
        private string CheckBuildFreshness()
        {
            try
            {
                DateTime newestSrc = DateTime.MinValue, oldestOut = DateTime.MaxValue;
                string newestSrcName = "", oldestOutName = "";
                foreach (var f in Directory.GetFiles(_cfg.ScanRoot, "*.cs", SearchOption.AllDirectories))
                {
                    var m = File.GetLastWriteTimeUtc(f);
                    if (m > newestSrc) { newestSrc = m; newestSrcName = f; }
                }
                foreach (var name in ReverseIndexConfig.DeployFiles)
                {
                    string p = Path.Combine(_cfg.BuildOut, name);
                    if (!File.Exists(p)) continue; // 缺件由 Deployer 的存在性预校验负责
                    var m = File.GetLastWriteTimeUtc(p);
                    if (m < oldestOut) { oldestOut = m; oldestOutName = name; }
                }
                if (newestSrc > oldestOut)
                    return "编译产物比源码旧（" + Path.GetFileName(oldestOutName) + " 产物时间早于 " + Path.GetFileName(newestSrcName)
                        + "），直接换件会把旧产物换进游戏目录。\n请先点「重编译」或「保存改动」。";
                return null;
            }
            catch (Exception ex) { return "无法校验产物新鲜度：" + ex.Message + "（可改用「保存改动」全链路）。"; }
        }

        private void DeployOnly()
        {
            if (_busy) { AppendResult("[进行中] 已有保存/编译/部署操作在执行，请等它完成。"); return; }
            if (Deployer.ClientRunning())
            {
                AppendResult("[拒绝部署] 客户端 Client.exe 正在运行，请先关闭客户端。");
                return;
            }
            string stale = CheckBuildFreshness(); // P2-15：陈旧产物不换件
            if (stale != null) { AppendResult("[拒绝部署] " + stale); return; }
            SetBusy(true, "部署换件");
            try
            {
                var backup = new BackupManager(_cfg);
                var dr = new Deployer(_cfg, backup).Deploy();
                AppendResult("部署：" + (dr.Success ? "成功" : "失败") + " — " + dr.Message);
                foreach (var f in dr.Files) AppendResult("  " + f.FileName + ": " + (f.Ok ? "✓" : "✗ " + f.Message));
            }
            finally { SetBusy(false, null); }
        }

        private void ShowDiff()
        {
            var changed = _refs.Where(r => r.Changed).ToList();
            if (changed.Count == 0) { AppendResult("没有改动可预览。"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("==== 改动预览（diff）====");
            foreach (var r in changed)
            {
                string oldLine = ReadSourceLine(r.FilePath, r.Line);
                string newLine = oldLine;
                int start = r.TokenStart - GetLineCharStart(r.FilePath, r.Line);
                if (start >= 0 && start + r.TokenLength <= oldLine.Length)
                    newLine = oldLine.Substring(0, start) + r.EditedValue + oldLine.Substring(start + r.TokenLength);
                sb.AppendLine("[" + r.HumanLabel + "]  " + r.FilePath + ":" + r.Line + "  " + r.Target);
                sb.AppendLine("  - " + oldLine.Trim());
                sb.AppendLine("  + " + newLine.Trim());
            }
            AppendResult(sb.ToString());
        }

        private void Undo()
        {
            var changed = _refs.Where(r => r.Changed).ToList();
            if (changed.Count == 0) { AppendResult("没有已编辑的项可撤销。"); return; }
            foreach (var r in changed) r.EditedValue = r.BaseValue;
            BindGrid();
            AppendResult("已撤销本界面的编辑（未写回磁盘的保持原值；已写回的请用备份恢复）。");
        }

        private string ReadSourceLine(string relativePath, int line)
        {
            try
            {
                string full = Path.Combine(_cfg.RepoRoot, relativePath.Replace('/', '\\'));
                var lines = File.ReadAllLines(full);
                return line > 0 && line <= lines.Length ? lines[line - 1] : "(行不存在)";
            }
            catch { return "(读取失败)"; }
        }

        private int GetLineCharStart(string relativePath, int line)
        {
            try
            {
                string full = Path.Combine(_cfg.RepoRoot, relativePath.Replace('/', '\\'));
                string text = File.ReadAllText(full);
                int cur = 1, idx = 0;
                while (cur < line && idx < text.Length)
                {
                    if (text[idx] == '\n') { cur++; }
                    idx++;
                }
                return idx;
            }
            catch { return -1; }
        }

        private void AppendResult(string s)
        {
            _txtResult.AppendText(s + Environment.NewLine);
            // P2-10：滚动裁剪代替整体清空——超长时丢最前面的旧日志，保留最近的（Clear 会让用户突然"丢历史"）
            if (_txtResult.TextLength > 30000)
                _txtResult.Text = _txtResult.Text.Substring(_txtResult.TextLength - 20000);
            _txtResult.SelectionStart = _txtResult.Text.Length;
            _txtResult.ScrollToCaret();
        }

        // T3.3: 别名管理弹窗——为引用设置"人话"标签，持久化到 FrameAliases.json。
        private void ManageAliases()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "别名管理（人话标签）";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(760, 520);
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
                };
                grid.Columns.Add("disp", "当前说明");
                grid.Columns.Add("src", "来源(文件:行)");
                grid.Columns.Add("alias", "自定义别名");
                var candidates = _refs.Where(r => r != null).OrderBy(r => r.LabelQualified).ThenBy(r => r.FilePath).ToList();
                foreach (var r in candidates)
                {
                    string alias;
                    bool has = _aliasMgr.Aliases.TryGetValue(r.Id, out alias);
                    int row = grid.Rows.Add(r.HumanLabel, r.FilePath + ":" + r.Line, has ? alias : "");
                    grid.Rows[row].Tag = r;
                }
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
                var txt = new TextBox { Width = 360 };
                var btnSet = new Button { Text = "设为别名" };
                var btnDel = new Button { Text = "删除别名" };
                var btnClose = new Button { Text = "关闭" };
                btnSet.Click += (s, e) =>
                {
                    if (grid.CurrentRow == null) return;
                    var r = grid.CurrentRow.Tag as Reference;
                    if (r == null) return;
                    _aliasMgr.SetAlias(r.Id, txt.Text.Trim());
                    _aliasMgr.Reload();
                    grid.CurrentRow.Cells["alias"].Value = txt.Text.Trim();
                    grid.CurrentRow.Cells["disp"].Value = txt.Text.Trim();
                    AppendResult("[别名] 已保存 " + r.FilePath + ":" + r.Line + " => " + txt.Text.Trim());
                };
                btnDel.Click += (s, e) =>
                {
                    if (grid.CurrentRow == null) return;
                    var r = grid.CurrentRow.Tag as Reference;
                    if (r == null) return;
                    _aliasMgr.RemoveAlias(r.Id);
                    _aliasMgr.Reload();
                    grid.CurrentRow.Cells["alias"].Value = "";
                    Rescan();
                };
                btnClose.Click += (s, e) => dlg.Close();
                grid.CellClick += (s, e) =>
                {
                    if (grid.CurrentRow == null) return;
                    var r = grid.CurrentRow.Tag as Reference;
                    if (r == null) return;
                    txt.Text = grid.CurrentRow.Cells["alias"].Value == null ? "" : grid.CurrentRow.Cells["alias"].Value.ToString();
                };
                bar.Controls.Add(txt);
                bar.Controls.Add(btnSet);
                bar.Controls.Add(btnDel);
                bar.Controls.Add(btnClose);
                dlg.Controls.Add(grid);
                dlg.Controls.Add(bar);
                dlg.ShowDialog(this);
            }
            Rescan(); // 让新别名在人话标签中立即生效
        }

        // T3.3: 映射设置弹窗——修正 lib↔源码变量映射，持久化到 LibraryMapOverrides.json。
        private void ManageMap()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "映射设置（lib ↔ 源码变量）";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(820, 540);
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
                };
                grid.Columns.Add("var", "源码变量");
                grid.Columns.Add("disk", "磁盘目录");
                grid.Columns.Add("file", "文件基名/编号位数");
                grid.Columns.Add("array", "数组");
                grid.Columns.Add("override", "覆盖变量名(留空=还原)");
                foreach (var e in _map.Entries)
                {
                    // key 用解析原始名（SourceVariable）：覆盖改名后仍能找回自己的覆盖项
                    string key = "Libraries." + e.SourceVariable;
                    string ov;
                    bool has = _aliasMgr.MapOverrides.TryGetValue(key, out ov);
                    int row = grid.Rows.Add(e.SourceVariable, e.DiskDir, e.FileBase + (e.IsArray ? ("[" + e.Digits + "]") : ""), e.IsArray ? "是" : "否", has ? ov : "");
                    grid.Rows[row].Tag = e;
                }
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
                var txt = new TextBox { Width = 300 };
                var btnSet = new Button { Text = "设为覆盖" };
                var btnClose = new Button { Text = "关闭" };
                var hint = new Label { Text = "覆盖变量名用 Libraries.XXX 形式（数组库粘贴带 [编号] 会自动取基名）；留空并点设为覆盖=还原。改后需重扫。", AutoSize = true, Padding = new Padding(4) };
                btnSet.Click += (s, e) =>
                {
                    if (grid.CurrentRow == null) return;
                    var entry = grid.CurrentRow.Tag as LibraryEntry;
                    if (entry == null) return;
                    string target = txt.Text.Trim();
                    // key 用解析原始名：覆盖生效改名后，还原/再覆盖仍指向同一条目
                    _aliasMgr.SetMapOverride("Libraries." + entry.SourceVariable, target);
                    _aliasMgr.Reload();
                    // 重新加载映射到内存并应用覆盖
                    _map.Load(_cfg);
                    ApplyMapOverrides();
                    // 行 Tag 指向重载前的旧条目对象，换成本轮新对象（SourceVariable 相同）
                    var fresh = _map.Entries.FirstOrDefault(x => x.SourceVariable == entry.SourceVariable);
                    if (fresh != null) grid.CurrentRow.Tag = fresh;
                    grid.CurrentRow.Cells["override"].Value = target;
                    AppendResult("[映射] 已保存覆盖 " + entry.SourceVariable + " => " + (string.IsNullOrEmpty(target) ? "(还原)" : target));
                    Rescan();
                };
                btnClose.Click += (s, e) => dlg.Close();
                grid.CellClick += (s, e) =>
                {
                    if (grid.CurrentRow == null) return;
                    txt.Text = grid.CurrentRow.Cells["override"].Value == null ? "" : grid.CurrentRow.Cells["override"].Value.ToString();
                };
                bar.Controls.Add(hint);
                bar.Controls.Add(txt);
                bar.Controls.Add(btnSet);
                bar.Controls.Add(btnClose);
                dlg.Controls.Add(grid);
                dlg.Controls.Add(bar);
                dlg.ShowDialog(this);
            }
        }

        // T4.3: 读取上次编译计数（ConfigDir\LastCompileResult.json）用于对比。返回 null 表示无历史。
        private int[] ReadLastCompileCounts()
        {
            try
            {
                string path = Path.Combine(_cfg.ConfigDir, "LastCompileResult.json");
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                var mErr = System.Text.RegularExpressions.Regex.Match(json, "\"errors\"\\s*:\\s*(\\d+)");
                var mWarn = System.Text.RegularExpressions.Regex.Match(json, "\"warnings\"\\s*:\\s*(\\d+)");
                if (!mErr.Success || !mWarn.Success) return null;
                return new[] { int.Parse(mErr.Groups[1].Value), int.Parse(mWarn.Groups[1].Value) };
            }
            catch { return null; }
        }

        // T4.3: 写回本次编译计数。
        private void SaveCompileCounts(int errors, int warnings)
        {
            try
            {
                Directory.CreateDirectory(_cfg.ConfigDir);
                string path = Path.Combine(_cfg.ConfigDir, "LastCompileResult.json");
                string json = "{\"errors\":" + errors + ",\"warnings\":" + warnings + ",\"time\":\"" + DateTime.Now.ToString("s") + "\"}";
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch { }
        }

        // T4.3: 生成"较上次"对比片段。
        private string CompileCompareSuffix(int errors, int warnings)
        {
            int[] last = ReadLastCompileCounts();
            if (last == null) return "（首次编译，无对比基线）";
            int de = errors - last[0], dw = warnings - last[1];
            return string.Format("（较上次 错误{0} / 警告{1}）",
                de == 0 ? "±0" : (de > 0 ? "+" + de : de.ToString()),
                dw == 0 ? "±0" : (dw > 0 ? "+" + dw : dw.ToString()));
        }
    }

    /// <summary>选帧弹窗：lib 缩略图网格（带帧号），选中回填。</summary>
    internal sealed class FramePickerForm : Form
    {
        private readonly MLibraryV2 _lib;
        // T4.1: 缩略图分页，避免大库一次性生成上千控件卡顿。
        private readonly FlowLayoutPanel _panel;
        private readonly Label _hint;
        private int _page;
        private const int PageSize = 60;
        public int SelectedFrame { get; private set; }

        public FramePickerForm(MLibraryV2 lib, int current)
        {
            _lib = lib;
            SelectedFrame = current;
            this.Text = "从 lib 选择帧号";
            this.StartPosition = FormStartPosition.CenterParent;
            this.WindowState = FormWindowState.Maximized;

            int count = lib == null ? 0 : lib.Count;
            _page = current >= 0 ? current / PageSize : 0;

            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var prev = new Button { Text = "上一页", Width = 80 };
            var next = new Button { Text = "下一页", Width = 80 };
            prev.Click += (s, e) => { if (_page > 0) { _page--; BuildPage(); } };
            next.Click += (s, e) => { if ((_page + 1) * PageSize < count) { _page++; BuildPage(); } };
            nav.Controls.Add(prev);
            nav.Controls.Add(next);
            this.Controls.Add(nav);

            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            this.Controls.Add(_panel);
            _hint = new Label { Dock = DockStyle.Bottom, Height = 26 };
            this.Controls.Add(_hint);
            this.FormClosed += (s, e) => DisposePageImages(); // P2-13：关闭后释放本页克隆图
            BuildPage();
        }

        // P2-13：释放当前页所有 PictureBox 的克隆图（窗体关闭/翻页共用）。
        private void DisposePageImages()
        {
            foreach (Control c in _panel.Controls)
            {
                foreach (Control sub in c.Controls)
                {
                    var pic = sub as PictureBox;
                    if (pic != null && pic.Image != null) { pic.Image.Dispose(); pic.Image = null; }
                }
            }
        }

        // T4.1: 每次只构建当前页的 60 个缩略图，翻页时释放上一页图片引用。
        private void BuildPage()
        {
            DisposePageImages();
            _panel.Controls.Clear();
            int count = _lib == null ? 0 : _lib.Count;
            int start = _page * PageSize;
            int end = System.Math.Min(start + PageSize, count);
            for (int i = start; i < end; i++)
            {
                var cell = new Panel { Width = 92, Height = 92, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3) };
                var pic = new PictureBox { Dock = DockStyle.Top, Height = 64, SizeMode = PictureBoxSizeMode.Zoom };
                try
                {
                    var img = _lib.GetMImage(i);
                    if (img != null) pic.Image = (Image)img.Image.Clone();
                }
                catch { }
                var lab = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Text = i.ToString() };
                int captured = i;
                cell.Click += (s, e) => { SelectedFrame = captured; this.DialogResult = DialogResult.OK; this.Close(); };
                pic.Click += (s, e) => { SelectedFrame = captured; this.DialogResult = DialogResult.OK; this.Close(); };
                cell.Controls.Add(pic);
                cell.Controls.Add(lab);
                _panel.Controls.Add(cell);
            }
            int pages = count == 0 ? 0 : (count + PageSize - 1) / PageSize;
            _hint.Text = "第 " + (_page + 1) + "/" + pages + " 页  共 " + count + " 帧（点击帧号选择）";
        }
    }

    /// <summary>
    /// T6（第4问）：组图预览弹窗——一次展示一组的全部帧缩略图，带真实帧号；
    /// 当前引用指向的帧高亮。右键菜单提供实用操作（复制帧号/复制带帧号文件名清单），
    /// 后续可继续扩展（右键功能用户未定，先给可用的占位能力）。
    /// </summary>
    internal sealed class GroupPreviewForm : Form
    {
        private readonly MLibraryV2 _lib;
        private readonly List<int> _frames;
        private readonly FlowLayoutPanel _panel;
        private readonly Label _hint;
        private readonly ContextMenuStrip _menu; // T6 右键：复制帧号 + T7 扩展：跳到表格行/导出组图
        private readonly ReverseIndexForm _owner; // T7：来源表格（跳转用），可为 null
        private readonly Reference _ownerRef;     // T7：双击来源的引用（跳转目标行），可为 null

        public GroupPreviewForm(MLibraryV2 lib, List<int> frames, int currentFrame,
                                ReverseIndexForm owner = null, Reference ownerRef = null)
        {
            _lib = lib;
            _frames = frames ?? new List<int>();
            _owner = owner;
            _ownerRef = ownerRef;
            this.Text = "组图预览（共 " + _frames.Count + " 帧）";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(Math.Min(1200, 130 * Math.Max(1, Math.Min(_frames.Count, 8)) + 40), 420);
            this.MinimumSize = new Size(360, 260);

            _hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Text = "当前帧：" + currentFrame + "（高亮）。缩略图按帧号排序；右键可复制帧号、跳到表格行、导出本组图片。",
                Padding = new Padding(8, 4, 8, 0)
            };
            this.Controls.Add(_hint);

            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            this.Controls.Add(_panel);

            _menu = new ContextMenuStrip();
            var miCopy = new ToolStripMenuItem("复制帧号");
            miCopy.Click += (s, e) =>
            {
                var cell = _menu.SourceControl as Control;
                int f;
                if (cell != null && int.TryParse(cell.Tag as string, out f))
                    try { Clipboard.SetText(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch { }
            };
            var miCopyAll = new ToolStripMenuItem("复制本组全部帧号");
            miCopyAll.Click += (s, e) =>
            {
                try { Clipboard.SetText(string.Join(",", _frames)); } catch { }
            };
            // T7：跳回表格行——预览里"这个帧在哪用的"与表格行互查（从表格打开时才可用）。
            var miJump = new ToolStripMenuItem("跳到表格对应行");
            if (_ownerRef == null) miJump.Enabled = false;
            miJump.Click += (s, e) => { this.DialogResult = DialogResult.Retry; this.Close(); };
            // T7：整组导出为带帧号文件名的 PNG（换图/补图时直接交给画师/替换工具）。
            var miExport = new ToolStripMenuItem("导出本组全部图片…");
            miExport.Click += (s, e) => ExportViaDialog();
            _menu.Items.Add(miCopy);
            _menu.Items.Add(miCopyAll);
            _menu.Items.Add(miJump);
            _menu.Items.Add(miExport);
            this.ContextMenuStrip = _menu; // 右键空白处也可复制全部

            this.FormClosed += (s, e) => // P2-13：关闭后释放全部缩略图克隆
            {
                foreach (Control c in _panel.Controls)
                    foreach (Control sub in c.Controls)
                    {
                        var pic = sub as PictureBox;
                        if (pic != null && pic.Image != null) { pic.Image.Dispose(); pic.Image = null; }
                    }
            };

            BuildCells(currentFrame);
        }

        // T7：菜单动作——选目录后整组导出，成功时弹窗告知数量与位置（零基础反馈）。
        private void ExportViaDialog()
        {
            using (var fb = new FolderBrowserDialog { Description = "选择导出目录（将保存 帧_帧号.png 共 " + _frames.Count + " 张）" })
            {
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                int n = ExportGroupImages(fb.SelectedPath);
                MessageBox.Show(this,
                    n > 0
                        ? "已导出 " + n + " 张图片到：\n" + fb.SelectedPath + "\n文件名＝帧_帧号.png，可直接拿去替换/补图。"
                        : "导出失败：该组没有可导出的图片（lib 中这些帧可能为空）。",
                    "导出组图", MessageBoxButtons.OK, n > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        // T7：把本组每帧导出为 帧_帧号.png，并写一份组帧清单.txt（文件名列表）。
        // 返回成功导出的张数；单帧失败只跳过不中断（空帧容错与预览一致）。
        internal int ExportGroupImages(string dir)
        {
            int ok = 0;
            try { Directory.CreateDirectory(dir); }
            catch { return 0; }
            foreach (int f in _frames)
            {
                try
                {
                    var img = _lib != null ? _lib.GetMImage(f) : null;
                    if (img == null || img.Image == null) continue;
                    using (var bmp = (Image)img.Image.Clone())
                        bmp.Save(Path.Combine(dir, "帧_" + f.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png"),
                                 System.Drawing.Imaging.ImageFormat.Png);
                    ok++;
                }
                catch { }
            }
            try
            {
                File.WriteAllLines(Path.Combine(dir, "组帧清单.txt"),
                    _frames.Select(f => "帧_" + f.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png"),
                    new UTF8Encoding(false));
            }
            catch { }
            return ok;
        }

        private void BuildCells(int currentFrame)
        {
            foreach (var f in _frames)
            {
                var cell = new Panel { Width = 110, Height = 110, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4), Tag = f.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                var pic = new PictureBox { Dock = DockStyle.Top, Height = 78, SizeMode = PictureBoxSizeMode.Zoom };
                try
                {
                    var img = _lib.GetMImage(f);
                    if (img != null) pic.Image = (Image)img.Image.Clone();
                }
                catch { }
                bool isCurrent = f == currentFrame;
                var lab = new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 22,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "帧 " + f + (isCurrent ? "（当前）" : ""),
                    BackColor = isCurrent ? Color.Gold : SystemColors.Control,
                    Tag = f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                cell.Controls.Add(pic);
                cell.Controls.Add(lab);
                cell.ContextMenuStrip = _menu;
                _panel.Controls.Add(cell);
            }
        }
    }
}
