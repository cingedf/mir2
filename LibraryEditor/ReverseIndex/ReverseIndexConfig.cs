using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// P0/T0.1 — 源码帧号反向索引的路径常量与配置（规范 §10/§11）。
    /// 由程序集位置向上定位仓库根；支持可选的 JSON 覆盖。
    /// </summary>
    public sealed class ReverseIndexConfig
    {
        private static ReverseIndexConfig _instance;
        public static ReverseIndexConfig Instance => _instance ?? (_instance = Load());

        // 由程序集位置向上定位，默认回退到已知路径
        public string RepoRoot { get; set; }
        public string DeployRoot { get; set; }

        public string BuildOut => Path.Combine(RepoRoot, "Build", "Client", "Release");
        public string ClientProject => Path.Combine(RepoRoot, "Client", "Client.csproj");
        public string ScanRoot => Path.Combine(RepoRoot, "Client");
        public string SettingsCs => Path.Combine(ScanRoot, "Settings.cs");
        public string MLibraryCs => Path.Combine(ScanRoot, "MirGraphics", "MLibrary.cs");
        public string ClientFramesCs => Path.Combine(ScanRoot, "MirObjects", "Frames.cs");
        public string EnumsCs => Path.Combine(RepoRoot, "Shared", "Enums.cs");
        public string DeployDataRoot => Path.Combine(DeployRoot, "Data");

        public string ConfigDir => Path.Combine(RepoRoot, "LibraryEditor", "Config");
        public string BackupsDir => Path.Combine(RepoRoot, "LibraryEditor", "Backups");
        public string AliasesPath => Path.Combine(ConfigDir, "FrameAliases.json");
        public string MapOverridesPath => Path.Combine(ConfigDir, "LibraryMapOverrides.json");

        /// <summary>编译产物 7 件（规范 §10.4）。</summary>
        public static readonly string[] DeployFiles =
        {
            "Client.exe", "Client.dll", "Client.pdb", "Client.deps.json",
            "Client.runtimeconfig.json", "Shared.dll", "Shared.pdb",
        };

        private static ReverseIndexConfig Load()
        {
            var root = FindRepoRoot();
            var cfg = new ReverseIndexConfig
            {
                RepoRoot = root,
                DeployRoot = @"C:\1.40\mir2-deploy\Client",
            };
            try
            {
                var path = Path.Combine(cfg.ConfigDir, "ReverseIndexConfig.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var m1 = Regex.Match(json, @"""RepoRoot""\s*:\s*""([^""]+)""");
                    if (m1.Success) cfg.RepoRoot = m1.Groups[1].Value;
                    var m2 = Regex.Match(json, @"""DeployRoot""\s*:\s*""([^""]+)""");
                    if (m2.Success) cfg.DeployRoot = m2.Groups[1].Value;
                }
            }
            catch { /* 配置损坏时用默认值，不阻断 */ }
            return cfg;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Client", "Client.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return @"C:\1.40\mir2-angelk727";
        }
    }
}
