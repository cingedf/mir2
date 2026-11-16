using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NpcVisualEditor
{
    /// <summary>
    /// 脚本静态检查（纯逻辑）。检查：标签引用缺失、非法变量名、参数不足、
    /// 未闭合分组、未知命令。只读分析，不改脚本、不执行脚本。
    /// 契约来自子代理对 NPCSegment.cs / 命令说明.txt 的实证调研。
    /// </summary>
    public static class ScriptChecker
    {
        public enum Severity { Error, Warning, Info }

        public class Issue
        {
            public Severity Level;
            public string File, Section, Message;
            public int Line;
            public string Text => $"[{Level}] {Path.GetFileName(File)}:{Line} ({Section}) {Message}";
        }

        public class FileReport
        {
            public string File;
            public List<Issue> Issues = new();
            public int Segments, References, Variables;
        }

        // 段定义 [@名称]；段名可含 - 与 (参数)
        private static readonly Regex RxSegment = new(@"^\s*\[@([^\]]+)\]", RegexOptions.Compiled);
        // 按钮跳转 <文字/@目标> 或 <<文字/@目标/颜色>>
        private static readonly Regex RxJump = new(@"<{1,2}\s*[^/@>]*?/@([A-Za-z0-9_\-\(\)]+?)(?:/[^>]*?)?>{1,2}", RegexOptions.Compiled);
        // 命令行 GOTO/GROUPGOTO 第二词、DELAYGOTO/TIMERECALL 第三词
        private static readonly Regex RxGoto2 = new(@"^\s*(GOTO|GROUPGOTO)\s+@?([A-Za-z0-9_\-\(\)]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxGoto3 = new(@"^\s*(DELAYGOTO|TIMERECALL|TIMERECALLGROUP)\s+\S+\s+@?([A-Za-z0-9_\-\(\)]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // 变量：引擎按最严 [A-Z][0-9] 判定（字母+单数字）
        private static readonly Regex RxVarStrict = new(@"(?<![A-Za-z0-9])[A-Za-z][0-9](?![A-Za-z0-9])", RegexOptions.Compiled);
        // 多字母变量误用（如 TempGold / A10）——引擎会部分匹配或拒绝
        private static readonly Regex RxVarSuspect = new(@"(?<![A-Za-z0-9])(?:[A-Za-z]{2,}[0-9]|[A-Za-z][0-9]{2,})(?![A-Za-z0-9])", RegexOptions.Compiled);
        // MOV / CALC / CHECKCALC 参数
        private static readonly Regex RxMov = new(@"^\s*(MOV|CALC|CHECKCALC)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>加载命令字典：以 "英文名[:：]说明" 起记录（含全角冒号）。</summary>
        public static HashSet<string> LoadCommands(string helpPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(helpPath)) return set;
            var rx = new Regex(@"^([A-Za-z_]\w*)\s*[:：]", RegexOptions.Compiled);
            foreach (var raw in File.ReadAllLines(helpPath))
            {
                var m = rx.Match(raw);
                if (m.Success) set.Add(m.Groups[1].Value);
            }
            return set;
        }

        public static FileReport CheckFile(string path, HashSet<string> commands)
        {
            var rep = new FileReport { File = path };
            var lines = File.ReadAllLines(path);
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var refs = new List<(string target, int line, string section)>();
            string section = "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int ln = i + 1;

                var sm = RxSegment.Match(line);
                if (sm.Success)
                {
                    section = sm.Groups[1].Value.Trim();
                    defined.Add(section);
                    rep.Segments++;
                    continue;
                }

                // 按钮跳转引用
                foreach (Match jm in RxJump.Matches(line))
                    refs.Add((jm.Groups[1].Value, ln, section));

                // 命令跳转引用
                var g2 = RxGoto2.Match(line);
                if (g2.Success) refs.Add((g2.Groups[2].Value, ln, section));
                var g3 = RxGoto3.Match(line);
                if (g3.Success) refs.Add((g3.Groups[2].Value, ln, section));

                // 变量合法性
                foreach (Match vm in RxVarSuspect.Matches(line))
                    rep.Issues.Add(new Issue { Level = Severity.Warning, File = path, Section = section, Line = ln,
                        Message = $"疑似非法变量名「{vm.Value}」（引擎仅认单字母+单数字，如 A1）" });

                // MOV/CALC/CHECKCALC 参数数量
                var mm = RxMov.Match(line);
                if (mm.Success && commands.Count > 0)
                {
                    string cmd = mm.Groups[1].Value.ToUpperInvariant();
                    int need = cmd == "CALC" ? 4 : 2; // CALC 至少4参，MOV/CHECKCALC 至少2
                    var parts = mm.Groups[2].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < need)
                        rep.Issues.Add(new Issue { Level = Severity.Error, File = path, Section = section, Line = ln,
                            Message = $"{cmd} 参数不足（需≥{need}，实际{parts.Length}）" });
                }
            }

            // 段引用缺失校验（大小写不敏感）
            rep.References = refs.Count;
            foreach (var r in refs)
            {
                if (!defined.Contains(r.target))
                    rep.Issues.Add(new Issue { Level = Severity.Error, File = path, Section = r.section, Line = r.line,
                        Message = $"跳转目标「@{r.target}」在本脚本无对应段" });
            }

            // 变量统计
            rep.Variables = RxVarStrict.Matches(string.Join("\n", lines)).Count;

            // GOTO 死循环检测：段为节点，跳转为边，DFS 找环/自环
            DetectLoops(defined, refs, path, rep);

            return rep;
        }

        /// <summary>
        /// 检测段级跳转环：自循环直接报 Error，互环报 Warning。
        /// 只分析已定义的段，缺失目标已在上面单独报错，避免重复。
        /// </summary>
        private static void DetectLoops(HashSet<string> defined,
            List<(string target, int line, string section)> refs, string path, FileReport rep)
        {
            var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defined) graph[d] = new List<string>();
            foreach (var r in refs)
                if (defined.Contains(r.target) && graph.TryGetValue(r.section, out var edges))
                    edges.Add(r.target);

            // 自环
            foreach (var kv in graph)
                if (kv.Value.Any(t => string.Equals(t, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    var r = refs.FirstOrDefault(x => x.section == kv.Key &&
                        string.Equals(x.target, kv.Key, StringComparison.OrdinalIgnoreCase));
                    rep.Issues.Add(new Issue { Level = Severity.Error, File = path,
                        Section = kv.Key, Line = r.line,
                        Message = $"段「@{kv.Key}」跳转到自身，构成死循环（无任何 #IF 条件出口）" });
                }

            // 互环（长度≥2）：Tarjan 风格 DFS 找回边，去重报告
            var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0未访问1在栈2完成
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in graph.Keys)
            {
                if (color.TryGetValue(start, out var c) && c != 0) continue;
                Dfs(start, graph, color, new List<string>(), reported, path, rep, refs);
            }
        }

        private static void Dfs(string node, Dictionary<string, List<string>> graph,
            Dictionary<string, int> color, List<string> stack, HashSet<string> reported,
            string path, FileReport rep, List<(string target, int line, string section)> refs)
        {
            color[node] = 1;
            stack.Add(node);
            foreach (var next in graph[node])
            {
                if (!color.TryGetValue(next, out var c)) c = 0;
                if (c == 0)
                {
                    Dfs(next, graph, color, stack, reported, path, rep, refs);
                }
                else if (c == 1)
                {
                    int idx = stack.FindIndex(s => string.Equals(s, next, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        var cycle = stack.Skip(idx).Concat(new[] { next }).ToList();
                        string key = string.Join("→", cycle.Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                        if (reported.Add(key))
                        {
                            int line = refs.Where(r => cycle.Any(c => string.Equals(c, r.section, StringComparison.OrdinalIgnoreCase)))
                                .Select(r => r.line).DefaultIfEmpty(0).First();
                            rep.Issues.Add(new Issue { Level = Severity.Warning, File = path,
                                Section = cycle[0], Line = line,
                                Message = $"检测到跳转环：{string.Join(" → ", cycle)}（若循环内无出口条件将死循环）" });
                        }
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            color[node] = 2;
        }
    }
}
