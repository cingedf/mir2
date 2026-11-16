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
                    int segs = 0, refs = 0, vars = 0, issues = 0;
                    foreach (var f in files)
                    {
                        var r = ScriptChecker.CheckFile(f, cmds);
                        segs += r.Segments; refs += r.References; vars += r.Variables;
                        issues += r.Issues.Count;
                    }
                    bool ok = files.Length >= 100 && segs > 0 && cmds.Count >= 50;
                    Console.WriteLine(ok
                        ? $"SCRIPTS OK: files={files.Length} commands={cmds.Count} segments={segs} refs={refs} vars={vars} issues={issues}"
                        : $"SCRIPTS FAIL: files={files.Length} commands={cmds.Count} segments={segs}");
                    return ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SCRIPTS FAIL: " + ex.Message);
                    return 1;
                }
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
            return 0;
        }
    }
}
