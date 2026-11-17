using System;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // 无头自检：--smoke <服务端目录> → 加载 MirDB 并打印统计，验证数据链路
            if (args.Length >= 2 && args[0] == "--smoke")
            {
                Environment.CurrentDirectory = args[1];
                // 引擎 LoadDB 在缺库时会写一份空库，自检必须先确认目标库存在
                if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                {
                    Console.WriteLine("SMOKE FAIL: 未找到 Server.MirDB");
                    return 1;
                }
                try
                {
                    var envir = Server.MirEnvir.Envir.Edit;
                    bool ok = envir.LoadDB();
                    Console.WriteLine(ok
                        ? $"SMOKE OK: maps={envir.MapInfoList.Count} npcs={envir.NPCInfoList.Count} items={envir.ItemInfoList.Count} monsters={envir.MonsterInfoList.Count}"
                        : "SMOKE FAIL: LoadDB returned false");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SMOKE FAIL: {ex.Message}");
                    return 1;
                }
            }

            // 浏览器属性表包装探针：--probe-browser <服务端目录>
            // 遍历全部物品/怪物，逐个构造只读包装并赋给 PropertyGrid，精确定位 SelectedObjects NRE。
            if (args.Length >= 2 && args[0] == "--probe-browser")
            {
                Environment.CurrentDirectory = args[1];
                if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                {
                    Console.WriteLine("PROBE FAIL: 未找到 Server.MirDB");
                    return 1;
                }
                var env = Server.MirEnvir.Envir.Edit;
                if (!env.LoadDB()) { Console.WriteLine("PROBE FAIL: LoadDB 失败"); return 1; }
                using var host = new Form();
                var pg = new PropertyGrid();
                host.Controls.Add(pg);
                host.CreateControl();
                int fail = 0;
                foreach (var it in env.ItemInfoList)
                {
                    try { pg.SelectedObject = new ItemInfoViewWrapper(it); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"PROBE ITEM {it.Index} {it.Name}: {ex.GetType().Name}: {ex.Message}");
                        if (++fail >= 5) break;
                    }
                }
                if (fail < 5)
                    foreach (var m in env.MonsterInfoList)
                    {
                        try { pg.SelectedObject = new ReadOnlyObjectWrapper(m); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"PROBE MON {m.Index} {m.Name}: {ex.GetType().Name}: {ex.Message}");
                            if (++fail >= 5) break;
                        }
                    }
                Console.WriteLine($"PROBE DONE fail={fail}");
                return fail == 0 ? 0 : 1;
            }

            // 客户端配置解析自检：--verify-client-config <客户端目录>
            if (args.Length >= 2 && args[0] == "--verify-client-config")
            {
                string dir = args[1];
                try
                {
                    var cfg = new IniFile(System.IO.Path.Combine(dir, "Mir2Config.ini"));
                    var keys = new IniFile(System.IO.Path.Combine(dir, "KeyBinds.ini"));
                    int cfgKeys = cfg.Sections.Sum(s => cfg.GetKeys(s).Length);
                    int keySections = keys.Sections.Count(s =>
                        keys.GetKeys(s).Contains("RequireKey", StringComparer.OrdinalIgnoreCase));

                    string locDir = System.IO.Path.Combine(dir, "Localization");
                    string zh = System.IO.Path.Combine(locDir, "Chinese.json");
                    int locCount = 0;
                    if (System.IO.File.Exists(zh))
                    {
                        var doc = System.Text.Json.JsonSerializer.Deserialize<
                            System.Collections.Generic.Dictionary<string,
                                System.Collections.Generic.Dictionary<string, string>>>(
                            System.IO.File.ReadAllText(zh));
                        locCount = doc?.Values.Sum(d => d.Count) ?? 0;
                    }

                    bool ok = cfg.Sections.Count >= 5 && cfgKeys >= 30 && keySections >= 50 && locCount >= 100;
                    Console.WriteLine(ok
                        ? $"CONFIG OK: Mir2Config sections={cfg.Sections.Count} keys={cfgKeys}, " +
                          $"KeyBinds bindSections={keySections}, Localization entries={locCount}"
                        : $"CONFIG FAIL: Mir2Config sections={cfg.Sections.Count} keys={cfgKeys}, " +
                          $"KeyBinds bindSections={keySections}, Localization entries={locCount}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CONFIG FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 掉落表解析自检：--verify-drops <服务端目录>（只读解析 Drops 目录全部文件）
            if (args.Length >= 2 && args[0] == "--verify-drops")
            {
                string dir = args[1];
                try
                {
                    string dropDir = System.IO.Path.Combine(dir, "Envir", "Drops");
                    if (!System.IO.Directory.Exists(dropDir))
                    {
                        Console.WriteLine("DROPS FAIL: 未找到 " + dropDir);
                        return 1;
                    }
                    var files = System.IO.Directory.GetFiles(dropDir, "*.txt");
                    int totalEntries = 0, totalIssues = 0, parsedFiles = 0;
                    foreach (var f in files)
                    {
                        var r = DropAnalyzer.ParseFile(f, null);
                        parsedFiles++;
                        totalEntries += DropAnalyzer.Flatten(r.Entries).Count();
                        totalIssues += r.Issues.Count;
                    }
                    bool ok = parsedFiles >= 100 && totalEntries > 0;
                    Console.WriteLine(ok
                        ? $"DROPS OK: files={parsedFiles} entries={totalEntries} parseIssues={totalIssues}"
                        : $"DROPS FAIL: files={parsedFiles} entries={totalEntries}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DROPS FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 脚本检查自检：--verify-scripts <服务端目录>（只读扫描 NPC 脚本）
            if (args.Length >= 2 && args[0] == "--verify-scripts")
            {
                string dir = args[1];
                try
                {
                    string npcDir = System.IO.Path.Combine(dir, "Envir", "NPCs");
                    if (!System.IO.Directory.Exists(npcDir))
                    {
                        Console.WriteLine("SCRIPTS FAIL: 未找到 " + npcDir);
                        return 1;
                    }
                    var cmds = ScriptChecker.LoadCommands(System.IO.Path.Combine(npcDir, "NPC命令说明.txt"));
                    var files = System.IO.Directory.GetFiles(npcDir, "*.txt", System.IO.SearchOption.AllDirectories);
                    int segs = 0, refs = 0, vars = 0, issues = 0, loops = 0;
                    foreach (var f in files)
                    {
                        var r = ScriptChecker.CheckFile(f, cmds);
                        segs += r.Segments; refs += r.References; vars += r.Variables;
                        issues += r.Issues.Count;
                        loops += r.Issues.Count(i => i.Message.Contains("跳转环") || i.Message.Contains("死循环"));
                    }
                    bool ok = files.Length >= 100 && segs > 0 && cmds.Count >= 50;
                    Console.WriteLine(ok
                        ? $"SCRIPTS OK: files={files.Length} commands={cmds.Count} segments={segs} refs={refs} vars={vars} issues={issues} loops={loops}"
                        : $"SCRIPTS FAIL: files={files.Length} commands={cmds.Count} segments={segs}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SCRIPTS FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 变量引用链自检：--verify-vars <服务端目录>（只读扫描全部脚本建反向索引）
            if (args.Length >= 2 && args[0] == "--verify-vars")
            {
                string dir = args[1];
                try
                {
                    var files = VariableTrace.ScanFiles(dir).ToList();
                    var idx = VariableTrace.Build(files);
                    int occurrences = idx.Vars.Values.Sum(v => v.Occurrences.Count);
                    bool ok = files.Count >= 100 && idx.Vars.Count > 0;
                    Console.WriteLine(ok
                        ? $"VARS OK: files={files.Count} variables={idx.Vars.Count} occurrences={occurrences}"
                        : $"VARS FAIL: files={files.Count} variables={idx.Vars.Count}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("VARS FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 地图爆率链路自检：--verify-map-drops <服务端目录>（只读，验证地图↔怪物↔掉落文件链路）
            if (args.Length >= 2 && args[0] == "--verify-map-drops")
            {
                string dir = args[1];
                Environment.CurrentDirectory = dir;
                try
                {
                    if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                    {
                        Console.WriteLine("MAPDROPS FAIL: 未找到 Server.MirDB");
                        return 1;
                    }
                    var envir = Server.MirEnvir.Envir.Edit;
                    if (!envir.LoadDB())
                    {
                        Console.WriteLine("MAPDROPS FAIL: LoadDB returned false");
                        return 1;
                    }
                    string dropDir = System.IO.Path.Combine(dir, "Envir", "Drops");
                    int mapsWithRespawn = 0, mapMonsterLinks = 0, monstersWithDrop = 0;
                    var seenMonsters = new System.Collections.Generic.HashSet<int>();
                    foreach (var m in envir.MapInfoList)
                    {
                        if (m.Respawns == null || m.Respawns.Count == 0) continue;
                        mapsWithRespawn++;
                        foreach (var rp in m.Respawns)
                        {
                            mapMonsterLinks++;
                            if (!seenMonsters.Add(rp.MonsterIndex)) continue;
                            var mi = envir.MonsterInfoList.FirstOrDefault(d => d.Index == rp.MonsterIndex);
                            if (mi == null) continue;
                            string name = string.IsNullOrEmpty(mi.DropPath) ? mi.Name : mi.DropPath;
                            if (File.Exists(System.IO.Path.Combine(dropDir, name + ".txt"))) monstersWithDrop++;
                        }
                    }
                    bool ok = mapsWithRespawn > 0 && mapMonsterLinks > 0 && monstersWithDrop > 0;
                    Console.WriteLine(ok
                        ? $"MAPDROPS OK: mapsWithRespawn={mapsWithRespawn} links={mapMonsterLinks} monstersWithDrop={monstersWithDrop}"
                        : $"MAPDROPS FAIL: mapsWithRespawn={mapsWithRespawn} links={mapMonsterLinks} monstersWithDrop={monstersWithDrop}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MAPDROPS FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 关系图谱自检：--verify-graph <服务端目录>（只读，构建引用图并统计节点/边）
            if (args.Length >= 2 && args[0] == "--verify-graph")
            {
                string dir = args[1];
                Environment.CurrentDirectory = dir;
                try
                {
                    if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                    {
                        Console.WriteLine("GRAPH FAIL: 未找到 Server.MirDB");
                        return 1;
                    }
                    var envir = Server.MirEnvir.Envir.Edit;
                    if (!envir.LoadDB())
                    {
                        Console.WriteLine("GRAPH FAIL: LoadDB returned false");
                        return 1;
                    }
                    var graph = ReferenceGraph.Build(envir, dir);
                    int textEdges = graph.Edges.Count(e => e.IsText);
                    bool ok = graph.Nodes.Count > 100 && graph.Edges.Count > 100;
                    Console.WriteLine(ok
                        ? $"GRAPH OK: nodes={graph.Nodes.Count} edges={graph.Edges.Count} textEdges={textEdges}"
                        : $"GRAPH FAIL: nodes={graph.Nodes.Count} edges={graph.Edges.Count}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("GRAPH FAIL: " + ex.Message);
                    return 1;
                }
            }

            // 运行时视觉核验：--capture <服务端目录> <客户端目录> <输出目录>
            // 程序化实例化各窗体 → Show() 触发真实数据加载 → DrawToBitmap 渲染成 PNG，
            // 供逐张查看挤压/截断/遮挡/缺图等视觉问题（绿色测试分支，不影响正常启动）
            if (args.Length >= 4 && (args[0] == "--capture" || args[0] == "--layout-audit" || args[0] == "--probe-interact"))
            {
                bool auditMode = args[0] == "--layout-audit";
                bool interactMode = args[0] == "--probe-interact";
                string serverDir = args[1], clientDir = args[2], outDir = args[3];
                Environment.CurrentDirectory = serverDir;
                Directory.CreateDirectory(outDir);
                if (!File.Exists(Server.MirEnvir.Envir.DatabasePath))
                {
                    Console.WriteLine("CAPTURE FAIL: 未找到 Server.MirDB");
                    return 1;
                }
                var envir = Server.MirEnvir.Envir.Edit;
                if (!envir.LoadDB()) { Console.WriteLine("CAPTURE FAIL: LoadDB 失败"); return 1; }
                int okCount = 0, failCount = 0, layoutIssues = 0, interactionFails = 0;

                // 布局溢出审计：递归遍历控件树，量化检测「文本溢出 / 控件越界 / 文本控件重叠」
                // ——客观回应「输入框和文字挤压 / 显示不全 / 控件遮挡文字」类视觉问题
                void AuditLayout(Control root, string formName)
                {
                    string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 22 ? s.Substring(0, 22) + "…" : s);
                    void Walk(Control parent, string path)
                    {
                        foreach (Control c in parent.Controls)
                        {
                            string here = string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name;
                            string full = path + "/" + here;

                            // ① 文本溢出：非 AutoSize 的文本类控件，测量单行文本宽 vs 实宽
                            if ((c is Label || c is Button || c is CheckBox || c is RadioButton)
                                && !c.AutoSize
                                && !(c is Label al && al.AutoEllipsis) && !(c is Button ab && ab.AutoEllipsis)
                                && !string.IsNullOrEmpty(c.Text) && c.Width > 0)
                            {
                                try
                                {
                                    // 多行文本按行测量取最大行宽（Label 会自动换行，不能整串 SingleLine 测量）
                                    int needW = 0;
                                    foreach (var line in c.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                                    {
                                        if (line.Length == 0) continue;
                                        var sz = TextRenderer.MeasureText(line, c.Font,
                                            new System.Drawing.Size(int.MaxValue, int.MaxValue),
                                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                                        needW = Math.Max(needW, sz.Width);
                                    }
                                    if (needW > c.Width + 6)
                                    {
                                        Console.WriteLine($"LAYOUT OVERFLOW: {formName}{full} 文本[{Trunc(c.Text)}] 需{needW}px 实宽{c.Width}px");
                                        layoutIssues++;
                                    }
                                }
                                catch { }
                            }

                            // ② 控件越界：非滚动/非自动布局容器内，控件 Bounds 超出父客户区
                            bool skip = (parent is ScrollableControl sc && sc.AutoScroll) || parent is FlowLayoutPanel || parent is TableLayoutPanel
                                        || c.Dock != DockStyle.None;
                            if (!skip && c.Visible && c.Width > 0)
                            {
                                var pr = parent.ClientRectangle;
                                var r = c.Bounds;
                                if (r.Right > pr.Width + 4 || r.Bottom > pr.Height + 4 || r.X < -4 || r.Y < -4)
                                {
                                    Console.WriteLine($"LAYOUT OUTOFBOUND: {formName}{full} {r} 父区{pr}");
                                    layoutIssues++;
                                }
                            }

                            Walk(c, full);
                        }
                    }
                    Walk(root, "");
                }

                // 程序化交互探针：对窗体内所有列表类控件逐项选中，触发真实的
                // SelectedIndexChanged / AfterSelect / CellClick 事件链，捕获「选中→详情加载」
                // 路径上才会暴露的崩溃（这类框架回调契约 bug 静态审计与纯渲染都无法发现）。
                // 每项交互后 DoEvents 让事件处理器完整执行；异常单独计数不中断整体。
                void InteractForm(Form f, string name)
                {
                    int before = interactionFails;
                    void Walk(Control parent, string path)
                    {
                        foreach (Control c in parent.Controls)
                        {
                            string here = string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name;
                            string full = path + "/" + here;
                            try
                            {
                                if (c is ListBox lb && lb.Items.Count > 0)
                                {
                                    for (int i = 0; i < Math.Min(lb.Items.Count, 6); i++)
                                    { lb.SelectedIndex = i; Application.DoEvents(); }
                                }
                                else if (c is ComboBox cb && cb.Items.Count > 0 && cb.DropDownStyle != ComboBoxStyle.DropDown)
                                {
                                    for (int i = 0; i < Math.Min(cb.Items.Count, 6); i++)
                                    { cb.SelectedIndex = i; Application.DoEvents(); }
                                }
                                else if (c is TreeView tv && tv.Nodes.Count > 0)
                                {
                                    foreach (TreeNode node in tv.Nodes)
                                    { tv.SelectedNode = node; Application.DoEvents(); break; }
                                }
                                else if (c is ListView lv && lv.Items.Count > 0)
                                {
                                    lv.Items[0].Selected = true; Application.DoEvents();
                                }
                                else if (c is DataGridView dg && dg.RowCount > 0)
                                {
                                    for (int r = 0; r < Math.Min(dg.RowCount, 3); r++)
                                    { dg.CurrentCell = dg.Rows[r].Cells[0]; Application.DoEvents(); }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"INTERACT FAIL: {name}{full} [{c.GetType().Name}] {ex.GetType().Name}: {ex.Message}");
                                interactionFails++;
                            }
                            Walk(c, full);
                        }
                    }
                    try { Walk(f, ""); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"INTERACT WALK FAIL: {name} {ex.GetType().Name}: {ex.Message}");
                        interactionFails++;
                    }
                    if (interactionFails == before) Console.WriteLine($"INTERACT OK: {name}");
                }

                void Shot(Form f, string name, Action<Form> before = null)
                {
                    try
                    {
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new System.Drawing.Point(0, 0);
                        f.Show();
                        Application.DoEvents();
                        before?.Invoke(f);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(120);
                        Application.DoEvents();
                        if (interactMode) { InteractForm(f, name); }
                        else
                        {
                            var rect = new System.Drawing.Rectangle(0, 0, Math.Max(1, f.Width), Math.Max(1, f.Height));
                            using var bmp = new System.Drawing.Bitmap(rect.Width, rect.Height);
                            f.DrawToBitmap(bmp, rect);
                            bmp.Save(Path.Combine(outDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                            Console.WriteLine($"CAPTURE OK: {name} {rect.Width}x{rect.Height}");
                        }
                        okCount++;
                        if (auditMode) AuditLayout(f, name);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"CAPTURE FAIL: {name} {ex.GetType().Name}: {ex.Message}");
                        failCount++;
                    }
                    finally { try { f.Close(); } catch { } try { f.Dispose(); } catch { } }
                }

                try
                {
                    var npc = envir.NPCInfoList.FirstOrDefault();
                    string npcScript = npc != null ? Path.Combine(serverDir, "Envir", "NPCs", npc.FileName + ".txt") : "";
                    string npcDir = Path.Combine(serverDir, "Envir", "NPCs");
                    string flowScript = Directory.Exists(npcDir)
                        ? Directory.GetFiles(npcDir, "*.txt", SearchOption.AllDirectories)
                            .FirstOrDefault(p => { try { return File.ReadAllText(p).Contains("[@"); } catch { return false; } }) ?? npcScript
                        : npcScript;

                    Shot(new LauncherForm(), "01-Launcher");
                    Shot(new MainForm(envir, serverDir, clientDir), "02-Main");
                    if (npc != null) Shot(new NpcEditForm(npc, envir, serverDir, false), "03-NpcEdit");
                    if (File.Exists(npcScript)) Shot(new ConversationForm(npc, npcScript, () => { }), "04-Conversation");
                    if (File.Exists(flowScript)) Shot(new DialogueFlowForm(flowScript, "预览NPC"), "05-DialogueFlow");
                    Shot(new WorkspaceManagerForm(serverDir), "06-WorkspaceManager");
                    Shot(new VariableTraceForm(serverDir), "07-VariableTrace");
                    Shot(new BrowserHostForm(envir, serverDir), "08-BrowserHost");
                    Shot(new ClientConfigForm(clientDir), "09-ClientConfig");
                    Shot(new VariableManagerForm(serverDir), "10-VariableManager");
                    var hubHost = new Form { Text = "工具中心（预览）", Size = new System.Drawing.Size(1000, 700) };
                    hubHost.Controls.Add(new ToolHubForm(serverDir, clientDir) { Dock = DockStyle.Fill });
                    Shot(hubHost, "11-ToolHub");
                    Shot(new MapDropViewForm(serverDir, envir), "12-MapDropView");
                    Shot(new ImagePickerForm(serverDir, "Items", -1), "13-ImagePicker");
                    Shot(new TemplateForm(serverDir, envir.MapInfoList), "14-Template");
                    Shot(new DropTreeForm(serverDir, envir), "15-DropTree");
                    Shot(new DropFormatForm(serverDir), "16-DropFormat");
                    Shot(new LocalizationAuditForm(serverDir, clientDir), "17-LocalizationAudit");
                    Shot(new ScriptCheckerForm(serverDir), "18-ScriptChecker");
                    Shot(new DropAnalysisForm(serverDir, envir), "19-DropAnalysis");
                    Shot(new SaveForm(serverDir), "20-Save");
                    Shot(new ReferenceGraphForm(serverDir, envir), "21-ReferenceGraph");
                    Shot(new MapRelationForm(envir), "22-MapRelation");
                    Shot(new CustomCommandForm(serverDir), "23-CustomCommand");
                    Shot(new DamageCalcForm(), "24-DamageCalc");
                    Shot(new PathNavigationForm(serverDir, envir), "25-PathNavigation");
                    Shot(new CommandWizardForm(serverDir), "26-CommandWizard");
                    Shot(new EffectBuffForm(serverDir), "27-EffectBuff");
                    Shot(new QuestFlowForm(envir.QuestInfoList, serverDir), "28-QuestFlow");
                    var fm = envir.MapInfoList.FirstOrDefault();
                    if (fm != null && fm.Respawns.Count > 0) Shot(new RespawnEditForm(fm.Respawns[0], envir), "29-RespawnEdit");
                    if (fm != null && fm.SafeZones.Count > 0) Shot(new SafeZoneEditForm(fm.SafeZones[0]), "30-SafeZoneEdit");
                    if (fm != null && fm.Movements.Count > 0) Shot(new MovementEditForm(fm.Movements[0], envir), "31-MovementEdit");
                    Shot(new TextInputForm("测试标题", "输入内容", "初始值"), "32-TextInput");
                    // 弹出式子对话框（补齐渲染核验，避免遗漏）
                    Shot(new NodeEditForm(new DialogueNodeRef
                    {
                        Name = "@main", Lines = new System.Collections.Generic.List<string>
                        { "#IF", "LEVEL >= 30", "#SAY", "欢迎来到传奇！", "#ACT", "GOTO @next" }
                    }), "33-NodeEdit");
                    Shot(new KeyValueInput("新增配置项"), "34-KeyValueInput");
                }
                catch (Exception ex) { Console.WriteLine("CAPTURE SETUP FAIL: " + ex.Message); }
                if (auditMode) Console.WriteLine($"LAYOUT AUDIT DONE: issues={layoutIssues}");
                if (interactMode)
                {
                    Console.WriteLine($"INTERACT DONE: forms={okCount} fails={interactionFails}");
                    return interactionFails == 0 ? 0 : 1;
                }
                Console.WriteLine($"CAPTURE DONE: ok={okCount} fail={failCount}");
                return failCount == 0 ? 0 : 1;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
            return 0;
        }
    }
}
