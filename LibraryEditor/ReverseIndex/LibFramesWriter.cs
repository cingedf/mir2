using System;
using System.Collections.Generic;
using System.IO;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// lib 自带 Frames 段原子写回器（规范 §10.2.7 / §11.10 / 缺陷④）。
    ///
    /// MLibraryV2.Save() 当前直接用 File.Create(FileName) 覆盖，非原子——本类不复用它，
    /// 而是按完全相同格式把 Images + Frames 序列化到临时文件，再用 File.Replace 原子替换。
    /// 写前：客户端进程门禁（任何写操作之前）+ 备份 lib；失败清理临时文件、不损坏原 lib。
    /// </summary>
    public sealed class LibFramesWriter
    {
        private readonly ReverseIndexConfig _cfg;
        private readonly BackupManager _backup;

        public LibFramesWriter(ReverseIndexConfig cfg, BackupManager backup)
        {
            _cfg = cfg;
            _backup = backup;
        }

        public LibFramesWriteResult Write(MLibraryV2 lib)
        {
            var r = new LibFramesWriteResult();
            if (lib == null || string.IsNullOrEmpty(lib.FileName))
            {
                r.Message = "未打开有效的 lib。";
                return r;
            }
            if (lib.Frames == null || lib.Frames.Keys.Count == 0)
            {
                r.Message = "当前 lib 无自带帧表可写回（v2 或空表）。";
                return r;
            }

            // 门禁：任何写操作之前
            if (Deployer.ClientRunning())
            {
                r.Message = "客户端 Client.exe 正在运行，请先关闭再写回 lib 帧表（文件被占用）。";
                return r;
            }

            string libPath = Path.GetFullPath(lib.FileName);
            string dataRoot = Path.GetFullPath(_cfg.DeployDataRoot) + Path.DirectorySeparatorChar;
            if (!libPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase))
            {
                r.Message = "lib 路径越界（仅允许运行目录 Data 下）: " + libPath;
                return r;
            }
            if (!File.Exists(libPath))
            {
                r.Message = "lib 文件不存在: " + libPath;
                return r;
            }

            try
            {
                r.BackupPath = _backup.BackupLib(libPath);
            }
            catch (Exception ex)
            {
                r.Message = "备份 lib 失败，已中止（未改动 lib）：" + ex.Message;
                return r;
            }

            string tmp = libPath + ".ri_tmp";
            try
            {
                lib.Close(); // 释放读取流，避免占用
                Serialize(lib, tmp);
                if (!File.Exists(tmp) || new FileInfo(tmp).Length == 0)
                    throw new IOException("临时文件序列化结果为空");
                File.Replace(tmp, libPath, null);
                r.Success = true;
                r.Message = "lib 自带帧表已写回并原子替换：" + libPath;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                r.Message = "lib 帧表写回失败（原 lib 未损坏）：" + ex.Message;
            }
            return r;
        }

        /// <summary>按 MLibraryV2.Save() 完全相同的字节格式序列化到指定路径（不触碰原文件）。</summary>
        private static void Serialize(MLibraryV2 lib, string path)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int count = lib.Images.Count;
                int offSet = (4 + 4 + 4) + (count * 4);
                var indexList = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    indexList.Add((int)stream.Length + offSet);
                    lib.Images[i].Save(writer);
                }
                int frameSeek = (int)stream.Length + offSet;
                writer.Flush();
                byte[] fBytes = stream.ToArray();

                using (var fs = File.Create(path))
                using (var fw = new BinaryWriter(fs))
                {
                    fw.Write(MLibraryV2.LibVersion);
                    fw.Write(count);
                    fw.Write(frameSeek);
                    for (int i = 0; i < count; i++) fw.Write(indexList[i]);
                    fw.Write(fBytes);
                    fw.Write(lib.Frames.Keys.Count);
                    foreach (var action in lib.Frames.Keys)
                    {
                        fw.Write((byte)action);
                        lib.Frames[action].Save(fw);
                    }
                    fw.Flush();
                }
            }
        }
    }

    public sealed class LibFramesWriteResult
    {
        public bool Success;
        public string Message;
        public string BackupPath;
    }
}
