# LibraryEditor 源码帧号反向索引 — 功能规范

日期：2026-11-18
版本：v1.6（特殊怪物帧表归属复核版）
状态：规范定稿（开发前）
项目：`C:\1.40\mir2-angelk727`

> v1.6 修订：特殊怪物帧表归属复核——5 类特殊怪物（GreatFoxSpirit=237、Mon380P=380、DragonStatue=902、HellBomb=903/904/905、SabukGate=950）在 `MonsterObject.cs:356-372` 用 `FrameSet.XXX[...]` **直接覆盖 Frames，走不到 default 分支的 `BodyLibrary.Frames ?? DefaultMonster`（:376），即忽略 lib 自带 Frames 段**。原 §8.2「打开 Monster\079.Lib 显示该库自带 Frames 段」对这 5 类 lib 是错的。补 §8.4：这 5 类 lib 须显示/编辑源码 `FrameSet.XXX` 列表帧表（Target=Source），标注"此怪物忽略 lib 自带帧表"，列表下标由 Direction/Stage/BaseImage-903/BaseImage-950 决定，编辑时勿改下标结构。详见§15。
>
> v1.5 修订：磁盘事实复核补两个缺口——① **带后缀数组库映射**（§7）：`InitLibrary` 第 4 参数 suffix（`" L"`/`" R"`/`" S"`）对应 `NN L.Lib` 等带空格文件名（实测 102 个），反查须拆"编号+后缀"，否则 `AWeaponsL`/`ARWeaponsS` 扫描不到引用；② **编译成功判定**（§10.3）：退出码 0 且错误数 0 即成功，**警告≠失败**——基线自带 1 个 `MSB3277` 警告须记入白名单，已实测编译成功（0 错误/1 警告）。详见§15 修订记录。
>
> v1.4 修订：端到端走查补一处逻辑缺口——**"保存改动"须按实际变更载体决定是否编译/换件**（§10.4）：① 客户端进程门禁提前到**任何写操作之前**（否则客户端运行时 lib 写回已失败却误报"编译成功未部署"）；② 仅 LibFrames 变更（无 `.cs` 改动）时跳过编译与换件、状态矩阵标"跳过"而非"✓"。详见§15 修订记录。
>
> v1.3 修订：补两个真实缺陷——① **玩家帧表被所有玩家库共用**（`PlayerObject.cs:121`），改一个影响全部，须标注"玩家全局"+二次确认；② **`Id` 必须用归一化表达式模式**（数字→`<N>`），否则改帧号后 Id 漂移、破坏幂等与别名；③ 明确选帧缩略图的取图来源（源码引用/本库/玩家共用/全局兜底四种）。详见§15 修订记录。
>
> v1.2 修订：补充**编译后部署到运行目录**（否则用户看不到效果，致命遗漏）、两套 FrameSet 归属澄清、MirAction 枚举来源、标签优先级修正、别名 key 示例矛盾修正。

---

## 1. 一句话目标

在 LibraryEditor 中打开任意一个 `.Lib`，工具自动扫描客户端源码，把**所有使用这个库帧号的代码位置**以"人话 + 可编辑帧号"的形式列出；用户可视化选帧、改帧号、保存，工具自动把改动写到**正确的载体**（源码或 lib 自带帧表）并编译，全程不需要打开代码编辑器。

## 2. 用户意图（原话要点）

- "以 lib 为中心做反向索引——打开一个 .Lib，工具自动扫描客户端源码，把所有'用到这个 lib 的帧号'的地方挖出来"
- "翻译成人话（'背包按钮 = 1903'），1903 帧号可编辑、可从 lib 里可视化选帧，比如我换成 1900 号"
- "点保存后工具自动回写源码并编译，你全程不打开代码编辑器"
- "一定要人话像这种背包按钮，一目了然，要不然真弄不清是干啥的"
- "增加重编译按钮……编译后要直观显示编译结果，有无出现错误或者警告等异常情况，为排查问题提供帮助"

## 3. 范围

### 3.1 In Scope（本期做）

1. 打开 lib → 自动识别它对应哪个源码 Library 变量。
2. 扫描客户端 `Client\**\*.cs`，索引所有对该 Library 的帧号引用。
3. 每条引用以"人话标签 + 可编辑帧号"呈现，帧号可从当前 lib 的缩略图可视化选取。
4. 保存：精确回写源码中的数字 token（保留其余代码、注释、中文编码）；怪物/NPC 的生效帧表写回 lib 的 Frames 段。
5. 重编译按钮：执行 `dotnet build`，直观展示成功/失败、错误数、警告数与逐条日志。
6. 用户可维护的人话别名，持久化且行号漂移后仍有效。

### 3.2 Out of Scope（本期不做）

1. 不改动 `.Lib` 的像素数据来响应源码帧号变更（源码引用只改源码；lib 自带帧表只写 lib）。
2. 不自动推断运行时动态选库（`BodyLibrary` 等）的精确编号——标记为"作用于目录"并提供手工指定编号。
3. 不做跨项目扫描，只扫本仓库 `Client` 工程。
4. 不做 Git 提交；只做备份与 diff 预览。
5. **不覆盖 MapLibs 地图库（v1.6 补，详见§7.7）**：`MLibrary.cs:49` MapLibs 为固定下标赋值、文件名无数字编号模式（Tiles/Objects1..20 等字符串拼接），不适用"编号+后缀"反查；其引用在 `tools\MapEditor`。本期不做其映射与帧号编辑。

## 4. 关键事实核查（设计依据）

| 事实 | 证据 |
|---|---|
| lib 文件内部**没有**跨库引用 | `Graphics/MLibraryV2.cs:99-140` Save 写入：version/count/frameSeek/offsets/图片(FBytes+XY+阴影+掩码)/本库 Frames；`MImage.Save():488-508` 仅像素数据。无库名/路径/外部指针字段 |
| lib ↔ 源码变量有确定映射 | `Client/Settings.cs:32-67` 路径常量；`Client/MirGraphics/MLibrary.cs:94-134` `InitLibrary(ref AArmours, Settings.AArmourPath, "00")` |
| 客户端可命令行编译 | `Client/Client.csproj` 为 SDK 风格 `net8.0-windows7.0`，`BaseOutputPath=..\Build\Client\` |
| 引用形式分四类 | 常量 `Libraries.Prguse.Draw(1121, …)`/`Index=1903`；表达式 `1820 + Direction*10 + FrameIndex`；动作表 `new Frame(0,4,0,500,…)`；间接 `BodyLibrary.Draw(DrawFrame + ArmourOffSet, …)` |
| 玩家帧表硬编码不读 lib | `PlayerObject.cs:121` `Frames = FrameSet.Player`；`Frames.cs:14+` 逐行 `Player.Add(MirAction.X, new Frame(...))` |
| 怪物/NPC 帧表**读 lib 的 Frames 段** | `MirGraphics/MLibrary.cs:583-596`；`MonsterObject.cs:464` / `NPCObject.cs:101` 用 `BodyLibrary.Frames ?? 默认表` |
| `Frames.cs` 的 `DefaultNPC`/`DefaultMonster` 是**全局兜底表**，不是某个 lib 的属性 | `Frames.cs:17-30` 静态初始化；客户端仅在 lib 无 Frames 时回退 |
| 间接引用无法静态定编号 | `PlayerObject.cs` 中 `BodyLibrary = Libraries.AArmours[Armour]` 等运行时赋值 |
| 单行可含多个可改 token | `Frames.cs:19-20` `{ MirAction.站立动作, new Frame(0,4,0,500) }, { MirAction.展示采集, new Frame(12,10,0,200) }` 在同一行 |
| **lib 文件只在运行目录** | 源码仓库 `mir2-angelk727\Client\Data` **不存在**；lib 实际在 `mir2-deploy\Client\Data`。故 lib 读写直接针对运行目录，**lib 本身不需要换件**；换件仅针对编译产物 7 件 |
| **编译产物需换件到运行目录** | `doc/20261110修复物品悬浮窗被鼠标遮挡.md:31,42`：每次改客户端需把 7 件（Client.exe/dll/pdb/deps.json/runtimeconfig.json/Shared.dll/Shared.pdb）从 `Build\Client\Release\` 复制到 `mir2-deploy\Client\`；客户端运行中无法换件，须先关闭 |
| **源码为 UTF-8 BOM + CRLF** | `MainDialogs.cs` 前 3 字节 `EF BB BF`，4731 个 CRLF = 4731 个 LF（全 CRLF）；写回必须保 BOM 和 CRLF |
| **数组库变量名清单** | `MLibrary.cs:62-133`：`CArmours/CHair/CWeapons/CWeaponEffect/CHumEffect/AArmours/AHair/AWeaponsL/AWeaponsR/.../Monsters(000)/Gates/Flags/Siege/NPCs/Mounts/Fishing/Pets/Transform...`，均 `00`/`000` 编号；部分带后缀（`" L"`/`" R"`/`" S"`） |
| **v3 lib 帧表读写双向支持已确认** | `MLibraryV2.cs:27,88` 读 Frames；`:135-139` Save 写 Frames.Keys.Count + (byte)action + Frame.Save；写回技术上可行 |
| **编辑器「自动填充」按数字名映射怪物帧表** | `LMain.cs:1146` `GetFrameSetByImage((Monster)imageNumber)` 按 lib 数字名转 Monster 枚举；但 `:1350-1353` 当前为 `NotImplementedException` 占位——本工具**不依赖**此函数，自行实现映射 |

## 5. 术语

- **帧号（FrameNo）**：lib 内图片的从 0 开始的索引。
- **引用点（Reference）**：源码中一个使用帧号的位置，含 lib 变量、帧号表达式、文件:行、人话标签。
- **人话标签（Human Label）**：一眼能懂的中文说明，如"背包按钮-普通态""站立动作-起始帧"。
- **基址（Base）**：表达式中可安全修改的常量部分（`1820 + Direction*10` 中的 `1820`）；`+ Direction*10` 为"动态项"，不可改。
- **映射（Mapping）**：磁盘 lib 路径模式 → 源码 Library 变量（含数组下标）。
- **载体（Target）**：改动的最终去处——`Source`（源码文件）或 `LibFrames`（lib 自带帧表）。

## 6. 数据模型

```
Reference {
  string  Id;              // 稳定签名哈希：文件 + 变量名 + 归一化表达式模式 + token 序号（不含行号、不含具体帧号数字）
  string  LibVariable;     // Libraries.Prguse / Libraries.AArmours[NN]
  string  FilePath;        // 相对仓库根
  int     Line, Column;    // 当前扫描到的帧号 token 位置（1-based）
  int     TokenStart, TokenLength; // 本次替换范围（字符偏移）
  int     TokenOrdinal;    // 同一行内第几个可改 token（用于从右到左替换）
  string  Expression;      // 原始表达式文本
  int     BaseValue;       // 可编辑常量基址
  string  DynamicPart;     // "+ Direction * 10 + ..." 保留项
  string  HumanLabel;      // 人话标签
  string  LabelSource;     // 别名/变量名/枚举/注释/本地化Key/兜底
  RefKind Kind;            // Constant | Expression | PlayerFrameArg | DefaultFrameArg | LibFrames | Indirect
  FrameArgKind ArgKind;    // FrameStart | EffectStart | Count | Skip | Interval | ...（仅 Frame 类）
  string  Target;          // "Source" | "LibFrames"
  bool    AmbiguousLib;
}
```

`AliasConfig`（持久化到 `LibraryEditor\Config\FrameAliases.json`）：
```
{ "Client/MirObjects/PlayerObject.cs|DrawBlend|Libraries.Magic#3": "火球术-绘制基址" }
```
key 由 `相对路径 | 方法/成员名 | Library变量 | 出现序号` 组成，**不含行号**（示例中的 `#3` 是该签名下的第 3 次出现，不是行号；v1.1 误写为 `#5527` 易被当成行号，v1.2 修正）；重新扫描时按签名匹配，行号漂移后别名仍有效。

> **归一化表达式模式（v1.3 明确）**：`Id` 里的"表达式模式"必须是**把所有可改数字 token 替换为占位符 `<N>` 后的结构串**，例如 `DrawBlend(1820 + Direction*10 + FrameIndex, …)` 归一化为 `DrawBlend(<N> + Direction*<N> + FrameIndex, …)`。这样用户把帧号 1820 改成 1900 后 Id 不变、幂等（§11.3）成立。**禁止用原始表达式文本 `Expression` 直接做 Id**——那会因改帧号而改变。（别名 key 本身不含表达式模式，用"出现序号"，改帧号不影响它；这里约束的是 Id。）

## 7. 映射规则

1. 读 `Client\Settings.cs` 提取 `XxxPath = @".\Data\Yyy\"`。
2. 读 `Client\MirGraphics\MLibrary.cs` 提取：
   - 单文件：`Prguse = new MLibrary(Settings.DataPath + "Prguse")` → `Data\Prguse.Lib` ↔ `Libraries.Prguse`
   - 数组：`InitLibrary(ref Monsters, Settings.MonsterPath, "000")` → `Data\Monster\NNN.Lib` ↔ `Libraries.Monsters[NNN]`
   - **数组带后缀（v1.5）**：`InitLibrary(ref AWeaponsL, Settings.AWeaponPath, "00", " L")` → `Data\AWeapon\NN L.Lib` ↔ `Libraries.AWeaponsL[NN]`（第 4 参数 suffix 含前导空格，`" L"`/`" R"`/`" S"`；同理 `AWeaponEffectL/R`、`ARWeaponsS`、`ARWeaponsEffectS`）。实测运行目录有 102 个带空格后缀文件（如 `ARWeapon\00 S.Lib`）。
3. 打开 lib 按路径反查变量；数组库保留 `NN` 占位用于引用匹配与"指定编号"下拉。**反查必须能拆分"数字编号 + 后缀"**：从 lib 文件名提取 `NN` 与 suffix，回查 `InitLibrary(..., path, digits, suffix)` 四参数签名，才能正确反查到 `AWeaponsL`/`ARWeaponsS` 而非笼统的 `AWeapons`；否则这类库扫描不到任何引用（真实缺口，v1.5）。
4. 映射表可在设置中人工修正（应对新增库/改名）；修正持久化到 `Config\LibraryMapOverrides.json`。
5. **扫描排除**：`Client\obj\`、`Client\bin\`、`Build\`、`**\.git\`、`*.Designer.cs` 中由设计器生成的纯 UI 资源代码仍扫描（控件帧号在其中），但排除彻底生成文件 `GlobalUsings.g.cs`、`*.AssemblyInfo.cs`。
6. **映射按字符串字面量解析，变量名 ≠ 文件名（v1.6 补）**：`MLibrary.cs:16-56` 存在变量名与磁盘文件名不一致的库——`MiniMap = new MLibrary(Settings.DataPath + "MMap")`（文件 `MMap.Lib`）、`StateItems`→`StateItem.Lib`、`FloorItems`→`DNItems.Lib`。映射解析必须按 `X = new MLibrary(<path表达式> + "Y")` 的**字符串字面量 Y** 取文件名，**禁止用变量名 X 猜文件名**，否则这三类库反查必失败。同理反查时也按字面量回查（全仓初始化清单以 `MLibrary.cs:16-133` 为准）。
7. **MapLibs 地图库边界（v1.6 补）**：`MLibrary.cs:49` `public static readonly MLibrary[] MapLibs = new MLibrary[400]`，在 `:137-172+` 按固定下标赋值，路径 `Data\Map\WemadeMir2|ShandaMir2|WemadeMir3\...`，文件名为字符串（`Tiles`/`Smtiles`/`Objects`/`Objects1..20`/`Objects_32bit`/`AniTiles1` 等，WemadeMir3 用 `Mapstate[i]` 数组拼接），**不经过 InitLibrary、无数字编号模式**；其引用主要在 `tools\MapEditor`（不在 Client 扫描范围）。**列入 Out of Scope**：本工具不提供 MapLibs 的映射与帧号编辑；若后续要支持，须单独立项（文件名无统一编号规则，反查策略完全不同）。（另见 §3.2 Out of Scope 同步条目）

## 8. 引用分类与识别规则

| Kind | 形态 | Target | 处理 |
|---|---|---|---|
| Constant | `Libraries.X.Draw(1121, …)`、`Index = 1903` | Source | 直接替换数字 token |
| Expression | `DrawBlend(1820 + Direction*10 + FrameIndex, …)` | Source | 只替换基址常量，保留动态项；UI 标注"动态项勿动"并预览各朝向实际帧 |
| PlayerFrameArg | `Player.Add(MirAction.X, new Frame(...))` | Source | 按 ArgKind 分别编辑；仅 FrameStart/EffectStart 提供选帧按钮 |
| DefaultFrameArg | `DefaultNPC/DefaultMonster/SpecialMount = new FrameSet{...}` | Source | 同上；UI 标注"全局兜底表，影响所有无自带帧表的库" |
| LibFrames | lib v3 自带 Frames 段 | LibFrames | 编辑 `_library.Frames`，保存写回 lib，**不改源码** |
| Indirect | `BodyLibrary.Draw(DrawFrame + ArmourOffSet, …)` | Source（歧义） | 跟踪赋值链到 `Libraries.AArmours[NN]`；标 `AmbiguousLib=true`，UI 提示"作用于整个 AArmour 目录"并给编号下拉 |

### 8.1 `new Frame(...)` 的 8 个数字——必须分类，不可一律当帧号

签名：`Frame(int start, int count, int skip, int interval, int effectstart=0, int effectcount=0, int effectskip=0, int effectinterval=0)`

| 位置 | 参数 | 是否帧号 | UI 控件 |
|---|---|---|---|
| 1 | `start` | ✅ 帧号 | 可输入 + 选帧按钮 |
| 2 | `count` | ❌ 帧数 | 整数输入（标注"连续几帧"） |
| 3 | `skip` | ❌ 跳帧数 | 整数输入 |
| 4 | `interval` | ❌ 毫秒 | 整数输入（标注"每帧毫秒"） |
| 5 | `effectstart` | ✅ 帧号 | 可输入 + 选帧按钮 |
| 6 | `effectcount` | ❌ 帧数 | 整数输入 |
| 7 | `effectskip` | ❌ 跳帧数 | 整数输入 |
| 8 | `effectinterval` | ❌ 毫秒 | 整数输入 |

`OffSet = count + skip`（客户端 `Frames.cs:238-241`），UI 在编辑 count/skip 时联动显示"每朝向占用帧数 = count+skip"。

### 8.2 怪物/NPC 的生效帧表归属（关键逻辑）

- 打开 `Monster\079.Lib` → 显示该库**自带的 Frames 段**（Target=LibFrames，写回 lib）；
- `Frames.cs` 的 `DefaultMonster` 是兜底表，**不属于 079.Lib**，仅在 079.Lib 没有 Frames 时生效。它列入"全局兜底"分组，编辑需二次确认，且标注"影响所有无自带帧表的怪物库"。
- 打开玩家库（AArmour/CArmour 等）→ 显示 `Player.Add(...)`（Target=Source，写源码）；**不显示** `DefaultNPC/DefaultMonster`。
- **玩家帧表被所有玩家库共用（v1.3 补）**：`PlayerObject.cs:121` 中每个玩家对象都 `Frames = FrameSet.Player`，所以 AArmour/CArmour/CWeapon/AHair… 等**全部玩家库共用同一份 `Player.Add` 源码帧表**。改任意一个玩家库的帧表 = 改所有玩家库。UI 打开玩家库时必须显示醒目提示"此帧表被所有玩家装备库共用，修改影响全部玩家"，编辑该项需二次确认；标签后缀加"（玩家全局）"以区别于怪物/NPC 的"本库独立"帧表。

### 8.3 两套 FrameSet 的归属（v1.2 澄清，勿混用）

项目里有**两套不同命名空间、数值也不同**的帧表，工具必须分清，否则会改错文件：

| 帧表 | 定义位置 | 谁使用 | 本工具如何对待 |
|---|---|---|---|
| `DefaultMonsterFrameSet` / `DefaultNPCFrameSet` | `LibraryEditor\Graphics\Frames.cs` | 编辑器「动作填充」写进 lib 的 Frames 段（LMain.cs:1076/1084） | 这是**编辑器自己的写入模板**，不是扫描目标；本工具不修改它 |
| `DefaultNPC` / `DefaultMonster` / `Player` / `SpecialMount` | `Client\MirObjects\Frames.cs`（`MirAction` 枚举在 `Shared\Enums.cs:876`） | 客户端运行时兜底（`Player` 更是 `PlayerObject.cs:121` 硬编码） | 这是**Client 源码**，属于扫描/回写目标（Target=Source） |

两者数值不同（例：怪物行走间隔 编辑器 100 vs 客户端 200；NPC 站立 编辑器 450 vs 客户端 500）。**Frame 类定义在 Client 项目**（`Client\MirObjects\Frames.cs`），但 **`MirAction` 枚举定义在 Shared 项目**（`Shared\Enums.cs:876`，全仓唯一定义），扫描器取枚举成员名做标签时引用 Shared 那份，不要误用编辑器那套 FrameSet。

### 8.4 特殊怪物：忽略 lib 自带帧表，帧表在源码 `FrameSet.XXX` 列表（v1.6 新增）

以下 5 类怪物在 `MonsterObject.cs:356-372` 的 switch 里用 `Frames = FrameSet.XXX[...]` **直接赋值**，不走 default 分支（:376 `Frames = BodyLibrary.Frames ?? FrameSet.DefaultMonster`），因此**它们忽略对应 Monster\NNN.Lib 自带的 Frames 段**。打开这些 lib 时，§8.2 的"显示本库自带 Frames 段"不适用。

| 怪物 | 枚举值=lib 编号 | 源码帧表 | 下标来源 |
|---|---|---|---|
| GreatFoxSpirit | 237 | `FrameSet.GreatFoxSpirit[Stage]` | Stage |
| Mon380P | 380 | `FrameSet.Mon380P[(byte)Direction]` | 方向 |
| DragonStatue | 902 | `FrameSet.DragonStatue[(byte)Direction]` | 方向 |
| HellBomb1/2/3 | 903/904/905 | `FrameSet.HellBomb[BaseImage-903]` | BaseImage |
| SabukGate | 950 | `FrameSet.Gates[BaseImage-950]` | BaseImage |

处理规则：
- 打开上述 5 类 Monster\NNN.Lib → 显示并编辑源码里的 `FrameSet.XXX` 列表帧表（Target=Source，写回 `Client\MirObjects\Frames.cs`），**不显示/不编辑 lib 自带 Frames 段**。
- UI 标注"此怪物忽略 lib 自带帧表，实际帧表在源码列表"；标签格式如"石像鬼-方向3 / 九尾狐-阶段2 / 地狱炸弹-903"。
- 这些帧表用 `frame.Add(...)` 局部变量形态定义（Frames.cs:38-170），扫描器须识别"局部变量 `frame` 在 `new List<FrameSet>{...}` / `List.Add(frame = new FrameSet())` 中赋值"的模式，归属到对应 `FrameSet.XXX` 列表——不能只认 `Player.Add` / `DefaultNPC=...` 两种形态。
- 列表**下标是运行时按 Direction/Stage/BaseImage 动态选取**，编辑时只改 `new Frame(...)` 里的帧号，勿改列表元素个数/顺序，否则下标错位。

## 9. 人话标签生成规则（优先级从高到低）

1. **用户别名**：`AliasConfig` 命中 → 直接用。
2. **控件变量名词典**（v1.2 提升优先级）：按 `XxxButton/MirXXX` 变量名翻译，`InventoryButton` → "背包按钮"、`CharacterButton` → "人物按钮"、`SkillButton` → "技能按钮"；词典内置常见控件名，缺失项可在别名管理补。这是"一目了然"的主力——用户明确要求"背包按钮"式标签，不要只显示 `ClientTextKeys.InventoryKey` 这种代码符号。
3. **MirAction 枚举**：`MirAction.站立动作` → "站立动作"。
4. **控件状态后缀**：`Index/HoverIndex/PressedIndex` → "-普通态/悬停态/按下态"。
5. **本地化 Key**（降级为辅助）：`ClientTextKeys.XxxKey` → 查 `GameLanguage` 映射取中文（如"物品栏（{0}）"）；与变量名词典冲突时**以变量名词典为准**。
6. **邻近注释**：上方 3 行内中文注释。
7. **上下文方法名**：`SpellObject`/技能分支 → "魔法-<技能名>"（取不到用行号）。
8. **兜底**：`<文件名>:<行号> 的帧引用（原表达式）`。

规则：标签必须含"什么东西 + 什么状态/动作"，禁止只显示变量名或裸数字、禁止只显示 `ClientTextKeys.*` 代码符号。无法生成合格标签时，UI 标灰并要求用户指定别名后才允许保存（防止"改了不知道改的是啥"）。`MainDialogs.cs:66-76` 的 InventoryButton 必须显示为"背包按钮-普通态/悬停态/按下态"三行，作为标签质量的验收基准样例。

## 10. UI 规范

### 10.1 主界面（打开 lib 后）

- 顶部：lib 路径、识别到的 Library 变量、总帧数、映射来源。
- **帧带时间轴**：`0 … Count-1` 横向色带，每个引用点占一段并标注人话标签；颜色按 Kind 区分；点击段 → 定位引用行。
- **引用列表**（DataGridView）：列 = 人话标签 / 原表达式 / 当前值（可编辑）/ 来源(文件:行) / 类型 / 载体 / 歧义标记。
- 编辑控件：
  - 帧号类：数值输入 + 选帧按钮；点选帧弹出 lib 缩略图网格（带帧号），选中回填。选帧来源（v1.3 明确）：① 源码引用/本库帧表 → 从**当前打开的 lib** 取缩略图；② 玩家共用帧表（`Player.Add`）→ 从当前打开的玩家库取，但必须同时显示"全局共用"警告；③ 全局兜底表（`DefaultNPC/DefaultMonster`）→ 不属于任何具体 lib，选帧按钮默认从当前 lib 取，但 UI 标注"此值用于所有无自带帧表的库，请用代表性库的帧号"，并允许切换参考库。
  - 非帧号数字：普通整数输入，带参数名提示（如"连续帧数"）。
  - 表达式行：基址可编辑，动态项只读灰显；旁注"实际帧 = 基址 + 动态项"，并给出方向 0/4 的示例值。
- 分组：① 源码引用（Target=Source）② 本库自带帧表（Target=LibFrames）③ 全局兜底表（需二次确认）。
- 按钮：`扫描/刷新` `保存改动` `重编译` `部署` `查看差异` `撤销` `别名管理` `映射设置`。（v1.2：`重编译`只编译，`部署`只换件，`保存改动`=写回+编译+部署一条龙；三者分离便于排障，见§10.4）

### 10.2 保存流程（原子性）

1. 校验：所有值为整数；帧号在 `0..Count-1`；动态项未被改；无未命名标签。
2. 生成变更清单（旧值→新值，含人话标签、文件:行、TokenOrdinal）。
3. 备份：
   - `.cs` 源码 → `LibraryEditor\Backups\<时间戳>\<相对路径>\`
   - `.Lib` → 同备份目录，扩展名加 `.bak`
   - 保留最近 10 次备份；更早的清理需用户确认（禁止自动删除）。
4. 弹出 diff 预览（人话标签 + 旧代码行 + 新代码行），用户确认。
5. **同行多 token 替换必须按 `TokenOrdinal` 降序（从右到左）执行**，避免前一个替换改变后面 token 的列偏移。替换后校验该行 token 数量未变、非目标 token 文本未变。
6. 逐文件原子写：写临时文件 → 校验编码/行数 → `File.Replace`/`Move` 覆盖；失败即回滚本次已写文件。
7. lib Frames 写回：`MLibraryV2.Save()` 当前直接 `File.Create(FileName)`，**实现时必须改为先写临时文件再原子替换**，并用备份做失败恢复；大库（>100MB）写回前显示进度与二次确认。
8. 保存别名配置与映射覆盖配置。
9. **多载体独立原子、部分成功要明确展示**（v1.2 补）：源码写回、lib Frames 写回是各自独立的原子操作，随后才编译/部署。任一步失败**不跨载体回滚**（源码已改就不会因编译失败而退回），但 UI 必须逐项显示"源码 ✓ / lib ✓ / 编译 ✗ / 部署 未执行"的状态矩阵，让用户清楚哪部分生效、哪部分没生效，并提供"回到备份"统一回滚入口。禁止出现"看似全成功实则某载体没写"的静默部分成功。

### 10.3 重编译与结果展示

- 命令：`dotnet build <repo>\Client\Client.csproj -c Release --nologo`（工作目录 = 仓库根）。
- 输出捕获：stdout + stderr，保留完整文本与时间戳；异步读取，超时 10 分钟可取消。
- 解析规则：`error CS\d+` / `warning CS\d+` / `error MSB\d+` / `warning MSB\d+`，提取 文件(行,列)、编号、消息；无法归类的行归入"其他信息"。
 - **成功判定与基线警告（v1.5 明确）**：`编译成功` 的判据是**错误数 == 0**，**警告不阻断成功**（`0 错误 / N 警告` 仍为"编译成功"，黄底计数提示，不弹失败告警）。实测当前客户端基线编译即带 **1 个 `MSB3277`（WindowsBase 版本冲突）警告、0 错误**（2026-11-18 `dotnet build -c Release` 实测，属既有工程依赖警告，非本次改动引入）。实现须把基线警告列入"已知警告白名单"（按编号+消息前缀匹配），结果展示中标注"既有警告"，"新增警告"只统计白名单外的新增项，避免把基线噪声误报为本次改动的问题。
- 异常场景必须处理：
  - `dotnet` 不在 PATH / 启动失败 → 显示"编译工具不可用"及完整异常。
  - 超时 → 显示"编译超时"并保留已捕获输出。
  - MSB 错误（项目/依赖问题）→ 与 CS 错误同样可视化，不丢弃。
  - 进程非零退出但无 error 行 → 显示"退出码 N，未解析到错误"，并展开原始输出。
- 展示：
  - 顶部大字结论：`编译成功` / `编译失败`，绿/红配色。
  - 计数：`错误 N / 警告 N / 其他 N`。
  - 列表：级别 + 文件:行 + 编号 + 消息；点击在本工具内只读预览源码。
  - 过滤器：全部 / 仅错误 / 仅警告；搜索框。
  - 原始输出文本框（可复制）。
  - 可选增强：与上次编译结果对比，标注"新增警告 N"（不作为验收硬性项）。
- 编译失败**不回滚**源码改动；提供"回到备份"入口。

### 10.4 部署到运行目录（v1.2 新增，致命环节）

**为何必须做**：源码在 `mir2-angelk727`，运行的客户端在 `mir2-deploy\Client`。编译只更新 `Build\Client\Release\`，**不换件则用户看不到任何效果**——点"保存"后编译成功却一格没变，这是功能性失败，不是可选增强。

**换件清单（7 件，历史文档 `20261110修复物品悬浮窗被鼠标遮挡.md:31,42` 确认）**：

| 文件 | 源 | 目标 |
|---|---|---|
| Client.exe / Client.dll / Client.pdb / Client.deps.json / Client.runtimeconfig.json / Shared.dll / Shared.pdb | `Build\Client\Release\` | `C:\1.40\mir2-deploy\Client\` |

**lib 不在换件清单**：源码仓库无 `Client\Data`；lib 只在运行目录 `mir2-deploy\Client\Data`。写 lib 帧表（LibFrames）直接改运行目录文件即可，**不复制 lib**。

**部署前置条件与安全**：
1. 检测客户端进程（Client.exe）是否运行；**运行中禁止换件**，提示"请先关闭游戏客户端"并中止（历史文档确认运行中无法覆盖）。
2. 覆盖前把目标 7 件备份到 `LibraryEditor\Backups\<时间戳>\deploy\`（遵循§10.2 保留策略）。
3. 逐件复制；任一失败立刻用备份回滚已复制的文件。
4. 校验：复制后比对 7 件大小与时间戳，全部一致才算部署成功。
5. 路径白名单：源仅限 `Build\Client\Release\`，目标仅限 `mir2-deploy\Client\`；禁止写别处。

**"保存"按钮的完整链路**（顺序不可颠倒）：
```
校验 → [客户端进程门禁：需写 lib 或换件时，在任何写操作之前检测，运行中即中止] →
备份源码/lib → diff 确认 → 写源码 token + 写 lib Frames →
[本次无源码变更] 跳过编译与换件 → 提示"lib 帧表已写入，请重启客户端" → 结束
[本次有源码变更] 编译(dotnet build) →
    → [成功] 换件 7 件 → 校验 → 提示"已生效，请重启客户端"
    → [失败] 停止换件，展示编译错误，源码保留，给"回到备份"入口
```

- **客户端进程检测时机（v1.4 修正）**：门禁必须在**任何写操作之前**执行——不仅换件需要客户端关闭，lib 帧表写回同样需要（§11.12）。若等到"编译成功后"才检测，客户端运行时 lib 写回早已失败，却误报"编译成功未部署"，掩盖真正原因。故保存链路开头先检测；客户端未关则直接中止，不写任何文件、不编译。
- **按变更载体决定是否编译/换件（v1.4 修正）**：编译只更新源码产物，换件只复制这 7 件产物。当本次变更**全部是 LibFrames**（如只改怪物库动作表，无 `.cs` 改动）时，源码与产物均未变，编译与换件属多余操作，且会在客户端运行时误报"编译成功未部署"。此时跳过编译与换件，仅写 lib 帧表并提示重启客户端。仅当**存在源码变更**时才走"编译→换件"链路；状态矩阵对跳过的阶段显示"跳过（无源码变更）"而非"✓"，避免误导。
- 编译成功但换件失败（如目标文件被占用、磁盘错误等非客户端运行原因）：源码/lib 已改、产物已编译，但未部署；UI 明确显示"编译成功，未部署，原因：…"，提供"重试部署"按钮，不重复编译。
- 换件按钮独立：`重编译` = 编译 + 展示结果（不部署）；`部署` = 仅换件（需先编译成功且有源码产物更新）；`保存改动` = 写回 + 编译 + 部署一条龙。三者职责分离，便于排障。
- 部署结果同样结构化展示：成功/失败、7 件逐件状态、失败原因，原始异常可展开。

## 11. 正确性约束

1. **只替换数字 token**：用字符偏移切片替换；同行多 token 从右到左。**偏移体系必须明确为 UTF-16 char 偏移（`string` 索引），与 Roslyn `TextSpan.Start/End` 同源**——源码含大量中文（本地化文本/注释），一个中文 char 占多字节，若误用字节偏移切片将导致同文件后续所有 token 整体错位（真实 bug 风险，v1.5 明确）。实现时用 `string.Remove/Insert` 或按 `TextSpan` 切片，禁止先把源码转成 `byte[]` 再按字节偏移替换。
2. **编码保真**：UTF-8 读写，探测并保留 BOM；换行符 CRLF/LF 按原文件保留。
3. **幂等**：重复扫描/保存不产生重复 Id；重复保存同值不产生 diff。
4. **不改无关文件**：只写"有实际变更且在引用清单内"的文件；排除目录见§7.5。
5. **不删数据**：禁止删除项目内任何原有文件；备份清理需确认。
6. **引用失效**：源码改动后重新扫描，消失引用自动移除，不残留旧数据。
7. **标签质量**：无合格人话标签的项禁止保存。
8. **载体正确**：玩家→源码，怪物/NPC 本库帧表→lib，全局兜底→源码且需确认。
9. **表达式安全**：只改基址，`DynamicPart` 逐字保留；保存后重新解析验证 `Expression == 新基址 + DynamicPart`。
10. **lib 写回原子性**：临时文件 + 原子替换 + 备份恢复；禁止直接覆盖正在使用的 lib。
11. **部署不可省略**：源码改动编译成功后必须换件 7 件到运行目录，否则视为任务未完成（见§10.4）。
12. **客户端进程门禁**：换件前检测 Client.exe，运行中不换件；lib 写回同理（lib 在运行目录被占用）。
13. **部署原子性**：7 件先备份，逐件复制，失败即回滚；复制后逐件比对大小与时间戳。
14. **路径白名单**：编译源仅 `Build\Client\Release\`，部署目标仅 `mir2-deploy\Client\`，lib 读写仅 `mir2-deploy\Client\Data\`；越界路径拒绝且记录。

## 12. 兼容与边界

- lib v2 无 Frames 段：动作表区显示"此库不支持帧表，需先升级到 v3"，不提供 LibFrames 编辑。
- 大库（1024+ 帧）：帧带虚拟化绘制，缩略图分页加载。
- 动态库选择（`BodyLibrary`）：标歧义，不自动推断具体编号。
- 源码跨库调用（NPCObject 用 Prguse 画血条）：按**被调用的 Library 变量**归属——打开 Prguse 能看到，打开 NPCs 看不到。这是正确行为。
- 中文注释/标识符：全程 UTF-8；Roslyn 按 Unicode 处理。
- 同一数字出现在多个引用（如 `Draw(1121,…)` 出现两次）：各自独立 Id、独立可改；保存时分别替换各自 token。
- 用户手工改过源码导致行号漂移：按稳定签名重新匹配；匹配失败的别名标记"待重新绑定"。

## 13. 技术选型

- **扫描定位**：Roslyn (`Microsoft.CodeAnalysis.CSharp`) 语法树只读分析，拿到精确 token 列/长度；避免正则误匹配注释与字符串。**只用语法树（`CSharpSyntaxTree.ParseText`），不依赖语义编译/符号解析**——本机可能缺 SlimDX 等引用导致 `GetSemanticModel` 失败；变量名、枚举名、调用形态全部按语法节点文本匹配即可。
- **写回**：按记录的 token 字符偏移切片替换（最小改动），不用 Roslyn 重写整棵树（会重排格式/丢注释）。
- **映射解析**：正则读 `Settings.cs` / `MLibrary.cs`，结果缓存 + 人工覆盖。
- **编译**：`System.Diagnostics.Process` 调 dotnet，异步 + 增量输出，超时可取消。
- **UI**：WinForms，复用现有 `FixedListView` 与缩略图渲染。
- **配置**：`LibraryEditor\Config\`（别名、映射覆盖）；`LibraryEditor\Backups\`（备份）。

## 14. 风险与对策

| 风险 | 对策 |
|---|---|
| 源码回写误改 | token 级替换 + 同行从右到左 + 备份 + diff 确认 + 失败回滚 |
| 帧表 8 数字误当帧号 | ArgKind 分类，仅 start/effectstart 给选帧按钮 |
| 怪物帧表改错载体 | Target 分类：LibFrames 写 lib，Player/Default 写源码 |
| 人话标签不准确 | 多级规则 + 别名持久化 + 不合格禁保存 |
| 动态库选不精确 | 明确标记歧义 + 手工指定编号 |
| lib 保存损坏 | 原子替换 + 备份恢复 + 大库二次确认 |
| 编译输出难读 | 结构化解析 + 计数 + 过滤 + 原始输出双视图 + 异常场景覆盖 |
| 大库卡顿 | 帧带虚拟化 + 缩略图分页 + 后台扫描 |
| 误写用户文件 | 只写引用清单覆盖文件；写前校验路径在仓库白名单内；不删除任何文件 |
| **编译成功但用户看不到效果** | 编译后强制换件 7 件到运行目录（§10.4），并校验大小/时间戳；未部署不算完成 |
| **客户端运行中换件失败** | 换件前检测 Client.exe，运行中不换件并提示；提供"重试部署" |
| **语义编译因缺引用失败** | 扫描只用语法树 ParseText，不用 GetSemanticModel（§13） |
| **多载体部分成功被误认为全成功** | 状态矩阵逐项展示源码/lib/编译/部署成败（§10.2.9） |

## 15. 修订记录

| 版本 | 修订内容 |
|---|---|
| v1.0 | 初稿 |
| v1.1 | ① 同行多 token 从右到左替换（防列偏移改错）② `new Frame` 8 数字按 ArgKind 分类（仅 2 个是帧号）③ 怪物/NPC 生效帧表归属改为 lib，Default 表标注全局兜底 ④ 别名 key 去掉行号改稳定签名 ⑤ 备份改到 `LibraryEditor\Backups\` 并定保留策略 ⑥ 编译覆盖 MSB 错误/工具缺失/超时/非零退出 ⑦ 扫描排除 obj/bin/Build 生成目录 ⑧ lib 写回原子性要求 ⑨ 未合格标签禁止保存 ⑩ 跨库调用归属规则明确 |
| v1.2 | ① **补致命遗漏：编译后必须换件 7 件到运行目录**（§10.4），否则编译成功却看不到效果；`重编译`/`部署`/`保存改动` 三者分离 ② 澄清两套 FrameSet 归属（§8.3）：编辑器写入模板 vs Client 运行时源码，数值不同勿混用 ③ 标注 `MirAction` 枚举来自 `Shared\Enums.cs:876`、Frame 类来自 Client ④ 标签优先级修正（§9）：控件变量名词典提升到"背包按钮"级，本地化 Key 降级辅助，冲突以词典为准 ⑤ 修正别名 key 示例 `#5527`（易误认行号）为 `#3`（出现序号）⑥ 补事实核查 7 条：lib 只在运行目录、源码 UTF-8 BOM+CRLF、数组库变量名清单、v3 帧表双向读写确认、自动填充函数 NotImplemented 占位 ⑦ 正确性约束补部署/进程门禁/原子换件/路径白名单（§11.11-14） |
| v1.3 | ① **补真实缺陷：玩家帧表被所有玩家库共用**（§8.2，`PlayerObject.cs:121` 所有玩家 `Frames=FrameSet.Player`），改一个影响全部，UI 标注"玩家全局"+二次确认+标签后缀 ② **补真实缺陷：`Id` 必须归一化表达式模式**（§6，数字→`<N>`），否则改帧号后 Id 漂移、破坏幂等（§11.3）③ 选帧缩略图取图来源四种情形明确（§10.1）④ 同步到任务分解 T1.4/T1.5 与验收 A6/A7/A8 |
| v1.4 | ① **端到端走查补逻辑缺口：按变更载体决定是否编译/换件**（§10.4）——仅 LibFrames 变更（无 `.cs` 改动）时跳过编译与换件，状态矩阵标"跳过"而非"✓" ② **客户端进程门禁提前到任何写操作之前**（§10.4）——否则客户端运行时 lib 写回已失败却误报"编译成功未部署"，掩盖真正原因；客户端未关则不写任何文件、不编译 ③ 修正"编译成功但换件失败"场景限定为非客户端运行原因 ④ 同步到任务分解 T2.5/T2.6 与验收 D5 |
| v1.5 | ① **磁盘事实复核补带后缀数组库映射**（§7）：`InitLibrary` 第 4 参数 suffix（`" L"`/`" R"`/`" S"`）→ 带空格文件名 `NN L.Lib`，实测运行目录 102 个此类文件；反查必须拆"编号+后缀"，否则 `AWeaponsL`/`ARWeaponsS` 等扫描不到引用 ② **编译成功判定**（§10.3）：判据=错误数 0、警告不阻断成功；基线自带 1 个 `MSB3277` 警告须列入白名单标"既有警告"，已 `dotnet build -c Release` 实测（0 错误/1 警告） ③ 同步任务分解/验收 |
| v1.6 | ① **补真实缺口：5 类特殊怪物忽略 lib 自带帧表**（§8.4）——`MonsterObject.cs:356-372` 用 `FrameSet.XXX[...]` 直接覆盖 Frames，走不到 default 的 `BodyLibrary.Frames ?? DefaultMonster`（:376）；对应 lib 237/380/902/903/904/905/950 须显示/编辑源码 `FrameSet.XXX` 列表帧表（Target=Source），标注"忽略 lib 自带帧表" ② 扫描器须识别 `frame.Add(...)` 局部变量形态（Frames.cs:38-170，`new List<FrameSet>{...}`/`List.Add(frame=new FrameSet())` 中赋值），不能只认 `Player.Add`/`DefaultNPC=...` ③ 列表下标按 Direction/Stage/BaseImage 动态选取，编辑只改帧号勿改元素个数/顺序 ④ 同步任务分解/验收 ⑤ 映射按字符串字面量解析、变量名≠文件名（§7.6：MiniMap→MMap.Lib、StateItems→StateItem.Lib、FloorItems→DNItems.Lib，禁止用变量名猜文件名）⑥ MapLibs 地图库边界（§7.7/§3.2.5：固定下标赋值、文件名无数字编号、引用在 MapEditor，本期 Out of Scope） |
