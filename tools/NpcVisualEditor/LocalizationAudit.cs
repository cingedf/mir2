using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NpcVisualEditor
{
    /// <summary>
    /// 本地化缺失键检测（纯逻辑，只读）：对比 Client/Server 的 Chinese/English.json 的 Text/Enum 段，
    /// 以及 12 个可本地化枚举类型的全部成员键，找出缺键/缺翻译。严格只读，避开 Language.Save 的覆写时点。
    /// 对标 BOO/Vastopia 本地化上下文管理。
    /// </summary>
    public static class LocalizationAudit
    {
        public class MissingKey
        {
            public string File, Section, Key, Detail;
        }

        public class AuditResult
        {
            public string Scope;
            public Dictionary<string, int> Counts = new();      // 文件→条目数
            public List<MissingKey> Missing = new();
        }

        // 与 Language.localizationEnumTypes 一致（Shared\Language.cs:4116-4130）
        private static readonly string[] RefEnumTypes =
        {
            "MirClass", "RequiredClass", "RequiredType", "Stat", "BuffType", "PoisonType",
            "AwakeType", "MonsterType", "HeroBehaviour", "BindMode", "SpecialItemMode", "LightSetting"
        };

        /// <summary>读取含 Text/Enum 两节的本地化 json，返回节→键集合（JsonDocument 保大小写精确）。</summary>
        private static Dictionary<string, Dictionary<string, string>> ReadSections(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var sec in doc.RootElement.EnumerateObject())
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                if (sec.Value.ValueKind == JsonValueKind.Object)
                    foreach (var kv in sec.Value.EnumerateObject())
                        d[kv.Name] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : "";
                result[sec.Name] = d;
            }
            return result;
        }

        /// <summary>审计一组语言目录（如 Client\Localization 或 Server\Localization）。</summary>
        public static AuditResult Audit(string locDir, string scope)
        {
            var r = new AuditResult { Scope = scope };
            string zhPath = Path.Combine(locDir, "Chinese.json");
            string enPath = Path.Combine(locDir, "English.json");
            var zh = ReadSections(zhPath);
            var en = ReadSections(enPath);

            foreach (var kv in zh) r.Counts["中文." + kv.Key] = kv.Value.Count;
            foreach (var kv in en) r.Counts["英文." + kv.Key] = kv.Value.Count;

            // 1) 英文 Text 相对中文 Text 的缺失键（含全角冒号/占位符键名也要计数，但不误报占位符）
            if (zh.TryGetValue("Text", out var zhText))
            {
                var enText = en.TryGetValue("Text", out var t) ? t : new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in zhText.Keys)
                {
                    if (IsPlaceholder(key)) continue;
                    if (!enText.ContainsKey(key))
                        r.Missing.Add(new MissingKey { File = "English.json", Section = "Text", Key = key, Detail = "英文缺该文案键" });
                }
            }

            // 2) 英文 Enum 相对中文 Enum 的缺失键——用参考枚举成员集过滤中文成员名（避免中文键在英文0命中误报）
            if (zh.TryGetValue("Enum", out var zhEnum))
            {
                var enEnum = en.TryGetValue("Enum", out var e) ? e : new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in zhEnum.Keys)
                {
                    if (!IsRefEnumKey(key)) continue; // 只审计参考枚举集合的键，排除 StatFormula_* 等孤儿
                    if (!enEnum.ContainsKey(key))
                        r.Missing.Add(new MissingKey { File = "English.json", Section = "Enum", Key = key, Detail = "英文缺该枚举本地化" });
                }
            }
            return r;
        }

        /// <summary>审计服务端枚举本地化完整性：12 枚举成员键 vs Server\Localization\Chinese.json 的 Enum 段。</summary>
        public static AuditResult AuditServerEnums(string serverLocDir, IEnumerable<string> expectedKeys)
        {
            var r = new AuditResult { Scope = "ServerEnum" };
            var zh = ReadSections(Path.Combine(serverLocDir, "Chinese.json"));
            var en = ReadSections(Path.Combine(serverLocDir, "English.json"));
            var zhEnum = zh.TryGetValue("Enum", out var z) ? z : new Dictionary<string, string>(StringComparer.Ordinal);
            var enEnum = en.TryGetValue("Enum", out var e) ? e : new Dictionary<string, string>(StringComparer.Ordinal);
            r.Counts["中文.Enum"] = zhEnum.Count;
            r.Counts["英文.Enum"] = enEnum.Count;
            foreach (var key in expectedKeys)
            {
                if (!IsRefEnumKey(key)) continue;
                if (!zhEnum.ContainsKey(key))
                    r.Missing.Add(new MissingKey { File = "Chinese.json", Section = "Enum", Key = key, Detail = "服务端中文缺该枚举键" });
                if (!enEnum.ContainsKey(key))
                    r.Missing.Add(new MissingKey { File = "English.json", Section = "Enum", Key = key, Detail = "服务端英文缺该枚举键" });
            }
            return r;
        }

        /// <summary>由源码反射生成期望的枚举键（类型_成员名）。传入实际程序集的类型以避免硬编码成员。</summary>
        public static List<string> ExpectedEnumKeys(IEnumerable<Type> enumTypes)
        {
            var keys = new List<string>();
            foreach (var t in enumTypes)
            {
                if (!t.IsEnum) continue;
                foreach (var n in Enum.GetNames(t)) keys.Add(t.Name + "_" + n);
            }
            return keys;
        }

        private static bool IsRefEnumKey(string key)
        {
            foreach (var t in RefEnumTypes)
                if (key.StartsWith(t + "_", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsPlaceholder(string key) =>
            key.Contains("{0}") || key.Contains("{1}") || key.Contains("{2}") || key.Length == 0;
    }
}
