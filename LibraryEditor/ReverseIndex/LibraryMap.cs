using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>P0/T0.2 — 单条库映射：磁盘 lib ↔ 源码 Library 变量。</summary>
    public sealed class LibraryEntry
    {
        public string Variable;       // 变量名，如 Prguse / AArmours（数组基名）；被映射覆盖改写后的生效名
        public string SourceVariable; // 解析原始名（映射覆盖的稳定 key，不随覆盖改名漂移）
        public bool IsArray;
        public string DiskDir;        // 相对 Data 目录，如 "" / "Monster" / "AWeapon"
        public string FileBase;       // 单文件文件名字面量（如 MMap、DNItems）
        public string Digits;         // 数组编号格式 "00" / "000"
        public string Suffix;         // 数组后缀 "" / " L" / " R" / " S"
        public string PathConstant;   // DataPath / MonsterPath ...
    }

    public sealed class LibraryResolveResult
    {
        public LibraryEntry Entry;
        public int Index = -1;        // 数组下标；单文件为 -1
        public string VariableText => Entry == null
            ? null
            : (Entry.IsArray ? "Libraries." + Entry.Variable + "[" + Index + "]" : "Libraries." + Entry.Variable);
    }

    /// <summary>
    /// P0/T0.2 — 映射解析器（规范 §7）。读 Settings.cs 路径常量 + MLibrary.cs 初始化。
    /// 关键点：单文件库变量名 ≠ 文件名字面量（MiniMap→MMap、StateItems→StateItem、FloorItems→DNItems），
    /// 必须按字符串字面量取文件名；数组支持 suffix 四参数；反查须拆"数字编号+后缀"。
    /// </summary>
    public sealed class LibraryMap
    {
        private readonly List<LibraryEntry> _entries = new List<LibraryEntry>();
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _dataPrefix = @".\Data\";

        public IReadOnlyList<LibraryEntry> Entries => _entries;

        public void Load(ReverseIndexConfig cfg)
        {
            _entries.Clear();
            _paths.Clear();
            ParseSettings(cfg.SettingsCs);
            ParseMLibrary(cfg.MLibraryCs);
        }

        private void ParseSettings(string path)
        {
            var text = File.ReadAllText(path);
            // public const string 块，可能跨多行、逗号分隔，分号结尾
            foreach (Match block in Regex.Matches(text, @"public\s+const\s+string\s+([^;]+);", RegexOptions.Singleline))
            {
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"(\w+)\s*=\s*@""([^""]+)"""))
                    _paths[m.Groups[1].Value] = m.Groups[2].Value;
            }
            if (_paths.TryGetValue("DataPath", out var dp)) _dataPrefix = dp;
        }

        private void ParseMLibrary(string path)
        {
            var text = File.ReadAllText(path);
            // 单文件：X = new MLibrary(Settings.DataPath + "Y")
            foreach (Match m in Regex.Matches(text,
                @"(\w+)\s*=\s*new\s+MLibrary\(\s*Settings\.(\w+)\s*\+\s*""([^""]+)""\s*\)"))
            {
                var varName = m.Groups[1].Value;
                if (varName == "MapLibs") continue; // §7.7 Out of Scope
                _entries.Add(new LibraryEntry
                {
                    Variable = varName,
                    SourceVariable = varName,
                    IsArray = false,
                    DiskDir = ToDataRelative(_paths.TryGetValue(m.Groups[2].Value, out var p) ? p : ""),
                    FileBase = m.Groups[3].Value,
                    PathConstant = m.Groups[2].Value,
                });
            }
            // 数组：InitLibrary(ref V, Settings.P, "digits"[, "suffix"])
            foreach (Match m in Regex.Matches(text,
                @"InitLibrary\(\s*ref\s+(\w+)\s*,\s*Settings\.(\w+)\s*,\s*""([^""]+)""(?:\s*,\s*""([^""]*)"")?\s*\)"))
            {
                _entries.Add(new LibraryEntry
                {
                    Variable = m.Groups[1].Value,
                    SourceVariable = m.Groups[1].Value,
                    IsArray = true,
                    DiskDir = ToDataRelative(_paths.TryGetValue(m.Groups[2].Value, out var p) ? p : ""),
                    Digits = m.Groups[3].Value,
                    Suffix = m.Groups[4].Success ? m.Groups[4].Value : "",
                    PathConstant = m.Groups[2].Value,
                });
            }
        }

        /// <summary>把 Settings 常量值（如 .\Data\Monster\）转为相对 Data 的目录（Monster）。</summary>
        private string ToDataRelative(string raw)
        {
            var s = (raw ?? "").Replace('/', '\\');
            if (s.StartsWith(_dataPrefix.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                s = s.Substring(_dataPrefix.Length);
            return s.Trim('\\', '.').TrimEnd('\\');
        }

        /// <summary>按磁盘 lib 绝对路径反查源码变量（规范 A1）。</summary>
        public LibraryResolveResult Resolve(string libFullPath, ReverseIndexConfig cfg)
        {
            var rel = Path.GetRelativePath(cfg.DeployDataRoot, libFullPath).Replace('/', '\\');
            var dir = Path.GetDirectoryName(rel) ?? "";
            var file = Path.GetFileNameWithoutExtension(rel);

            foreach (var e in _entries.Where(x => !x.IsArray))
            {
                if (string.Equals(e.DiskDir, dir, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.FileBase, file, StringComparison.OrdinalIgnoreCase))
                    return new LibraryResolveResult { Entry = e };
            }
            foreach (var e in _entries.Where(x => x.IsArray))
            {
                if (!string.Equals(e.DiskDir, dir, StringComparison.OrdinalIgnoreCase)) continue;
                var pattern = @"^(\d+)" + Regex.Escape(e.Suffix) + @"$";
                var m = Regex.Match(file, pattern);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var idx))
                    return new LibraryResolveResult { Entry = e, Index = idx };
            }
            return null; // 映射失败，UI 提供手工指定
        }
    }
}
