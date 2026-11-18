using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LibraryEditor.ReverseIndex
{
    /// <summary>
    /// Roslyn 引用扫描器（规范 §13 / 任务 T1.1-T1.6）。
    /// 纯语法树 ParseText，禁用 GetSemanticModel（本机缺 SlimDX 引用会失败）。
    /// token 位置用 Roslyn TextSpan（UTF-16 char 偏移），写回按 string 切片（§11.10）。
    /// </summary>
    public sealed class ReferenceScanner
    {
        private readonly ReverseIndexConfig _cfg;
        private readonly LabelGenerator _labels;

        public ReferenceScanner(ReverseIndexConfig cfg, LabelGenerator labels = null)
        {
            _cfg = cfg;
            _labels = labels ?? new LabelGenerator();
        }

        /// <summary>扫描 Client 工程全部 .cs，返回所有引用（含 LibVariable，UI 按 lib 过滤）。</summary>
        public List<Reference> ScanAll(out List<string> errors)
        {
            errors = new List<string>();
            var result = new List<Reference>();
            if (!Directory.Exists(_cfg.ScanRoot)) { errors.Add("扫描目录不存在: " + _cfg.ScanRoot); return result; }

            string[] files;
            try { files = Directory.GetFiles(_cfg.ScanRoot, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex) { errors.Add("枚举源码失败: " + ex.Message); return result; }

            foreach (string file in files)
            {
                string rel = ToRelative(file);
                if (ScanExclusions.IsExcluded(rel)) continue;
                try { ScanFile(file, rel, result, errors); }
                catch (Exception ex) { errors.Add("扫描 " + rel + " 失败: " + ex.Message); }
            }
            MarkDrawRuns(result); // T6 组C：Draw 常量连续串补组
            return result;
        }

        // P1-7：同文件+同变量+同归一化表达式可能有多条完全相同的语句（如 MirItemCell.cs 两条
        // Draw(1121,…)），仅靠 ordinal（表达式内 token 序号，恒 0）会得到相同 Id → 别名互相污染、
        // 重扫恢复串编辑。扫描时按 (file|libVar|normalized) 计数出现次数，作为 occurrence 混入 Id。
        private readonly Dictionary<string, int> _occurrence = new Dictionary<string, int>();

        private int NextOccurrence(string file, string libVar, string expression)
        {
            string key = file + "|" + libVar + "|" + Reference.Normalize(expression);
            int occ;
            _occurrence.TryGetValue(key, out occ);
            _occurrence[key] = occ + 1;
            return occ;
        }

        /// <summary>
        /// T6 组C：同文件+同 lib 的 Draw 常量串，源码相邻且帧号连续（+1）才合并为一组
        /// （如用 N 条 Draw 各画一帧的写法）。值不连续或调用形态不同则不合并，防误组。
        /// </summary>
        private static void MarkDrawRuns(List<Reference> refs)
        {
            var byFileLib = new Dictionary<string, List<Reference>>();
            foreach (var r in refs)
            {
                if (r.Kind != RefKind.Constant || !r.IsFrameNumber) continue;
                if (!string.IsNullOrEmpty(r.GroupId)) continue; // 组A/组B 已归属
                string key = r.FilePath + "|" + r.LibVariable;
                if (!byFileLib.TryGetValue(key, out var list)) { list = new List<Reference>(); byFileLib[key] = list; }
                list.Add(r);
            }
            foreach (var kv in byFileLib)
            {
                var list = kv.Value;
                int i = 0;
                while (i < list.Count)
                {
                    int j = i;
                    while (j + 1 < list.Count
                        && list[j + 1].NormalizedExpr == list[i].NormalizedExpr // 同一调用形态
                        && list[j + 1].BaseValue == list[j].BaseValue + 1)      // 帧号连续 +1
                        j++;
                    int len = j - i + 1;
                    if (len >= 2)
                    {
                        string gid = "run|" + list[i].FilePath + "|" + list[i].TokenStart;
                        for (int k = i; k <= j; k++)
                        {
                            list[k].GroupId = gid;
                            list[k].GroupStart = list[i].BaseValue; // 连续串等价于 start/count/skip
                            list[k].GroupCount = len;
                            list[k].GroupSkip = 0;
                        }
                    }
                    i = j + 1;
                }
            }
        }

        /// <summary>按打开的 lib 过滤引用（规范 §12：按被调用变量归属）。</summary>
        public static List<Reference> ForLibrary(IEnumerable<Reference> all, LibraryResolveResult lib)
        {
            if (lib == null) return new List<Reference>();
            string var = lib.VariableText;
            var list = new List<Reference>();
            foreach (var r in all)
            {
                if (r.LibVariable == var) { list.Add(r); continue; }
                // 玩家库（数组 AArmours/CArmours/CWeapons/...）共用 Player 帧表（§8.2）
                if (r.PlayerGlobal && IsPlayerLibrary(var)) { list.Add(r); continue; }
                // 全局兜底表单独分组（UI 二次确认），此处不并入普通列表
                if (r.GlobalFallback) continue;
                // 特殊怪物列表帧表：VariableText 含列表名对应的 lib 编号（§8.4）
                if (!string.IsNullOrEmpty(r.SpecialMonsterList) && SpecialListMatchesLib(r.SpecialMonsterList, var)) { list.Add(r); }
            }
            return list;
        }

        private static bool IsPlayerLibrary(string var)
        {
            return Regex.IsMatch(var, @"Libraries\.(AArmours|CArmours|CWeapons|CWeaponEffect|CHair|AHair|AWeapons|AWeaponEffect|AR[A-Za-z]*|CHumEffect|AHumEffect|ARHumEffect)\[");
        }

        private static bool SpecialListMatchesLib(string listVar, string libVar)
        {
            int n;
            switch (listVar)
            {
                case "DragonStatue": n = 902; break;
                case "GreatFoxSpirit": n = 237; break;
                case "HellBomb": return libVar.Contains("Monsters[903]") || libVar.Contains("Monsters[904]") || libVar.Contains("Monsters[905]");
                case "Mon380P": n = 380; break;
                case "Gates": return libVar.Contains("Monsters[950]"); // 沙巴克门仅 950（收窄：旧 Contains("Monsters[95") 误吞 951-959）
                default: return false;
            }
            return libVar.Contains("Monsters[" + n + "]");
        }

        private string ToRelative(string full)
        {
            try { return Path.GetRelativePath(_cfg.RepoRoot, full).Replace('/', '\\'); }
            catch { return full; }
        }

        private void ScanFile(string fullPath, string rel, List<Reference> result, List<string> errors)
        {
            string text = File.ReadAllText(fullPath, Encoding.UTF8);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string method = "";

            foreach (var node in root.DescendantNodes())
            {
                if (node is MethodDeclarationSyntax md) method = md.Identifier.Text;

                // T1.3/T1.6: new Frame(...) 8 参数
                if (node is ObjectCreationExpressionSyntax oc && IsType(oc.Type, "Frame"))
                    ScanFrameCreation(oc, rel, lines, method, result);

                // T1.2: 对象初始化器 Index/HoverIndex/PressedIndex + Library
                if (node is InitializerExpressionSyntax ini && ini.IsKind(SyntaxKind.ObjectInitializerExpression))
                    ScanObjectInitializer(ini, rel, lines, method, result);

                // T1.2: Libraries.X.Draw(...)/DrawBlend(...)
                if (node is InvocationExpressionSyntax inv)
                    ScanLibraryCall(inv, rel, lines, method, result);

                // T1.2: 三元偏移 ArmourOffSet = ... ? 352 : 808
                if (node is AssignmentExpressionSyntax asg && asg.Right is ConditionalExpressionSyntax)
                    ScanOffsetAssignment(asg, rel, lines, method, result);

                // P1-3：普通赋值语句 X.Index = 500 / X.HoverIndex = 1903（CharacterDialog 等大量形态）
                if (node is AssignmentExpressionSyntax asg2 && !(asg2.Right is ConditionalExpressionSyntax))
                    ScanMemberAssignment(asg2, rel, lines, method, result);
            }
        }

        private static bool IsType(TypeSyntax t, string name)
        {
            if (t is IdentifierNameSyntax id) return id.Identifier.Text == name;
            return t != null && t.ToString() == name;
        }

        // ---------- Frame 帧表（Frames.cs）----------
        private void ScanFrameCreation(ObjectCreationExpressionSyntax oc, string rel, string[] lines,
                                       string method, List<Reference> result)
        {
            var args = oc.ArgumentList.Arguments;
            if (args.Count < 4) return; // Frame 至少 4 参
            string exprText = oc.ToString();
            int line = GetLine(oc, lines);

            // 归属判断（文本行辅助 + 语法树祖先）
            string group = DetermineFrameGroup(oc, lines, line); // "Player"/"DefaultXXX"/特殊列表名
            string libVar = "FrameSet." + group;
            bool isPlayer = group == "Player";
            bool isDefault = group == "DefaultNPC" || group == "DefaultMonster" || group == "SpecialMount";
            string specialList = IsSpecialList(group) ? group : null;

            FrameArgKind[] kinds = { FrameArgKind.FrameStart, FrameArgKind.Count, FrameArgKind.Skip,
                FrameArgKind.Interval, FrameArgKind.EffectStart, FrameArgKind.EffectCount,
                FrameArgKind.EffectSkip, FrameArgKind.EffectInterval };

            // T6 组A：同一条 new Frame(...) 语句 = 一组（最强证据）。
            // 第1/2/3参是数字时记录 起始帧/帧数/跳帧，UI 据此精确展开"共 N 帧：s–e"。
            string groupId = "frame|" + rel + "|" + oc.SpanStart;
            int gStart = -1, gCount = 0, gSkip = 0;
            bool rangeOk = args.Count >= 3
                && TryNumber(args[0].Expression, out gStart, out _, out _)
                && TryNumber(args[1].Expression, out gCount, out _, out _)
                && TryNumber(args[2].Expression, out gSkip, out _, out _)
                && gCount > 0 && gSkip >= 0;
            if (!rangeOk) { gStart = -1; gCount = 0; gSkip = 0; }

            int n = Math.Min(args.Count, kinds.Length);
            for (int i = 0; i < n; i++)
            {
                if (!TryNumber(args[i].Expression, out int value, out int start, out int len)) continue;
                var r = new Reference
                {
                    Id = Reference.MakeId(rel, libVar, exprText, i, NextOccurrence(rel, libVar, exprText)),
                    LibVariable = libVar,
                    FilePath = rel,
                    Line = line,
                    Column = GetColumn(oc, lines),
                    TokenStart = start,
                    TokenLength = len,
                    TokenOrdinal = i,
                    Expression = exprText,
                    NormalizedExpr = Reference.Normalize(exprText),
                    BaseValue = value,
                    EditedValue = value,
                    DynamicPart = "",
                    Kind = isDefault ? RefKind.DefaultFrameArg : RefKind.PlayerFrameArg,
                    ArgKind = kinds[i],
                    Target = "Source",
                    PlayerGlobal = isPlayer,
                    GlobalFallback = isDefault && (group == "DefaultNPC" || group == "DefaultMonster"),
                    SpecialMonsterList = specialList,
                    GroupId = groupId,
                    GroupStart = gStart,
                    GroupCount = gCount,
                    GroupSkip = gSkip,
                };
                string mirAction = FindMirAction(oc, lines, line);
                _labels.Apply(r, null, null, mirAction, lines, line, method);
                result.Add(r);
            }
        }

        /// <summary>判断 new Frame(...) 归属的帧表组。返回 Player / DefaultNPC / 特殊列表名。</summary>
        private string DetermineFrameGroup(ObjectCreationExpressionSyntax oc, string[] lines, int line)
        {
            // 祖先法：Player.Add(...) / frame.Add(...)
            SyntaxNode n = oc;
            while (n != null)
            {
                if (n is ArgumentSyntax && n.Parent?.Parent is InvocationExpressionSyntax inv)
                {
                    string recv = (inv.Expression as MemberAccessExpressionSyntax)?.Expression.ToString();
                    if (recv == "Player") return "Player";
                    if (recv == "frame") return FindCurrentSpecialList(lines, line) ?? "frame";
                }
                n = n.Parent;
            }
            // 集合初始化器：{ MirAction.X, new Frame(...) } 属于上面的 X = new FrameSet{...}
            for (int i = line - 1; i >= 0 && i >= line - 3; i--)
            {
                string t = lines[i].Trim();
                var m = Regex.Match(t, @"(\w+)\s*=\s*new\s+FrameSet\s*\{");
                if (m.Success) return m.Groups[1].Value;
            }
            // 同行的 DefaultXXX = new FrameSet { { MirAction..., new Frame... } } 首元素
            string sameLine = line >= 1 && line <= lines.Length ? lines[line - 1] : "";
            var dm = Regex.Match(sameLine, @"(Default\w+|SpecialMount)\s*=\s*new\s+FrameSet");
            if (dm.Success) return dm.Groups[1].Value;
            return "Unknown";
        }

        /// <summary>向上找最近一次 frame = new FrameSet() 归属的特殊怪物列表。</summary>
        private string FindCurrentSpecialList(string[] lines, int line)
        {
            for (int i = line - 1; i >= 0; i--)
            {
                string t = lines[i];
                var m1 = Regex.Match(t, @"(\w+)\.Add\(\s*frame\s*=\s*new\s+FrameSet\s*\(\)");
                if (m1.Success) return m1.Groups[1].Value;
                var m2 = Regex.Match(t, @"(\w+)\s*=\s*new\s+List\s*<\s*FrameSet\s*>");
                if (m2.Success) return m2.Groups[1].Value;
            }
            return null;
        }

        private static bool IsSpecialList(string g)
        {
            return g == "DragonStatue" || g == "GreatFoxSpirit" || g == "HellBomb" || g == "Mon380P" || g == "Gates";
        }

        private static string FindMirAction(ObjectCreationExpressionSyntax oc, string[] lines, int line)
        {
            string scope = "";
            int start = Math.Max(0, line - 2), end = Math.Min(lines.Length - 1, line + 1);
            for (int i = start; i <= end; i++)
            {
                var m = Regex.Match(lines[i], @"MirAction\.([\u4e00-\u9fa5A-Za-z0-9]+)");
                if (m.Success) { scope = m.Groups[1].Value; break; }
            }
            return scope;
        }

        // ---------- 对象初始化器（背包按钮基准）----------
        private void ScanObjectInitializer(InitializerExpressionSyntax ini, string rel, string[] lines,
                                           string method, List<Reference> result)
        {
            string libVar = null, variableName = null;
            foreach (var e in ini.Expressions)
            {
                if (e is AssignmentExpressionSyntax a)
                {
                    string left = a.Left.ToString();
                    if (left == "Library" && TryGetLibrariesVar(a.Right, out string lv, out _)) libVar = lv;
                }
            }
            if (libVar == null) return;

            // 变量名：向上找 new Xxx / Xxx =
            SyntaxNode n = ini.Parent;
            while (n != null && variableName == null)
            {
                if (n is ObjectCreationExpressionSyntax) { }
                else if (n is AssignmentExpressionSyntax aleft) variableName = aleft.Left.ToString();
                else if (n is VariableDeclaratorSyntax vd) variableName = vd.Identifier.Text;
                n = n.Parent;
            }

            int ordinal = 0;
            // T6 组B：同一个对象初始化器（Index/HoverIndex/PressedIndex/DisabledIndex）
            // 是同一个控件的一组状态图（普通/悬停/按下…），换图时通常要一起换。
            string groupId = "ini|" + rel + "|" + ini.SpanStart;
            foreach (var e in ini.Expressions)
            {
                if (e is AssignmentExpressionSyntax a)
                {
                    string member = a.Left.ToString();
                    if (member != "Index" && member != "HoverIndex" && member != "PressedIndex" && member != "DisabledIndex") continue;
                    if (!TryNumber(a.Right, out int value, out int start, out int len)) continue;
                    int line = GetLine(a, lines);
                    string expr = a.ToString();
                    int occ = NextOccurrence(rel, libVar, expr);
                    var r = new Reference
                    {
                        Id = Reference.MakeId(rel, libVar, expr, ordinal, occ),
                        LibVariable = libVar,
                        FilePath = rel,
                        Line = line,
                        Column = GetColumn(a, lines),
                        TokenStart = start,
                        TokenLength = len,
                        TokenOrdinal = ordinal,
                        Expression = expr,
                        NormalizedExpr = Reference.Normalize(expr),
                        BaseValue = value,
                        EditedValue = value,
                        Kind = RefKind.Constant,
                        ArgKind = FrameArgKind.None,
                        Target = "Source",
                        GroupId = groupId,
                    };
                    ordinal++;
                    _labels.Apply(r, variableName, member, null, lines, line, method);
                    result.Add(r);
                }
            }
        }

        // ---------- Libraries.X.Draw/DrawBlend ----------
        private void ScanLibraryCall(InvocationExpressionSyntax inv, string rel, string[] lines,
                                     string method, List<Reference> result)
        {
            if (!(inv.Expression is MemberAccessExpressionSyntax ma)) return;
            string call = ma.Name.Identifier.Text;
            if (!IsFrameMethod(call)) return;
            if (!TryGetLibrariesVar(ma.Expression, out string libVar, out bool ambLib)) return;
            if (inv.ArgumentList.Arguments.Count == 0) return;

            var first = inv.ArgumentList.Arguments[0].Expression;
            int line = GetLine(inv, lines);
            string expr = inv.ToString();

            if (TryNumber(first, out int value, out int start, out int len))
            {
                var r = new Reference
                {
                    Id = Reference.MakeId(rel, libVar, expr, 0, NextOccurrence(rel, libVar, expr)),
                    LibVariable = libVar, FilePath = rel, Line = line, Column = GetColumn(inv, lines),
                    TokenStart = start, TokenLength = len, TokenOrdinal = 0,
                    Expression = expr, NormalizedExpr = Reference.Normalize(expr),
                    BaseValue = value, EditedValue = value,
                    Kind = RefKind.Constant, ArgKind = FrameArgKind.None, Target = "Source",
                    AmbiguousLib = ambLib,
                };
                _labels.Apply(r, null, call, null, lines, line, method);
                result.Add(r);
            }
            else if (TryExtractBase(first, out int baseVal, out int bStart, out int bLen, out string dynamicPart))
            {
                var r = new Reference
                {
                    Id = Reference.MakeId(rel, libVar, expr, 0, NextOccurrence(rel, libVar, expr)),
                    LibVariable = libVar, FilePath = rel, Line = line, Column = GetColumn(inv, lines),
                    TokenStart = bStart, TokenLength = bLen, TokenOrdinal = 0,
                    Expression = expr, NormalizedExpr = Reference.Normalize(expr),
                    BaseValue = baseVal, EditedValue = baseVal, DynamicPart = dynamicPart,
                    Kind = RefKind.Expression, ArgKind = FrameArgKind.None, Target = "Source",
                    AmbiguousLib = ambLib,
                };
                _labels.Apply(r, null, call, null, lines, line, method);
                result.Add(r);
            }
        }

        private static bool IsFrameMethod(string m)
        {
            return m == "Draw" || m == "DrawBlend" || m == "DrawBlendEx" || m == "DrawTinted"
                || m == "DrawShadow" || m == "GetOffSet" || m == "VisiblePixel" || m == "GetImage";
        }

        // ---------- 三元偏移（ArmourOffSet 等）----------
        private void ScanOffsetAssignment(AssignmentExpressionSyntax asg, string rel, string[] lines,
                                          string method, List<Reference> result)
        {
            string member = asg.Left.ToString();
            if (!member.EndsWith("Offset", StringComparison.OrdinalIgnoreCase) &&
                !member.EndsWith("OffSet", StringComparison.OrdinalIgnoreCase)) return;
            int line = GetLine(asg, lines);
            string expr = asg.ToString();
            var cond = (ConditionalExpressionSyntax)asg.Right;
            int ordinal = 0;
            // 遍历三元里所有数字字面量（0/352/808/416/840）
            foreach (var tok in cond.DescendantTokens().Where(t => t.IsKind(SyntaxKind.NumericLiteralToken)))
            {
                if (!int.TryParse(tok.ValueText, out int value)) continue;
                var r = new Reference
                {
                    Id = Reference.MakeId(rel, member, expr, ordinal++),
                    LibVariable = member, FilePath = rel, Line = line, Column = GetColumn(asg, lines),
                    TokenStart = tok.SpanStart, TokenLength = tok.Span.Length, TokenOrdinal = ordinal,
                    Expression = expr, NormalizedExpr = Reference.Normalize(expr),
                    BaseValue = value, EditedValue = value, DynamicPart = "",
                    Kind = RefKind.Expression, ArgKind = FrameArgKind.None, Target = "Source",
                };
                _labels.Apply(r, null, member, null, lines, line, method);
                result.Add(r);
            }
        }

        // ---------- P1-3：普通赋值语句 X.Index = 500 ----------
        // 形态：某变量.Index/.HoverIndex/.PressedIndex/.DisabledIndex = 数字（对象初始化器之外的赋值语句）。
        // 归属：变量名仅作展示线索，lib 归属用「未识别」让 UI 全列表可见（保守：不错误归属到无关库）。
        private void ScanMemberAssignment(AssignmentExpressionSyntax asg, string rel, string[] lines,
                                          string method, List<Reference> result)
        {
            if (!(asg.Left is MemberAccessExpressionSyntax mas)) return;
            string member = mas.Name.Identifier.Text;
            if (member != "Index" && member != "HoverIndex" && member != "PressedIndex" && member != "DisabledIndex") return;
            // 已有分支处理的对象初始化器形态（Index=… 在 ini 内）左值是 SimpleAssignmentExpression 的成员，
            // 这里只处理"语句级"赋值：排除被 ini 分支覆盖的初始化器（其祖先有 InitializerExpression）。
            for (SyntaxNode p = asg.Parent; p != null; p = p.Parent)
                if (p is InitializerExpressionSyntax) return;
            if (!TryNumber(asg.Right, out int value, out int start, out int len)) return;

            int line = GetLine(asg, lines);
            string expr = asg.ToString();
            string owner = mas.Expression.ToString(); // 如 CharacterButton / MiniMapDialog.LightSetting
            // lib 归属未知（该控件用的库在别处定义），标歧义由映射设置/编号下拉兜底。
            string libVar = "Libraries." + owner;
            var r = new Reference
            {
                Id = Reference.MakeId(rel, libVar, expr, 0, NextOccurrence(rel, libVar, expr)),
                LibVariable = libVar, FilePath = rel, Line = line, Column = GetColumn(asg, lines),
                TokenStart = start, TokenLength = len, TokenOrdinal = 0,
                Expression = expr, NormalizedExpr = Reference.Normalize(expr),
                BaseValue = value, EditedValue = value,
                Kind = RefKind.Constant, ArgKind = FrameArgKind.None, Target = "Source",
                AmbiguousLib = true, // 归属待定：仅当用户打开对应 lib 时需手工认领
            };
            _labels.Apply(r, owner, member, null, lines, line, method);
            result.Add(r);
        }

        // ---------- 工具 ----------
        /// <summary>
        /// 解析 Libraries 变量文本。支持两种形态：
        /// ① Libraries.X（普通）→ "Libraries.X"；
        /// ② Libraries.X[N]（数组库，审查 P1-3：TransformWeaponEffect[18].DrawBlend 等几十处）→ "Libraries.X[N]"。
        /// 索引为常量时精确到编号；索引为表达式时标歧义（ambiguous=true，UI 可手工指定编号）。
        /// </summary>
        private static bool TryGetLibrariesVar(ExpressionSyntax expr, out string libVar, out bool ambiguous)
        {
            libVar = null; ambiguous = false;
            if (expr is MemberAccessExpressionSyntax ma && ma.Expression.ToString() == "Libraries")
            {
                libVar = "Libraries." + ma.Name.Identifier.Text;
                return true;
            }
            // Libraries.X[...]：ElementAccessExpression( MemberAccess(Libraries, X) )[arg]
            if (expr is ElementAccessExpressionSyntax ea &&
                ea.Expression is MemberAccessExpressionSyntax ma2 && ma2.Expression.ToString() == "Libraries")
            {
                string name = ma2.Name.Identifier.Text;
                if (ea.ArgumentList.Arguments.Count == 1)
                {
                    var arg = ea.ArgumentList.Arguments[0].Expression;
                    if (TryGetConstInt(arg, out int idx))
                    {
                        libVar = "Libraries." + name + "[" + idx + "]";
                        return true;
                    }
                    // 非常量索引（如 Libraries.Monsters[i]）：歧义库，UI 手工指定编号
                    libVar = "Libraries." + name;
                    ambiguous = true;
                    return true;
                }
                libVar = "Libraries." + name;
                ambiguous = true;
                return true;
            }
            return false;
        }

        /// <summary>严格常量整数表达式（含一元负号），供数组索引解析。</summary>
        private static bool TryGetConstInt(ExpressionSyntax e, out int value)
        {
            value = 0;
            if (e is LiteralExpressionSyntax le && le.Token.IsKind(SyntaxKind.NumericLiteralToken))
                return int.TryParse(le.Token.ValueText, out value);
            if (e is PrefixUnaryExpressionSyntax pu && pu.IsKind(SyntaxKind.UnaryMinusExpression) &&
                pu.Operand is LiteralExpressionSyntax le2 && le2.Token.IsKind(SyntaxKind.NumericLiteralToken))
            {
                if (int.TryParse(le2.Token.ValueText, out value)) { value = -value; return true; }
                return false;
            }
            return false;
        }

        /// <summary>提取表达式首个常量基址（如 1820 + Direction*10 → 1820，动态项保留）。</summary>
        private static bool TryExtractBase(ExpressionSyntax expr, out int baseVal, out int start, out int len, out string dynamic)
        {
            baseVal = 0; start = 0; len = 0; dynamic = "";
            foreach (var d in expr.DescendantNodesAndSelf())
            {
                if (TryNumber(d as ExpressionSyntax, out baseVal, out start, out len))
                {
                    dynamic = expr.ToString().Substring(start + len - expr.Span.Start).Trim();
                    return true;
                }
            }
            return false;
        }

        private static bool TryNumber(ExpressionSyntax e, out int value, out int start, out int len)
        {
            value = 0; start = 0; len = 0;
            if (e == null) return false;
            if (e is LiteralExpressionSyntax le && le.Token.IsKind(SyntaxKind.NumericLiteralToken))
            {
                if (!int.TryParse(le.Token.ValueText, out value)) return false;
                start = le.Token.SpanStart; len = le.Token.Span.Length; return true;
            }
            if (e is PrefixUnaryExpressionSyntax pu && pu.IsKind(SyntaxKind.UnaryMinusExpression) &&
                pu.Operand is LiteralExpressionSyntax le2 && le2.Token.IsKind(SyntaxKind.NumericLiteralToken))
            {
                if (!int.TryParse(le2.Token.ValueText, out value)) return false;
                value = -value; start = pu.SpanStart; len = pu.Span.Length; return true;
            }
            return false;
        }

        private static int GetLine(SyntaxNode node, string[] lines)
        {
            var pos = node.GetLocation().GetLineSpan().StartLinePosition;
            return pos.Line + 1;
        }

        private static int GetColumn(SyntaxNode node, string[] lines)
        {
            var pos = node.GetLocation().GetLineSpan().StartLinePosition;
            return pos.Character + 1;
        }
    }
}
