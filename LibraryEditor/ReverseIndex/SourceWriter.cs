using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 精确源码 token 写回器（规范 §10.2 / §11.1-2 / 缺陷⑩）。
    ///
    /// 关键约束：
    /// - 按 Reference.TokenStart（UTF-16 char 偏移，与 Roslyn TextSpan.Start 同源）切片替换，禁止字节偏移。
    /// - 同一文件内从右到左替换，避免前一个替换改变后面 token 的偏移（同行多 token 必守）。
    /// - 保留 UTF-8 BOM 与 CRLF：只改数字字符，不触碰换行符；BOM 手工拼回字节。
    /// - 替换前校验 token 文本等于 BaseValue，防止源码已变导致错位改错。
    /// - 只写白名单（ScanRoot 下 .cs）。
    /// </summary>
    public sealed class SourceWriter
    {
        private readonly ReverseIndexConfig _cfg;

        public SourceWriter(ReverseIndexConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// 写回一批引用（仅 Target=Source 且 Changed 的项）。返回逐文件结果。
        /// 注意：调用方须在调用前已完成客户端进程门禁（规范 §10.4）。
        /// </summary>
        public WriteResult Write(IEnumerable<Reference> refs)
        {
            var result = new WriteResult();
            var byFile = new Dictionary<string, List<Reference>>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in refs ?? Enumerable.Empty<Reference>())
            {
                if (r == null || r.Target != "Source" || !r.Changed) continue;
                List<Reference> list;
                if (!byFile.TryGetValue(r.FilePath, out list))
                    byFile[r.FilePath] = list = new List<Reference>();
                list.Add(r);
            }

            foreach (var kv in byFile)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(Path.Combine(_cfg.RepoRoot, kv.Key.Replace('/', '\\')));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"路径解析失败 {kv.Key}: {ex.Message}");
                    continue;
                }

                string scanRoot = Path.GetFullPath(_cfg.ScanRoot);
                if (!full.StartsWith(scanRoot, StringComparison.OrdinalIgnoreCase)
                    || !full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"拒绝写入白名单外路径: {full}（仅允许 {scanRoot} 下 .cs）");
                    continue;
                }

                try
                {
                    int n = WriteFile(full, kv.Value, result);
                    result.FilesModified.Add(full);
                    result.TokensReplaced += n;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"写回失败 {full}: {ex.Message}");
                }
            }

            return result;
        }

        private static int WriteFile(string full, List<Reference> list, WriteResult result)
        {
            byte[] raw = File.ReadAllBytes(full);
            bool hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            int dataOffset = hasBom ? 3 : 0;
            // 用无 BOM 的 UTF-8 解码出纯文本；该 string 的 char 索引与 Roslyn ParseText 的 TextSpan 同源。
            string text = new UTF8Encoding(false).GetString(raw, dataOffset, raw.Length - dataOffset);

            // 全文件按 TokenStart 降序（从右到左）替换：左侧偏移不受影响。
            var ordered = list.OrderByDescending(r => r.TokenStart).ToList();
            int replaced = 0;

            foreach (var r in ordered)
            {
                if (r.TokenStart < 0 || r.TokenLength <= 0 || (long)r.TokenStart + r.TokenLength > text.Length)
                {
                    result.Errors.Add($"token 越界 [{r.Id}]: start={r.TokenStart}, len={r.TokenLength}, textLen={text.Length}");
                    continue;
                }

                string oldText = text.Substring(r.TokenStart, r.TokenLength);
                int oldVal;
                if (!int.TryParse(oldText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out oldVal))
                {
                    result.Errors.Add($"token 非数字 [{r.Id}]: '{oldText}'");
                    continue;
                }
                if (oldVal != r.BaseValue)
                {
                    result.Errors.Add($"token 值不符 [{r.Id}]: 期望 {r.BaseValue}，实际 {oldVal}（源码可能已被外部改动，本次跳过该项）");
                    continue;
                }

                string newText = r.EditedValue.ToString(CultureInfo.InvariantCulture);
                text = text.Remove(r.TokenStart, r.TokenLength).Insert(r.TokenStart, newText);
                replaced++;
            }

            if (replaced > 0)
            {
                byte[] body = new UTF8Encoding(false).GetBytes(text);
                byte[] outBytes;
                if (hasBom)
                {
                    outBytes = new byte[3 + body.Length];
                    outBytes[0] = 0xEF; outBytes[1] = 0xBB; outBytes[2] = 0xBF;
                    Array.Copy(body, 0, outBytes, 3, body.Length);
                }
                else
                {
                    outBytes = body;
                }
                // 原子写：临时文件 + 替换，避免写一半崩溃损坏源码。
                string tmp = full + ".ri_tmp";
                File.WriteAllBytes(tmp, outBytes);
                File.Replace(tmp, full, null);
            }

            return replaced;
        }
    }

    public sealed class WriteResult
    {
        public readonly List<string> FilesModified = new List<string>();
        public int TokensReplaced;
        public readonly List<string> Errors = new List<string>();
        public bool Ok { get { return Errors.Count == 0; } }
    }
}
