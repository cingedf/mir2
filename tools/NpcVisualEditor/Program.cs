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
                var envir = Server.MirEnvir.Envir.Edit;
                bool ok = envir.LoadDB();
                Console.WriteLine(ok
                    ? $"SMOKE OK: maps={envir.MapInfoList.Count} npcs={envir.NPCInfoList.Count} items={envir.ItemInfoList.Count} monsters={envir.MonsterInfoList.Count}"
                    : "SMOKE FAIL: LoadDB returned false");
                return ok ? 0 : 1;
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

            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
            return 0;
        }
    }
}
