using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>引用分类（规范 §8）。</summary>
    public enum RefKind
    {
        Constant,        // Libraries.X.Draw(1121, …) / Index = 1903
        Expression,      // DrawBlend(1820 + Direction*10 + FrameIndex, …)
        PlayerFrameArg,  // Player.Add(MirAction.X, new Frame(...))
        DefaultFrameArg, // DefaultNPC/DefaultMonster = new FrameSet{...}
        LibFrames,       // lib v3 自带 Frames 段（不来自源码扫描）
        Indirect         // BodyLibrary.Draw(DrawFrame + ArmourOffSet, …)
    }

    /// <summary>new Frame(...) 8 个参数的角色（规范 §8.1，仅第 1、第 5 是帧号）。</summary>
    public enum FrameArgKind
    {
        None,
        FrameStart,      // 1 start   ✅ 帧号
        Count,           // 2 count   ❌ 帧数
        Skip,            // 3 skip    ❌ 跳帧数
        Interval,        // 4 interval❌ 毫秒
        EffectStart,     // 5 effectstart ✅ 帧号
        EffectCount,     // 6
        EffectSkip,      // 7
        EffectInterval   // 8
    }

    /// <summary>
    /// 一条源码帧号引用（规范 §6 数据模型）。
    /// TokenStart/TokenLength 为 UTF-16 char 偏移（string 索引，与 Roslyn TextSpan 同源），
    /// 严禁当作字节偏移（源码含中文，§11.10）。
    /// </summary>
    public sealed class Reference
    {
        public string Id;                 // 稳定签名哈希：文件+变量名+归一化表达式模式+token序号（不含行号/帧号）
        public string LibVariable;        // Libraries.Prguse / Libraries.Monsters[79] / 特殊标记
        public string FilePath;           // 相对仓库根
        public int Line, Column;          // 1-based
        public int TokenStart, TokenLength; // 本次替换范围（UTF-16 char 偏移）
        public int TokenOrdinal;          // 同一行/同一表达式内第几个可改 token（从右到左替换）
        public string Expression;         // 原始表达式文本
        public string NormalizedExpr;     // 归一化表达式模式（数字→<N>），用于 Id
        public int BaseValue;             // 可编辑常量基址/帧号
        public string DynamicPart;        // "+ Direction * 10 + ..." 保留项
        public string HumanLabel;         // 人话标签
        public string LabelSource;        // 别名/变量名/枚举/状态/本地化Key/注释/上下文/兜底
        public RefKind Kind;
        public FrameArgKind ArgKind;
        public string Target;             // "Source" | "LibFrames"
        public bool AmbiguousLib;
        public bool PlayerGlobal;         // 玩家帧表全局共用（§8.2）
        public bool GlobalFallback;       // 全局兜底表 DefaultNPC/DefaultMonster
        public string SpecialMonsterList; // 特殊怪物 FrameSet.XXX 列表名（§8.4）

        // T6 组信息：同一组 = 一次换图/补图要一起处理的帧集合
        public string GroupId;            // 同组引用共享（null/空 = 未分组）
        public int GroupStart = -1;       // 组起始帧（仅 new Frame 语句组有精确值）
        public int GroupCount;            // 组帧数（同上）
        public int GroupSkip;             // 组跳帧（同上）
        public bool IsFrameNumberLibCheck = true; // P0-1：lib 帧表帧号参数的界内标记（帧数/跳帧/毫秒恒 true）

        // P0-1：lib 自带帧表引用（Target="LibFrames"）专用——编辑保存时写回 _lib.Frames[action] 的对应属性
        public string LibFrameAction;     // MirAction 名（如 站立动作），仅展示/定位用
        public int LibFrameActionValue = -1; // MirAction 枚举值（写回时还原枚举 key；-1 = 非 lib 帧表引用）

        // UI 编辑后的值（初始等于 BaseValue）
        public int EditedValue;
        public bool Changed { get { return EditedValue != BaseValue; } }

        /// <summary>该 token 是否为"帧号"（可选帧按钮）；非帧号参数为帧数/跳帧/毫秒。</summary>
        public bool IsFrameNumber
        {
            get
            {
                if (Kind == RefKind.PlayerFrameArg || Kind == RefKind.DefaultFrameArg)
                    return ArgKind == FrameArgKind.FrameStart || ArgKind == FrameArgKind.EffectStart;
                return Kind == RefKind.Constant || Kind == RefKind.Expression
                    || Kind == RefKind.LibFrames || Kind == RefKind.Indirect;
            }
        }

        /// <summary>人话标签是否合格（兜底且无语义的项禁止保存，§9）。</summary>
        public bool LabelQualified
        {
            get { return !string.IsNullOrEmpty(HumanLabel) && LabelSource != "兜底"; }
        }

        private static readonly Regex NumberPattern = new Regex(@"(?<![\w.])[-+]?\d+(?![\w])", RegexOptions.Compiled);

        /// <summary>
        /// 归一化表达式：所有独立数字 token 替换为 &lt;N&gt;（词边界排除变量名里的数字，如 Magic2）。
        /// 改帧号后归一化串不变 → Id 稳定、幂等成立（§6 / §11.3）。
        /// </summary>
        public static string Normalize(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return string.Empty;
            return NumberPattern.Replace(expression, "<N>");
        }

        /// <summary>
        /// 生成稳定 Id：文件 + 变量名 + 归一化表达式模式 + token序号 + 同形态出现序号，不含行号、不含具体帧号。
        /// occurrence：同文件+同变量+同归一化表达式的第几次出现（0 起）。
        /// 修复审查 P1-7：完全相同的两条 Draw(1121,…) 若 Id 相同，别名会互相污染、重扫恢复会串编辑。
        /// occurrence 由扫描器按出现顺序累加；Id 仍与行号/帧号无关（不移动语句则不变）。
        /// </summary>
        public static string MakeId(string file, string libVar, string expression, int ordinal, int occurrence = 0)
        {
            string raw = file + "|" + libVar + "|" + Normalize(expression) + "|#" + ordinal + "|@" + occurrence;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
