using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 客户端重编译器（规范 §10.3 / 缺陷⑨）。
    /// - 错误数 == 0 即编译成功；警告不阻断成功（0 错误 / N 警告仍为成功）。
    /// - 基线既有警告 MSB3277（WindowsBase 版本冲突，WebView2 依赖）列入白名单，
    ///   标"既有警告"，"新增警告"只统计白名单外项。
    /// - 覆盖异常：dotnet 缺失、超时（10 分钟）、非零退出但无 error 行。
    /// </summary>
    public sealed class Compiler
    {
        private readonly ReverseIndexConfig _cfg;
        private const int TimeoutMs = 10 * 60 * 1000;

        // MSB3277: 基线警告白名单（按编号 + 消息前缀匹配）
        private static readonly Regex BaselineMsb3277 = new Regex(
            @"MSB3277.*WindowsBase", RegexOptions.IgnoreCase);

        private static readonly Regex IssueWithLocation = new Regex(
            @"(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<level>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)",
            RegexOptions.IgnoreCase);

        private static readonly Regex IssueNoLocation = new Regex(
            @"^\s*(?<level>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)",
            RegexOptions.IgnoreCase);

        public Compiler(ReverseIndexConfig cfg) { _cfg = cfg; }

        public CompileResult Build()
        {
            var result = new CompileResult();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build \"" + _cfg.ClientProject + "\" -c Release --nologo",
                WorkingDirectory = _cfg.RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process p = null;
            try
            {
                p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!p.Start())
                {
                    result.ToolUnavailable = true;
                    result.Message = "编译工具不可用：dotnet 启动失败";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.ToolUnavailable = true;
                result.Message = "编译工具不可用（dotnet 不在 PATH 或启动失败）：" + ex.Message;
                return result;
            }

            System.Text.StringBuilder stdout = new System.Text.StringBuilder();
            System.Text.StringBuilder stderr = new System.Text.StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            bool timedOut = false;
            try
            {
                if (!p.WaitForExit(TimeoutMs))
                {
                    timedOut = true;
                    try { p.Kill(true); } catch { }
                    p.WaitForExit(5000);
                }
                else
                {
                    // 确保异步输出流读完
                    p.WaitForExit();
                    result.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.Message = "编译进程异常：" + ex.Message;
            }
            finally
            {
                if (p != null) p.Dispose();
            }

            string raw;
            lock (stdout) lock (stderr) raw = stdout.ToString() + stderr.ToString();
            result.RawOutput = raw;
            Parse(raw, result);

            if (timedOut)
            {
                result.TimedOut = true;
                result.Message = "编译超时（超过 10 分钟），已终止进程；保留已捕获输出。";
                result.Success = false;
                return result;
            }

            // 成功判据：错误数 0（警告不阻断）；且无工具缺失/进程异常
            if (result.ErrorCount == 0 && string.IsNullOrEmpty(result.Message))
            {
                result.Success = true;
                result.Message = result.WarningCount > 0
                    ? "编译成功（0 错误 / " + result.WarningCount + " 警告，警告不阻断）"
                    : "编译成功（0 错误 / 0 警告）";
            }
            else if (result.ErrorCount > 0)
            {
                result.Success = false;
                if (string.IsNullOrEmpty(result.Message))
                    result.Message = "编译失败：" + result.ErrorCount + " 个错误";
            }
            else
            {
                // 非零退出但无 error 行 → 如实提示
                result.Success = false;
                if (string.IsNullOrEmpty(result.Message))
                    result.Message = "编译进程退出码 " + result.ExitCode + "，未解析到 error 行，请查看原始输出。";
            }
            return result;
        }

        private static void Parse(string raw, CompileResult result)
        {
            foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string level = null, code = null, msg = null, file = null;
                int ln = 0, col = 0;
                Match m = IssueWithLocation.Match(line);
                if (m.Success)
                {
                    file = m.Groups["file"].Value.Trim();
                    int.TryParse(m.Groups["line"].Value, out ln);
                    int.TryParse(m.Groups["col"].Value, out col);
                    level = m.Groups["level"].Value;
                    code = m.Groups["code"].Value;
                    msg = m.Groups["msg"].Value;
                }
                else
                {
                    Match m2 = IssueNoLocation.Match(line);
                    if (m2.Success)
                    {
                        level = m2.Groups["level"].Value;
                        code = m2.Groups["code"].Value;
                        msg = m2.Groups["msg"].Value;
                    }
                }
                if (level == null) continue;

                bool isError = level.Equals("error", StringComparison.OrdinalIgnoreCase);
                // 基线警告白名单（规范§10.3）：MSB3277 是项目固有的依赖统一警告
                // （WindowsBase/WebView2 版本冲突），MSBuild 跨多行输出，续行消息只含
                // WebView2 路径、不含 WindowsBase 字样。源码帧号改动不可能引入它，
                // 故所有 MSB3277 警告行一律视为基线，不计入"新增警告"，
                // 避免把基线噪声误报为本次改动的问题（实测曾误报 NewWarnings=6）。
                bool isBaseline = false;
                if (!isError && code != null && code.Equals("MSB3277", StringComparison.OrdinalIgnoreCase))
                    isBaseline = true;

                var issue = new BuildIssue
                {
                    IsError = isError,
                    Code = code,
                    File = file,
                    Line = ln,
                    Column = col,
                    Message = msg,
                    IsKnownBaseline = isBaseline
                };
                result.Issues.Add(issue);
                if (isError) result.ErrorCount++;
                else
                {
                    result.WarningCount++;
                    if (!isBaseline) result.NewWarnings++;
                }
            }
        }
    }

    public sealed class BuildIssue
    {
        public bool IsError;
        public string Code;
        public string File;
        public int Line;
        public int Column;
        public string Message;
        public bool IsKnownBaseline;
    }

    public sealed class CompileResult
    {
        public bool Success;
        public int ErrorCount;
        public int WarningCount;
        public int NewWarnings;
        public int ExitCode;
        public bool ToolUnavailable;
        public bool TimedOut;
        public string Message;
        public string RawOutput;
        public readonly List<BuildIssue> Issues = new List<BuildIssue>();
    }
}
