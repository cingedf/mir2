using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// T3.3：人话别名与映射覆盖的持久化管理（规范 §6 AliasConfig、§7 映射覆盖）。
    /// 别名 key = Reference.Id（稳定签名，不含行号/帧号），改帧号后仍绑定。
    /// 映射覆盖 key = lib 变量文本（如 Libraries.AWeaponsL），value = 覆盖后的变量文本。
    /// 两个 JSON 文件都用 UTF-8（无 BOM）写，读时兼容带/不带 BOM。
    /// </summary>
    public sealed class AliasManager
    {
        private readonly ReverseIndexConfig _cfg;

        /// <summary>别名字典：Reference.Id -> 人话标签。</summary>
        public Dictionary<string, string> Aliases { get; private set; }

        /// <summary>映射覆盖字典：原变量文本 -> 覆盖变量文本。</summary>
        public Dictionary<string, string> MapOverrides { get; private set; }

        public AliasManager(ReverseIndexConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Aliases = LoadDict(cfg.AliasesPath);
            MapOverrides = LoadDict(cfg.MapOverridesPath);
        }

        /// <summary>设置别名并立即持久化。label 为空则删除该别名。</summary>
        public void SetAlias(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (string.IsNullOrWhiteSpace(label))
                Aliases.Remove(id);
            else
                Aliases[id] = label.Trim();
            SaveDict(_cfg.AliasesPath, Aliases);
        }

        /// <summary>删除别名并持久化。返回是否确实删除了。</summary>
        public bool RemoveAlias(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            bool removed = Aliases.Remove(id);
            if (removed) SaveDict(_cfg.AliasesPath, Aliases);
            return removed;
        }

        /// <summary>设置映射覆盖并持久化。value 为空则移除覆盖。</summary>
        public void SetMapOverride(string variable, string overrideVariable)
        {
            if (string.IsNullOrWhiteSpace(variable)) return;
            if (string.IsNullOrWhiteSpace(overrideVariable))
                MapOverrides.Remove(variable);
            else
                MapOverrides[variable] = overrideVariable.Trim();
            SaveDict(_cfg.MapOverridesPath, MapOverrides);
        }

        /// <summary>重新从磁盘加载两个字典（外部改动文件后调用）。</summary>
        public void Reload()
        {
            Aliases = LoadDict(_cfg.AliasesPath);
            MapOverrides = LoadDict(_cfg.MapOverridesPath);
        }

        private static Dictionary<string, string> LoadDict(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return result;
                string json = ReadJson(path);
                if (string.IsNullOrWhiteSpace(json)) return result;
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            result[prop.Name] = prop.Value.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                // 配置损坏时返回空字典，不阻断功能（规范：配置错误不致命）。
                // P2-12：但必须留档 .bad——否则下次 SaveDict 会用空字典覆盖原文件，数据彻底丢失。
                try
                {
                    if (File.Exists(path))
                    {
                        string bad = path + ".bad";
                        if (File.Exists(bad)) File.Delete(bad);
                        File.Copy(path, bad, true);
                    }
                    Console.Error.WriteLine("[AliasManager] 配置解析失败，已留档 " + path + ".bad : " + ex.Message);
                }
                catch { /* 留档失败也不阻断 */ }
            }
            return result;
        }

        private static void SaveDict(string path, Dictionary<string, string> dict)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(dict, options);
            // UTF-8 无 BOM 写，沿用源码文件的编码保真原则。
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static string ReadJson(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                return new UTF8Encoding(false).GetString(raw, 3, raw.Length - 3);
            return new UTF8Encoding(false).GetString(raw);
        }
    }
}
