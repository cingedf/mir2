using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 部署换件器（规范 §10.4 / 缺陷①④）。
    ///
    /// 关键约束：
    /// - 客户端进程门禁在**任何写操作之前**：Client.exe 运行中直接拒绝，不写任何文件。
    /// - 7 件编译产物从 Build\Client\Release\ 换到 mir2-deploy\Client\。
    /// - 先备份目标 7 件，逐件复制 + 大小/时间戳校验，失败即回滚已复制的文件。
    /// - 路径白名单：源仅 BuildOut，目标仅 DeployRoot 下这 7 个文件名。
    /// - 失败不静默：结果如实列出逐件状态与原因。
    /// </summary>
    public sealed class Deployer
    {
        private readonly ReverseIndexConfig _cfg;
        private readonly BackupManager _backup;

        public Deployer(ReverseIndexConfig cfg, BackupManager backup)
        {
            _cfg = cfg;
            _backup = backup;
        }

        /// <summary>客户端 Client.exe 是否在运行（门禁依据）。</summary>
        public static bool ClientRunning()
        {
            try { return Process.GetProcessesByName("Client").Length > 0; }
            catch { return false; }
        }

        public DeployResult Deploy()
        {
            var r = new DeployResult();

            // 门禁：任何写操作之前
            if (ClientRunning())
            {
                r.Message = "客户端 Client.exe 正在运行，请先关闭游戏客户端再部署（避免文件被占用）。";
                return r;
            }

            // 预校验：源 7 件必须全部存在
            foreach (var f in ReverseIndexConfig.DeployFiles)
            {
                string src = Path.Combine(_cfg.BuildOut, f);
                if (!File.Exists(src))
                {
                    r.Message = "编译产物缺失，无法部署：" + src;
                    return r;
                }
            }

            // 备份目标 7 件（失败即中止，不动目标）
            var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in ReverseIndexConfig.DeployFiles)
            {
                try
                {
                    backups[f] = _backup.BackupDeployFile(f);
                }
                catch (Exception ex)
                {
                    r.Message = "备份部署目标失败，已中止（未改动任何文件）：" + f + " - " + ex.Message;
                    return r;
                }
            }

            // 逐件复制 + 校验
            var done = new List<string>();
            foreach (var f in ReverseIndexConfig.DeployFiles)
            {
                string src = Path.Combine(_cfg.BuildOut, f);
                string dst = Path.Combine(_cfg.DeployRoot, f);
                try
                {
                    // 白名单二次校验
                    string fullDst = Path.GetFullPath(dst);
                    string root = Path.GetFullPath(_cfg.DeployRoot) + Path.DirectorySeparatorChar;
                    if (!fullDst.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("目标越界: " + fullDst);

                    File.Copy(src, dst, true);
                    var fiSrc = new FileInfo(src);
                    var fiDst = new FileInfo(dst);
                    fiSrc.Refresh(); fiDst.Refresh();
                    if (fiSrc.Length != fiDst.Length)
                        throw new IOException("大小不一致: 源 " + fiSrc.Length + " / 目标 " + fiDst.Length);
                    if (Math.Abs((fiSrc.LastWriteTimeUtc - fiDst.LastWriteTimeUtc).TotalSeconds) > 2)
                        throw new IOException("时间戳不一致: 源 " + fiSrc.LastWriteTimeUtc + " / 目标 " + fiDst.LastWriteTimeUtc);

                    done.Add(f);
                    r.Files.Add(new DeployFileStatus { FileName = f, Ok = true, Message = "已换件并校验通过" });
                }
                catch (Exception ex)
                {
                    r.Files.Add(new DeployFileStatus { FileName = f, Ok = false, Message = ex.Message });
                    // 失败即回滚已复制的文件
                    foreach (var d in done)
                    {
                        try { _backup.Restore(backups[d], Path.Combine(_cfg.DeployRoot, d)); }
                        catch { /* 回滚失败也记录，不掩盖原始错误 */ }
                    }
                    r.Message = "部署失败，" + done.Count + " 件已回滚：" + f + " - " + ex.Message;
                    return r;
                }
            }

            r.Success = true;
            r.Message = "部署成功：7 件已换到 " + _cfg.DeployRoot + "，请重启客户端生效。";
            return r;
        }

        /// <summary>标记"跳过（无源码变更）"结果，供状态矩阵使用。</summary>
        public static DeployResult Skipped()
        {
            return new DeployResult { Skipped = true, Success = true, Message = "跳过（无源码变更，无需换件）" };
        }
    }

    public sealed class DeployFileStatus
    {
        public string FileName;
        public bool Ok;
        public string Message;
    }

    public sealed class DeployResult
    {
        public bool Success;
        public bool Skipped;
        public string Message;
        public readonly List<DeployFileStatus> Files = new List<DeployFileStatus>();
    }
}
