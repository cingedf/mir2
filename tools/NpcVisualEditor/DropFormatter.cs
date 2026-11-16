using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NpcVisualEditor
{
    /// <summary>
    /// 爆率文件格式化（纯逻辑）。现网 Drops\*.txt 为扁平 "1/N 物品" + ";分类" 注释结构。
    /// 两种模式：
    ///  - LayoutOnly：逐行保真，仅统一行格式/缩进，保留注释与 #INSERT 原位（默认，风险最低）；
    ///  - Rebuild：基于 DropAnalyzer 解析结果树完整重排（需人工确认）。
    /// 编码自适应：BOM → UTF8-strict → GB18030 回退；写回时用原文件编码原样保留，避免损坏文件。
    /// </summary>
    public static class DropFormatter
    {
        private static readonly Regex RxItem =
            new(@"^\s*1/(\d+)\s+(\S+)(?:\s+(\d+|Q))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxCategory =
            new(@"^\s*;", RegexOptions.Compiled);

        public enum FormatMode { LayoutOnly, Rebuild }

        public class FormatResult
        {
            public string Original;
            public string Formatted;
            public int LinesBefore, LinesAfter;
            public List<string> Changes = new();
        }

        /// <summary>获取 GB18030 编码（注册 CodePages 提供程序）。</summary>
        public static Encoding GetGb18030()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030");
        }

        /// <summary>
        /// 编码探测：BOM → UTF8-strict → GB18030 回退。
        /// 返回 (encoding, hasBom)。部署区实测 Drops\鸡.txt 等为 UTF-8 无 BOM + CRLF，
        /// 早期误判为 GBK；自适应避免 UTF-8 中文被读成乱码或写回时被转码损坏。
        /// </summary>
        public static (Encoding enc, bool bom) DetectEncoding(string path)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return (new UTF8Encoding(true), true);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return (Encoding.Unicode, true);
            var strict = new UTF8Encoding(false, true);
            try { strict.GetString(b); return (new UTF8Encoding(false), false); }
            catch { return (GetGb18030(), false); }
        }

        /// <summary>按自适应编码读取（UTF8 无 BOM 时严格解码，非乱码）。</summary>
        public static string ReadText(string path) => File.ReadAllText(path, DetectEncoding(path).enc);

        /// <summary>写回时探测原文件编码并原样保留（BOM/换行风格一致）。</summary>
        public static void WriteText(string path, string text)
        {
            var (enc, bom) = File.Exists(path) ? DetectEncoding(path) : (new UTF8Encoding(false), false);
            string crlf = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            File.WriteAllText(path, crlf, enc);
        }

        /// <summary>仅排版：统一 "1/N 物品 [数量|Q]" 行格式，注释/空行/#INSERT 原样保留。</summary>
        public static FormatResult Format(string text, FormatMode mode)
        {
            var res = new FormatResult { Original = text };
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            res.LinesBefore = lines.Length;

            if (mode == FormatMode.LayoutOnly)
            {
                var outLines = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0)
                    {
                        outLines.Add("");
                        continue;
                    }
                    if (RxCategory.IsMatch(line) || trimmed.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase)
                        || trimmed == "{" || trimmed == "}")
                    {
                        outLines.Add(trimmed);   // 注释/指令/括号原样（去首尾空白）
                        continue;
                    }
                    var m = RxItem.Match(line);
                    if (m.Success)
                    {
                        string norm = $"1/{m.Groups[1].Value} {m.Groups[2].Value}";
                        if (m.Groups[3].Success) norm += " " + m.Groups[3].Value;
                        if (norm != line)
                            res.Changes.Add($"L{i + 1}: \"{line}\" → \"{norm}\"");
                        outLines.Add(norm);
                        continue;
                    }
                    // 无法识别的行（GROUP 头等）原样保留
                    outLines.Add(trimmed);
                }
                res.Formatted = string.Join("\n", outLines);
            }
            else
            {
                // Rebuild：用 DropAnalyzer 解析树重建（规整缩进 + 子项排序）
                var tmp = Path.Combine(Path.GetTempPath(), "dropfmt_" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    WriteText(tmp, text);
                    var parsed = DropAnalyzer.ParseFile(tmp, null);
                    var sb = new StringBuilder();
                    foreach (var e in parsed.Entries)
                        WriteEntry(e, sb, 0);
                    res.Formatted = sb.ToString();
                    res.Changes.Add("树重建：按解析树重排所有条目（请人工核对）");
                    foreach (var iss in parsed.Issues)
                        res.Changes.Add("解析告警: " + iss);
                }
                finally { try { File.Delete(tmp); } catch { } }
            }

            res.LinesAfter = res.Formatted.Split('\n').Length;
            return res;
        }

        private static void WriteEntry(DropAnalyzer.Entry e, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            if (e.IsGroup)
            {
                sb.AppendLine(indent + e.Raw.Trim());
                sb.AppendLine(indent + "{");
                foreach (var c in e.Children) WriteEntry(c, sb, depth + 1);
                sb.AppendLine(indent + "}");
            }
            else
            {
                string line = $"1/{e.Chance} {e.ItemName}";
                if (e.IsGold) line = $"1/{e.Chance} Gold {e.Gold}";
                else if (e.QuestRequired) line += " Q";
                sb.AppendLine(indent + line);
            }
        }
    }
}
