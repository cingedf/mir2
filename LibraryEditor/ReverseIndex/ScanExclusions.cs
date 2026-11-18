using System;
using System.IO;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>P0/T0.3 — 扫描排除器（规范 §7.5）。relativePath 为相对 ScanRoot 的路径。</summary>
    public static class ScanExclusions
    {
        public static bool IsExcluded(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;
            var norm = relativePath.Replace('/', '\\');
            // 目录排除：obj / bin / Build / .git（不排除 *.Designer.cs，控件帧号在其中）
            foreach (var s in norm.Split('\\'))
            {
                if (s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("Build", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            var name = Path.GetFileName(norm);
            if (name.Equals("GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
