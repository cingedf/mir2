using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NpcVisualEditor
{
    /// <summary>
    /// 掉落表解析与有效爆率推导（纯逻辑，无 UI），供窗体与自检分支共用。
    /// 复刻引擎 DropInfo.AttemptDrop 概率口径：两次 int 截断 + rate&lt;1→1。
    /// </summary>
    public static class DropAnalyzer
    {
        public class Entry
        {
            public string Raw;
            public int Chance;
            public string ItemName;
            public uint Gold;
            public bool IsGold;
            public bool QuestRequired;
            public string GroupKind = "";
            public List<Entry> Children = new();
            public int Depth;
            public bool IsGroup => Children.Count > 0 || GroupKind.Length > 0;
        }

        public class Result
        {
            public string Name;
            public List<Entry> Entries = new();
            public List<string> Issues = new();
        }

        private static readonly Regex RxItem =
            new(@"^1/(\d+)\s+(\S+)(?:\s+(\d+|Q))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxInsert =
            new(@"^#INSERT\s+\[?([^\]\s]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> Builtins =
            new(StringComparer.OrdinalIgnoreCase) { "Gold" };

        /// <summary>解析单个掉落文件；itemNames 用于未知物品体检（可为 null）。</summary>
        public static Result ParseFile(string path, HashSet<string> itemNames)
        {
            var result = new Result { Name = Path.GetFileNameWithoutExtension(path) };
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines.All(l => string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith(";")))
                result.Issues.Add("空文件（无有效掉落）");

            var stack = new Stack<List<Entry>>();
            var groupStack = new Stack<Entry>();
            stack.Push(result.Entries);
            string dir = Path.GetDirectoryName(path);

            ParseLines(lines, dir, stack, groupStack, result.Issues, itemNames, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private static void ParseLines(IEnumerable<string> lines, string dir,
            Stack<List<Entry>> stack, Stack<Entry> groupStack, List<string> issues,
            HashSet<string> itemNames, HashSet<string> visiting)
        {
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;

                if (line == "}")
                {
                    if (stack.Count > 1) { stack.Pop(); groupStack.Pop(); }
                    else issues.Add("多余的 '}'");
                    continue;
                }

                if (line.StartsWith("GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    string kind = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
                    var grp = new Entry { Raw = line, GroupKind = kind, Depth = groupStack.Count };
                    stack.Peek().Add(grp);
                    if (line.Contains("{")) { groupStack.Push(grp); stack.Push(grp.Children); }
                    else issues.Add("GROUP 缺少 '{'");
                    continue;
                }

                var ins = RxInsert.Match(line);
                if (ins.Success)
                {
                    string sub = ins.Groups[1].Value;
                    string path = Path.Combine(dir, sub);
                    if (File.Exists(path))
                    {
                        if (!visiting.Add(sub)) { issues.Add($"#INSERT 循环引用: {sub}"); continue; }
                        ParseLines(File.ReadAllLines(path), Path.GetDirectoryName(path), stack, groupStack, issues, itemNames, visiting);
                        visiting.Remove(sub);
                    }
                    else issues.Add($"#INSERT 文件不存在: {sub}");
                    continue;
                }

                var m = RxItem.Match(line);
                if (!m.Success) { issues.Add($"无法解析: {line}"); continue; }
                if (!int.TryParse(m.Groups[1].Value, out int n) || n <= 0)
                {
                    issues.Add($"非法分母: {line}"); continue;
                }
                var e = new Entry { Raw = line, Chance = n, ItemName = m.Groups[2].Value, Depth = groupStack.Count };
                string third = m.Groups[3].Value;
                if (e.ItemName.Equals("Gold", StringComparison.OrdinalIgnoreCase))
                {
                    e.IsGold = true;
                    e.Gold = uint.TryParse(third, out var g) ? g : 0;
                }
                else
                {
                    e.QuestRequired = third.Equals("Q", StringComparison.OrdinalIgnoreCase);
                    if (itemNames != null && !itemNames.Contains(e.ItemName) && !Builtins.Contains(e.ItemName))
                        issues.Add($"未知物品: {e.ItemName}");
                }
                stack.Peek().Add(e);
            }
        }

        /// <summary>有效爆率 = 1/rate，复刻 AttemptDrop 两次 int 截断。Q 项恒为 0。</summary>
        public static double EffectiveRate(Entry e, double dropRate, int bonusPercent)
        {
            if (e.QuestRequired || e.IsGroup) return 0.0;
            int rate = (int)(e.Chance / dropRate);            // 截断1
            if (bonusPercent > 0) rate = (int)(rate / (1.0 + bonusPercent / 100.0)); // 截断2
            if (rate < 1) rate = 1;
            return 1.0 / rate;
        }

        /// <summary>遍历所有条目（含分组递归）。</summary>
        public static IEnumerable<Entry> Flatten(IEnumerable<Entry> entries)
        {
            foreach (var e in entries)
            {
                yield return e;
                if (e.Children.Count > 0)
                    foreach (var c in Flatten(e.Children)) yield return c;
            }
        }
    }
}
