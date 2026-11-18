# LibraryEditor 源码帧号反向索引 — 任务分解

日期：2026-11-18
版本：v1.4
配套规范：`20261118LibraryEditor源码帧号反向索引_规范.md`（v1.6）

> v1.4 修订（配套规范 v1.6）：T1.6 新增「特殊怪物 FrameSet.XXX 列表帧表识别」——5 类特殊怪物（lib 237/380/902/903/904/905/950）忽略 lib 自带 Frames 段，帧表在源码 `FrameSet.XXX` 列表，需识别 `frame.Add(...)` 局部变量形态（规范§8.4）。
>
> v1.3 修订（配套规范 v1.5）：T0.2 增加「带后缀库反查必须拆数字编号+后缀」；T2.4 编译结果增加「警告白名单/警告不阻断成功」判定。
项目：`C:\1.40\mir2-angelk727`

> 原则：先让端到端闭环跑通（P0→P2），再补全覆盖（P3）与体验（P4）。每个任务都有明确产出与"完成判定"，禁止留 TODO。全程遵守规范§7.5 扫描排除、§11 正确性约束、§11.14 路径白名单。

---

## P0 — 基础设施与映射（无 UI，可单测）

### T0.1 配置与路径常量
- 产出：`LibraryEditor\Config\FrameAliases.json`、`LibraryMapOverrides.json`（可为空模板）；代码内集中定义路径常量：
  - `RepoRoot` = `C:\1.40\mir2-angelk727`（由程序集位置向上定位，可配置覆盖）
  - `DeployRoot` = `C:\1.40\mir2-deploy\Client`（可配置覆盖）
  - `BuildOut` = `RepoRoot\Build\Client\Release`
  - `ClientProject` = `RepoRoot\Client\Client.csproj`
  - `ScanRoot` = `RepoRoot\Client`
- 完成判定：常量可被单测读取；切换覆盖配置后路径随之变化。

### T0.2 库映射解析器
- 产出：`LibraryMap` 类，解析 `Settings.cs:32-67` 与 `MLibrary.cs:62-133`：
  - 单文件：`Prguse = new MLibrary(Settings.DataPath + "Prguse")` → `Data\Prguse.Lib` ↔ `Libraries.Prguse`
  - 数组：`InitLibrary(ref Monsters, Settings.MonsterPath, "000")` → `Data\Monster\NNN.Lib` ↔ `Libraries.Monsters[NNN]`
  - 带后缀：`InitLibrary(ref AWeaponsL, Settings.AWeaponPath, "00", " L")` → `NN.Lib` 文件名含空格+L
- 支持人工覆盖（`LibraryMapOverrides.json`）与反查：给定磁盘 lib 绝对路径 → 变量（含下标）。**单文件库按 `X = new MLibrary(path + "Y")` 的字符串字面量 Y 取文件名，禁止用变量名 X 猜**——变量名≠文件名真实存在：`MiniMap`→`MMap.Lib`、`StateItems`→`StateItem.Lib`、`FloorItems`→`DNItems.Lib`（`MLibrary.cs:16-56`）。
- **带后缀库反查（v1.3/规范 v1.5）**：磁盘文件名需拆成「数字编号 + 后缀」两段再回查四参数 `InitLibrary(ref V, path, toString, suffix)`；文件名如 `00 S.Lib`、`03 L.Lib`，对应 `AWeaponsL`/`ARWeaponsS` 等变量。只按三参数形式会漏掉这 102 个库，导致其源码引用扫描不到（真实缺口）。
- 完成判定：
  - `monster\079.Lib` → `Libraries.Monsters[79]`
  - `Data\Prguse.Lib` → `Libraries.Prguse`
  - `AArmour\00.Lib` → `Libraries.AArmours[0]`
  - `AWeapon\03 L.Lib` → `Libraries.AWeaponsL[3]`（后缀反查）
  - `ARWeapon\00 S.Lib` → `Libraries.ARWeaponsS[0]`（后缀反查）
  - 源码仓库无 `Client\Data`，路径基线必须是 `DeployRoot\Data`（规范§4）。

### T0.3 扫描排除器
- 产出：目录/文件排除规则：`Client\obj\`、`Client\bin\`、`Build\`、`**\.git\`、`GlobalUsings.g.cs`、`*.AssemblyInfo.cs`；`*.Designer.cs` **不排除**（控件帧号在其中）。
- 完成判定：给定 Client 目录树，返回待扫描 `.cs` 列表；`obj/`、`Build/` 下文件不在列表，`MainDialogs.Designer.cs` 在列表。

---

## P1 — Roslyn 引用扫描器（核心，可单测）

### T1.1 语法树解析（不依赖语义编译）
- 产出：`ReferenceScanner`，对每个待扫 `.cs` 用 `CSharpSyntaxTree.ParseText(File.ReadAllText(path))`，**禁用 GetSemanticModel**（规范§13，本机可能缺 SlimDX 引用）。
- 完成判定：能解析全部 Client 源码，无因缺引用导致的异常。

### T1.2 四种引用形态识别
- 产出：识别并记录（每条含 LibVariable / FilePath / Line / Column / TokenStart / TokenLength / TokenOrdinal / Expression / Kind）：
  1. Constant：`Libraries.X.Draw(1121, …)`、`Index = 1903`
  2. Expression：`DrawBlend(1820 + Direction*10 + FrameIndex, …)` → 基址 1820 可改，`+ …` 为 DynamicPart 只读
  3. PlayerFrameArg / DefaultFrameArg：`X.Add(MirAction.Y, new Frame(...))`
  4. Indirect：`BodyLibrary.Draw(DrawFrame + ArmourOffSet, …)` → 标 AmbiguousLib
- 对象初始化器关联：`Index`/`HoverIndex`/`PressedIndex` 必须与同一初始化器内的 `Library = Libraries.Prguse` 关联，**不能按就近行**（规范§4 MainDialogs.cs:66-76）。
- 完成判定（基准样例）：扫描 `MainDialogs.cs`，`InventoryButton` 的 `Index=1903`、`HoverIndex=1904`、`PressedIndex=1905` 各为一条引用，LibVariable 均为 `Libraries.Prguse`，Kind=Constant；`MirItemCell.cs:2743,2783` 各识别一条 `Libraries.Prguse.Draw(1121,…)`。

### T1.3 `new Frame(...)` 8 参数分类
- 产出：按 ArgKind（FrameStart/Count/Skip/Interval/EffectStart/EffectCount/EffectSkip/EffectInterval）标注 8 个数字；仅第 1、5 个为帧号。
- 完成判定：`Frames.cs:19` `new Frame(0,4,0,500,…)` → 0/EffectStart 标帧号，4/0/500 标非帧号。

### T1.4 载体归属
- 产出：每条引用算 Target：
  - 玩家库（AArmours/CArmours/...）→ `Libraries.Player` 帧表 → Source
  - 怪物/NPC 本库 Frames 段 → LibFrames
  - 全局兜底 DefaultNPC/DefaultMonster → Source（需确认）
  - 常量/表达式 → 按 LibVariable 直接归属 Source
  - Indirect → AmbiguousLib + 目录级
- 完成判定：打开 `Monster\079.Lib` 不显示 `DefaultMonster`（只在兜底组显示）；打开 `AArmour\00.Lib` 显示 `Player.Add(...)` 而不显示 Default 表（规范§8.2）。
- **玩家全局帧表标注（规范§8.2 v1.3）**：打开任意玩家库（AArmour/CArmour/CWeapon…）时，`Player.Add(...)` 项必须带"玩家全局"后缀 + 醒目提示"此帧表被所有玩家装备库共用，修改影响全部玩家"，编辑需二次确认（依据 `PlayerObject.cs:121` 所有玩家 `Frames=FrameSet.Player`）。完成判定：切换两个不同玩家库，显示的 Player 帧表 Id 与内容一致，且均有共用警告。

### T1.5 稳定 Id 与人话标签
- 产出：
  - Id = 文件 + 变量名 + **归一化表达式模式** + TokenOrdinal 的哈希，不含行号、不含具体帧号数字。归一化 = 所有可改数字 token 替换为 `<N>`（如 `DrawBlend(1820 + Direction*10, …)` → `DrawBlend(<N> + Direction*<N>, …)`），保证改帧号后 Id 不变、幂等成立（规范§6 v1.3）。禁止用原始 `Expression` 文本做 Id。
  - 标签按规范§9 优先级：别名 → 控件变量名词典（背包按钮）→ MirAction → 状态后缀 → 本地化 Key（辅助）→ 注释 → 上下文 → 兜底。
  - 内置控件词典：`InventoryButton`→背包按钮、`CharacterButton`→人物按钮、`SkillButton`→技能按钮等。
- 完成判定：`MainDialogs.cs:66-76` 三条显示为"背包按钮-普通态/悬停态/按下态"；无合格标签的项被标灰且禁止保存。

### T1.6 特殊怪物 FrameSet.XXX 列表帧表识别（v1.4/规范§8.4）
- 产出：识别 5 类特殊怪物（lib 237/380/902/903/904/905/950），它们的 Frames 在 `MonsterObject.cs:356-372` 用 `FrameSet.XXX[...]` 直接覆盖、忽略 lib 自带 Frames 段；打开这些 lib 时显示/编辑源码 `FrameSet.XXX` 列表帧表（Target=Source，写回 `Client\MirObjects\Frames.cs`），标注"忽略 lib 自带帧表"。
- 扫描器须识别 `frame.Add(...)` 局部变量形态（`Frames.cs:38-170`，`new List<FrameSet>{...}` / `List.Add(frame = new FrameSet())` 中赋值），不能只认 `Player.Add` / `DefaultNPC=...` 两种形态（规范§8.4）。帧表列表下标按 Direction/Stage/BaseImage-903/BaseImage-950 动态选取，编辑只改 `new Frame(...)` 帧号，勿改元素个数/顺序。
- 完成判定：打开 `Monster\902.Lib` 显示 `FrameSet.DragonStatue` 源码列表而非 lib 自带 Frames 段；改其帧号写回 `Frames.cs` 且列表元素数不变；`Monster\079.Lib`（普通怪物）仍显示 lib 自带 Frames 段（不回归）。

---

## P2 — UI、写回、编译、部署（端到端闭环）

### T2.1 反向索引主界面
- 产出：WinForms 窗体（复用 `FixedListView` 缩略图）：顶部库信息、帧带时间轴、引用列表 DataGridView（人话/表达式/当前值/来源/类型/载体/歧义）、分组、编辑控件、选帧弹窗。
- 完成判定：打开一个 lib，引用列表能显示扫描结果；点击帧带段能定位源文件行；选帧弹窗能显示 lib 缩略图并回填帧号。

### T2.2 精确 token 写回（最小改动）
- 产出：按 TokenStart/Length 切片替换；同行多 token 按 TokenOrdinal **降序**替换；UTF-8 BOM 保留、CRLF 保留（规范§11.1-2、§10.2.5）。
- 完成判定：把 1903 改为 1900 后，diff 仅该数字变化，其余字节不变；同行两个 `new Frame` 只改目标 token；BOM 与 CRLF 未被破坏。

### T2.3 lib Frames 原子写回
- 产出：改 `_library.Frames` → 写临时文件 → 原子替换运行目录 lib（规范§10.2.7）；写前备份；客户端进程占用检测；大库二次确认。
- 完成判定：编辑怪物库帧表保存后，重新打开显示新帧表；`MLibraryV2.Save()` 读回一致（§4 事实已确认双向支持）。

### T2.4 编译与结果可视化
- 产出：`dotnet build Client.csproj -c Release --nologo` 异步执行，超时 10 分钟可取消；解析 `error/warning CS/MSB`；大字结论 + 计数 + 过滤列表 + 原始输出双视图；覆盖工具缺失/超时/非零退出无 error 行（规范§10.3）。
- **成功判定与基线警告（v1.3/规范 v1.5）**：错误数 0 即编译成功，**警告不阻断成功**；基线实测 `0 错误 / 1 警告 MSB3277`（WebView2 依赖的 WindowsBase 版本冲突）列入"已知警告白名单"按编号+消息前缀匹配，"新增警告"只统计白名单外项。
- 完成判定：注入一个编译错误样本能显示"编译失败/错误 1"；警告样本显示计数与逐条；未改源码的基线工程显示"编译成功 / 新增警告 0"（MSB3277 不算新增）；`dotnet` 缺失显示"编译工具不可用"。

### T2.5 部署换件（致命环节）
- 产出：编译成功后把 7 件从 `BuildOut` 复制到 `DeployRoot`，备份 + 逐件复制 + 大小/时间戳校验 + 失败回滚（规范§10.4）。
- **客户端进程门禁时机（v1.2/规范 v1.4）**：门禁必须在**任何写操作之前**检测——lib 帧表写回同样需要客户端关闭（§11.12），不能只在编译成功后检测；客户端运行则直接中止，不写文件、不编译。
- **按变更载体决定（v1.2/规范 v1.4）**：仅当**存在源码变更**时才编译+换件；纯 LibFrames 变更跳过二者。
- 完成判定：源码改动 → 保存 → 7 件时间戳更新且大小一致；客户端运行时点部署/写 lib 均被拒绝并提示；只改 lib 帧表不触发编译与换件；路径越界（写到 DeployRoot 外）被拒绝。

### T2.6 状态矩阵与回滚
- 产出：源码/lib/编译/部署四段状态矩阵；失败不跨载体回滚，统一"回到备份"入口（规范§10.2.9）；跳过阶段显示"跳过（无源码变更）"而非"✓"（规范§10.4 v1.4）。
- 完成判定：模拟"源码成功、编译失败"显示 `源码✓ / lib✓ / 编译✗ / 部署未执行`，且源码确实已改、可一键回到备份；纯 lib 变更显示 `源码跳过 / lib✓ / 编译跳过 / 部署跳过`。

---

## P3 — 覆盖增强

### T3.1 表达式动态项增强
- 产出：Expression 行标注"实际帧=基址+动态项"，给方向 0/4 示例帧；保存后校验 `Expression == 新基址 + DynamicPart`（规范§11.9）。

### T3.2 Indirect 歧义指定
- 产出：Indirect 引用提供编号下拉，用户手工指定 `Libraries.AAArmours[NN]`，标记 AmbiguousLib 解除。

### T3.3 映射/别名管理 UI
- 产出：映射设置（修正 lib↔变量）、别名管理（增删改，写 `FrameAliases.json`），行号漂移后标记"待重新绑定"。

### T3.4 全量回归扫描
- 产出：对 Client 全目录扫描一次，生成"引用总数/分类统计"报告，人工抽查 20 条确认无误。

---

## P4 — 体验与健壮性

### T4.1 大库性能
- 帧带虚拟化绘制、缩略图分页、后台扫描防卡顿（规范§12）。

### T4.2 diff 预览增强
- 人话标签 + 旧代码行 + 新代码行对照，确认后才写。

### T4.3 编译结果对比
- 与上次编译对比标注"新增警告 N"（可选，非验收硬性项）。

---

## 交付顺序与依赖

```
T0.1 ─┬─ T0.2 ─┬─ T1.2 ─┬─ T1.5 ─┬─ T2.1 ─┬─ T2.2 ─┬─ T2.6
T0.3 ─┘        ├─ T1.3 ─┘        │        ├─ T2.3 ─┤
               └─ T1.4 ─┘        │        ├─ T2.4 ─┤
                      T1.1 ──────┘        └─ T2.5 ─┘
                                                  ↓
                                        P3 (T3.1-3.4) → P4 (T4.1-4.3)
```

- P0、P1 全部可脱离 UI 单测。
- P2 是用户价值闭环：T2.2+2.4+2.5 完成后即可演示"改 1903→1900→保存→换件→进游戏生效"。
- P3/P4 在闭环稳定后增量交付。

## 全局完成定义（DoD）
1. 验收文档 `20261118LibraryEditor源码帧号反向索引_验收.md` 全部用例 PASS。
2. `dotnet build LibraryEditor.csproj -c Release` 0 错误。
3. 不删除任何原有文件；只写白名单内路径；备份与回滚可用。
4. 用户全程无需打开代码编辑器即可完成一次"反向索引→改帧号→生效"闭环。
