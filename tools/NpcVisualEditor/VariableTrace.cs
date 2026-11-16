using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NpcVisualEditor
{
    /// <summary>
    /// 变量引用链追踪（纯逻辑，只读）。跨全部脚本建立变量（单字母+单数字，如 A1/T0）
    /// 的赋值/引用/显示反向索引，支持点选变量列出所有出现位置并跳转定位。
    /// Crystal 变量体系：单字母+单数字(player.NPCVar)，显示用 &lt;$OUTPUT(A1)&gt;。
    /// </summary>
    public static class VariableTrace
    {
        public enum UseKind { Assign, Reference, Display }

        public class Occurrence
        {
            public string File, Section, LineText, Variable;
            public int Line;
            public UseKind Kind;
            public string KindText => Kind == UseKind.Assign ? "赋值" : Kind == UseKind.Display ? "显示" : "引用";
        }

        public class VarInfo
        {
            public string Name;
            public List<Occurrence> Occurrences = new();
            public int Assigns => Occurrences.Count(o => o.Kind == UseKind.Assign);
            public int Refs => Occurrences.Count(o => o.Kind == UseKind.Reference);
            public int Displays => Occurrences.Count(o => o.Kind == UseKind.Display);
        }

        public class Index
        {
            public Dictionary<string, VarInfo> Vars =
                new(StringComparer.OrdinalIgnoreCase);
            public int Files;
        }

        // 变量 token：单字母+单数字，带边界（避免 A10 误匹配成 A1）
        private static readonly Regex RxVar =
            new(@"(?<![A-Za-z0-9])([A-Za-z][0-9])(?![A-Za-z0-9])", RegexOptions.Compiled);
        // 赋值命令首参：MOV/CALC/INC/DEC/SUM/CHECKCALC/LOADVALUE/SAVEVALUE
        private static readonly Regex RxAssign =
            new(@"^\s*(MOV|CALC|INC|DEC|SUM|CHECKCALC|LOADVALUE|SAVEVALUE)\s+([A-Za-z][0-9])(?:\s|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // 显示：<$OUTPUT(A1)> / <$STR(A1)> 等
        private static readonly Regex RxDisplay =
            new(@"<\$[A-Za-z_]+\(\s*([A-Za-z][0-9])\s*\)>", RegexOptions.Compiled);
        private static readonly Regex RxSegment = new(@"^\s*\[@([^\]]+)\]", RegexOptions.Compiled);

        public static Index Build(IEnumerable<string> files)
        {
            var idx = new Index();
            foreach (var file in files)
            {
                idx.Files++;
                string section = "";
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int ln = i + 1;
                    var sm = RxSegment.Match(line);
                    if (sm.Success) { section = sm.Groups[1].Value.Trim(); continue; }

                    // 赋值：命令首参
                    var am = RxAssign.Match(line);
                    string assignedVar = am.Success ? am.Groups[2].Value : null;

                    // 显示：<$OUTPUT(A1)>
                    var displayVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match dm in RxDisplay.Matches(line))
                        displayVars.Add(dm.Groups[1].Value);

                    // 所有变量 token
                    foreach (Match vm in RxVar.Matches(line))
                    {
                        string name = vm.Groups[1].Value;
                        UseKind kind;
                        if (assignedVar != null && name.Equals(assignedVar, StringComparison.OrdinalIgnoreCase))
                            kind = UseKind.Assign;
                        else if (displayVars.Contains(name))
                            kind = UseKind.Display;
                        else
                            kind = UseKind.Reference;

                        if (!idx.Vars.TryGetValue(name, out var info))
                            idx.Vars[name] = info = new VarInfo { Name = name };
                        info.Occurrences.Add(new Occurrence
                        {
                            File = file, Section = section, Line = ln, LineText = line.Trim(),
                            Variable = name, Kind = kind
                        });
                    }
                }
            }
            return idx;
        }

        public static IEnumerable<string> ScanFiles(string serverDir)
        {
            var dirs = new[] {
                Path.Combine(serverDir, "Envir", "NPCs"),
                Path.Combine(serverDir, "SystemScripts")
            };
            foreach (var d in dirs)
                if (Directory.Exists(d))
                    foreach (var f in Directory.GetFiles(d, "*.txt", SearchOption.AllDirectories))
                        yield return f;
        }
    }
}
