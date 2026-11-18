using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 备份管理器（规范 §10.2.3 / §11.5）。
    /// 备份源码 .cs / .Lib / 部署 7 件到 BackupsDir\&lt;时间戳&gt;\，保留原相对路径。
    /// 保留最近 10 次；更早的清理必须用户显式确认，禁止自动删除任何文件。
    /// </summary>
    public sealed class BackupManager
    {
        private readonly ReverseIndexConfig _cfg;
        private readonly string _sessionDir;

        public BackupManager(ReverseIndexConfig cfg)
        {
            _cfg = cfg;
            _sessionDir = Path.Combine(cfg.BackupsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }

        public string SessionDir { get { return _sessionDir; } }

        /// <summary>备份单个源码文件（相对仓库根路径），返回备份绝对路径；失败抛异常。</summary>
        public string BackupSource(string relativePath)
        {
            string src = Path.GetFullPath(Path.Combine(_cfg.RepoRoot, relativePath.Replace('/', '\\')));
            if (!File.Exists(src)) throw new FileNotFoundException("源码文件不存在", src);
            string dst = Path.Combine(_sessionDir, relativePath.Replace('/', '\\'));
            CopyInto(dst, src);
            return dst;
        }

        /// <summary>备份 lib 文件（绝对路径），扩展名追加 .bak。</summary>
        public string BackupLib(string libFullPath)
        {
            if (!File.Exists(libFullPath)) throw new FileNotFoundException("lib 文件不存在", libFullPath);
            // 落在 BackupsDir\<session>\libs\<文件名>.bak，避免与源码相对路径冲突。
            string dst = Path.Combine(_sessionDir, "libs", Path.GetFileName(libFullPath) + ".bak");
            CopyInto(dst, libFullPath);
            return dst;
        }

        /// <summary>备份部署目标文件（7 件之一），落在 BackupsDir\&lt;session&gt;\deploy\。</summary>
        public string BackupDeployFile(string fileName)
        {
            string src = Path.Combine(_cfg.DeployRoot, fileName);
            if (!File.Exists(src)) throw new FileNotFoundException("部署文件不存在", src);
            string dst = Path.Combine(_sessionDir, "deploy", fileName);
            CopyInto(dst, src);
            return dst;
        }

        /// <summary>
        /// 从备份恢复单个文件到目标绝对路径（"回到备份"入口）。
        /// 仅允许恢复到白名单路径（源码 ScanRoot / 部署目录 / Data 目录）。
        /// </summary>
        public void Restore(string backupFullPath, string targetFullPath)
        {
            if (!File.Exists(backupFullPath)) throw new FileNotFoundException("备份不存在", backupFullPath);
            targetFullPath = Path.GetFullPath(targetFullPath);
            if (!IsWhitelistedTarget(targetFullPath))
                throw new InvalidOperationException("恢复目标越界（仅允许 Client 源码 / 部署目录 / Data 目录）: " + targetFullPath);
            CopyInto(targetFullPath, backupFullPath);
        }

        private static void CopyInto(string dst, string src)
        {
            string dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dst, true);
        }

        private bool IsWhitelistedTarget(string full)
        {
            string scan = Path.GetFullPath(_cfg.ScanRoot) + Path.DirectorySeparatorChar;
            string deploy = Path.GetFullPath(_cfg.DeployRoot) + Path.DirectorySeparatorChar;
            string data = Path.GetFullPath(_cfg.DeployDataRoot) + Path.DirectorySeparatorChar;
            return full.StartsWith(scan, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(deploy, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(data, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 列出早于保留窗口的备份目录（按目录名时间排序，最近 10 个之后）。
        /// 仅返回清单，不删除——删除必须由用户显式确认。
        /// </summary>
        public IReadOnlyList<string> ListExpiredBackupDirs()
        {
            if (!Directory.Exists(_cfg.BackupsDir)) return new List<string>();
            var dirs = Directory.GetDirectories(_cfg.BackupsDir)
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
            return dirs.Count <= 10 ? new List<string>() : dirs.Skip(10).ToList();
        }
    }
}
