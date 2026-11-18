using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// 人话标签生成器（规范 §9）：别名 → 控件变量名词典 → MirAction → 状态后缀
    /// → 本地化 Key(辅助) → 邻近注释 → 上下文方法名 → 兜底。
    /// 标签必须含"什么东西+什么状态/动作"，禁止裸数字或代码符号（兜底/未知时标灰禁保存）。
    /// </summary>
    public sealed class LabelGenerator
    {
        // 控件变量名词典（§9 第2优先级，"背包按钮"一目了然的主力）
        private static readonly Dictionary<string, string> ControlNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "InventoryButton", "背包按钮" },
            { "CharacterButton", "人物按钮" },
            { "SkillButton", "技能按钮" },
            { "MagicButton", "魔法按钮" },
            { "MapButton", "地图按钮" },
            { "QuestButton", "任务按钮" },
            { "NPCButton", "NPC按钮" },
            { "FriendButton", "好友按钮" },
            { "GuildButton", "行会按钮" },
            { "GroupButton", "组队按钮" },
            { "ExitButton", "退出按钮" },
            { "CloseButton", "关闭按钮" },
            { "OkButton", "确定按钮" },
            { "CancelButton", "取消按钮" },
        };

        // 状态后缀（§9 第4优先级）
        private static readonly Dictionary<string, string> StateSuffix =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Index", "-普通态" },
            { "HoverIndex", "-悬停态" },
            { "PressedIndex", "-按下态" },
            { "DisabledIndex", "-禁用态" },
        };

        // 控件变量 → 本地化 Key 片段（辅助校验）
        private static readonly Regex ClientTextKeyPattern =
            new Regex(@"ClientTextKeys\.(\w+)", RegexOptions.Compiled);

        private readonly Dictionary<string, string> _aliases; // key = Reference 稳定 Id

        public LabelGenerator(Dictionary<string, string> aliases = null)
        {
            _aliases = aliases ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// 生成标签。参数：引用对象、变量名(如 InventoryButton)、成员名(如 Index)、
        /// MirAction 名(如 站立动作)、源码行数组(用于邻近注释)、行号(1-based)、方法名。
        /// </summary>
        public void Apply(Reference r, string variableName, string memberName, string mirAction,
                          string[] lines, int lineNo, string methodName)
        {
            // 1. 用户别名（最高优先级）
            if (!string.IsNullOrEmpty(r.Id) && _aliases.TryGetValue(r.Id, out string alias) &&
                !string.IsNullOrWhiteSpace(alias))
            {
                r.HumanLabel = alias;
                r.LabelSource = "别名";
                return;
            }

            string label = null;
            string source = null;

            // 2. 控件变量名词典
            if (!string.IsNullOrEmpty(variableName) && ControlNames.TryGetValue(variableName, out string cn))
            {
                label = cn;
                source = "变量名";
            }

            // 3. MirAction 枚举名
            if (label == null && !string.IsNullOrEmpty(mirAction))
            {
                label = mirAction;
                source = "MirAction";
            }

            // 4. 状态后缀（附加到已有标签，或作为 NPC/怪物状态名）
            if (!string.IsNullOrEmpty(memberName) && StateSuffix.TryGetValue(memberName, out string suffix))
            {
                if (label != null) { label += suffix; }
                else { label = StripSuffix(memberName) + suffix; source = "状态"; }
            }

            // 5. 本地化 Key（辅助：仅当前面没有标签时取中文提示）
            if (label == null && lines != null && lineNo > 0 && lineNo <= lines.Length)
            {
                var m = ClientTextKeyPattern.Match(lines[lineNo - 1]);
                if (m.Success) { label = m.Groups[1].Value; source = "本地化Key"; }
            }

            // 6. 邻近注释（上方 3 行内的中文注释）
            if (label == null)
            {
                string comment = FindNearbyChineseComment(lines, lineNo);
                if (comment != null) { label = comment; source = "注释"; }
            }

            // 7. 上下文方法名
            if (label == null && !string.IsNullOrEmpty(methodName))
            {
                label = methodName;
                source = "上下文";
            }

            // 8. 兜底（标灰禁保存）
            if (label == null)
            {
                label = string.Format("{0}:{1} 的帧引用（{2}）",
                    Path.GetFileName(r.FilePath ?? ""), r.Line, r.Expression ?? "");
                source = "兜底";
            }

            // 特殊怪物：加枚举名前缀让标签一眼可懂（§8.4）
            if (!string.IsNullOrEmpty(r.SpecialMonsterList) && source != "别名")
            {
                string sm = SpecialMonsterName(r.SpecialMonsterList);
                if (sm != null && !label.Contains(sm)) label = sm + "-" + label;
            }

            // 玩家帧表全局共用后缀（§8.2）
            if (r.PlayerGlobal && !label.Contains("玩家")) label += "（玩家全局）";

            r.HumanLabel = label;
            r.LabelSource = source;
        }

        private static string StripSuffix(string member)
        {
            if (member.EndsWith("Index", StringComparison.OrdinalIgnoreCase))
                return member.Substring(0, member.Length - 5);
            return member;
        }

        private static string SpecialMonsterName(string listVar)
        {
            switch (listVar)
            {
                case "DragonStatue": return "石像鬼";
                case "GreatFoxSpirit": return "九尾狐";
                case "HellBomb": return "地狱炸弹";
                case "Mon380P": return "380P怪物";
                case "Gates": return "沙巴克门";
                default: return listVar;
            }
        }

        private static string FindNearbyChineseComment(string[] lines, int lineNo)
        {
            if (lines == null || lineNo < 1) return null;
            int start = Math.Max(0, lineNo - 4); // 上方 3 行
            for (int i = lineNo - 2; i >= start; i--)
            {
                if (i < 0 || i >= lines.Length) continue;
                string t = lines[i].Trim();
                int idx = t.IndexOf("//");
                if (idx >= 0)
                {
                    string c = t.Substring(idx + 2).Trim().TrimStart('/').Trim();
                    if (c.Length > 0 && c.Any(ch => ch > 0x4E00 && ch < 0x9FFF))
                        return c.Trim('：', ':', ' ', '(', ')', '（', '）');
                }
            }
            return null;
        }
    }
}
