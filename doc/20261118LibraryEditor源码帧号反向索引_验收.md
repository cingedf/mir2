# LibraryEditor 源码帧号反向索引 — 验收文档

日期：2026-11-18
版本：v1.4
配套规范：`20261118LibraryEditor源码帧号反向索引_规范.md`（v1.6）
配套任务：`20261118LibraryEditor源码帧号反向索引_任务分解.md`（v1.4）

> v1.4 修订（配套规范 v1.6）：新增 A10——5 类特殊怪物（lib 237/380/902/903/904/905/950）忽略 lib 自带 Frames 段，显示/编辑源码 FrameSet.XXX 列表帧表（frame.Add 局部变量形态）。
> v1.2 修订（配套规范 v1.4）：新增 D5——验证「仅 lib 帧表变更跳过编译/换件」与「客户端进程门禁在写操作前生效，不误报成功」。
> v1.3 修订（配套规范 v1.5）：A1 增加带后缀库反查用例；C4 验证基线 MSB3277 警告不误报编译失败。

> 验收原则：每条用例必须有**可观察证据**（命令输出/文件对比/截图/日志），"看起来对"不算通过。带 🔴 的是回归红线（失败即整体不通过）。

---

## A. 映射与扫描（T0-T1）

### A1 🔴 lib ↔ 变量映射正确
- 前置：无 lib 打开。
- 步骤：用映射解析器反查下列路径。
- 预期：
  | 输入 | 输出 |
  |---|---|
  | `C:\1.40\mir2-deploy\Client\Data\Prguse.Lib` | `Libraries.Prguse` |
  | `...\Data\Monster\079.Lib` | `Libraries.Monsters[79]` |
  | `...\Data\AArmour\00.Lib` | `Libraries.AArmours[0]` |
  | `...\Data\AWeapon\03 L.Lib` | `Libraries.AWeaponsL[3]` |
  | `...\Data\ARWeapon\00 S.Lib` | `Libraries.ARWeaponsS[0]` |
- 证据：解析结果文本输出逐条比对上表。

### A2 🔴 扫描排除生效
- 步骤：对 `RepoRoot\Client` 运行扫描，列出待扫 `.cs`。
- 预期：`obj\`、`bin\`、`Build\` 下文件**不在**列表；`*.Designer.cs` **在**列表；`GlobalUsings.g.cs`、`*.AssemblyInfo.cs` 不在列表。
- 证据：待扫列表 + 排除项计数。

### A3 🔴 背包按钮标签基准
- 步骤：打开 `Prguse.Lib` → 扫描 → 定位 `MainDialogs.cs` 的 InventoryButton。
- 预期：恰好三条引用，人话标签分别为"背包按钮-普通态""背包按钮-悬停态""背包按钮-按下态"，Library=`Libraries.Prguse`，Kind=Constant，帧号 1903/1904/1905。
- 证据：引用列表截图/导出。

### A4 常量引用识别
- 步骤：打开 `Prguse.Lib` 扫描。
- 预期：`MirItemCell.cs:2743,2783` 各一条 `Libraries.Prguse.Draw(1121,…)`。
- 证据：引用清单含两条，来源行号正确。

### A5 Frame 8 参数分类
- 步骤：打开玩家库扫描 `Player.Add(...)`。
- 预期：`new Frame(0,4,0,500,…)` 中只有 `0`(start) 与 effectstart 提供选帧按钮；`4/0/500` 标注为帧数/跳帧/毫秒，不提供选帧。
- 证据：UI 截图 + ArgKind 数据导出。

### A6 载体归属
- 步骤：分别打开 `Monster\079.Lib` 与 `AArmour\00.Lib` 扫描。
- 预期：079.Lib 显示本库 Frames 段（LibFrames），`DefaultMonster` 仅在"全局兜底"组；AArmour\00.Lib 显示 `Player.Add(...)`（Source），**不显示** `DefaultNPC/DefaultMonster`。
- 证据：两种打开态的分组截图。

### A7 🔴 玩家帧表全局共用标注（规范§8.2 v1.3）
- 步骤：分别打开 `AArmour\00.Lib` 与 `CArmour\01.Lib` 扫描，定位 `Player.Add(...)` 帧表项。
- 预期：两处显示的 Player 帧表 Id 与内容一致（因为 `PlayerObject.cs:121` 所有玩家共用 `FrameSet.Player`）；每项带"玩家全局"标签后缀 + "修改影响全部玩家装备库"醒目提示；编辑该项弹二次确认。
- 证据：两库帧表项对比截图 + 共用警告与二次确认截图。

### A8 🔴 Id 归一化·改帧号幂等（规范§6 v1.3）
- 步骤：扫描 `Prguse.Lib` 记下 `DrawBlend(1820+Direction*10, …)` 项的 Id → 把 1820 改成 1900 保存 → 重新扫描同一项。
- 预期：两次 Id 相同（表达式模式已把数字归一化为 `<N>`，Id 不含具体帧号）；重复扫描无重复 Id；别名（若设）仍绑定该项。
- 证据：改前/改后 Id 字符串逐字比对 + 引用清单无重复项。

### A10 🔴 特殊怪物忽略 lib 自带帧表（规范§8.4 v1.6）
- 步骤：打开 `Monster\902.Lib`（DragonStatue）→ 查看帧表来源；再打开 `Monster\079.Lib`（普通怪物）对照。
- 预期：
  - `Monster\902.Lib` 显示源码 `FrameSet.DragonStatue` 列表帧表（`Client\MirObjects\Frames.cs`），**不显示** lib 自带 Frames 段；标注"此怪物忽略 lib 自带帧表，实际帧表在源码列表"。
  - 改其中一帧的 `new Frame(...)` 帧号 → 写回 `Frames.cs`，列表元素个数/顺序不变（下标按 Direction 动态选取）。
  - 对照：`Monster\079.Lib` 仍显示 lib 自带 Frames 段（回归不破坏普通怪物）。
- 证据：两个 lib 的帧表来源截图 + `Frames.cs` 改前/改后 diff（元素数不变）+ 枚举值 902↔DragonStatue 核对。

---

## B. 写回正确性（T2.2-T2.3）🔴

### B1 精确 token 替换 + BOM/CRLF 保真
- 步骤：备份 `MainDialogs.cs` → 把 1903 改为 1900 保存 → 二进制对比。
- 预期：
  - 仅 `1903`→`1900` 一处字节变化（`\r\n` 保持，BOM `EF BB BF` 保持）。
  - `HoverIndex=1904`、`PressedIndex=1905` 未变。
  - 文件行数不变、中文未乱码。
- 证据：`fc`/`git diff` 输出 + 前 3 字节 hexdump + CRLF 计数。

### B2 同行多 token 从右到左
- 步骤：构造或定位同行两个 `new Frame(...)`（如 `Frames.cs:19-20`），只改第一个的 start。
- 预期：只目标 token 变化，第二个 `new Frame` 完全不变。
- 证据：diff 输出。

### B3 lib Frames 原子写回
- 步骤：打开 `Monster\079.Lib` → 改某动作 start 帧号 → 保存 → 重新打开。
- 预期：重新打开显示新帧号；备份目录有 `.bak`；写回过程无"文件被占用"崩溃。
- 证据：前后帧号对比 + 备份文件存在。

### B4 🔴 客户端占用门禁
- 步骤：启动 Client.exe → 尝试写 lib / 部署。
- 预期：均被拒绝并提示"请先关闭游戏客户端"，无文件损坏。
- 证据：提示截图 + lib 文件哈希未变。

---

## C. 编译与结果展示（T2.4）

### C1 编译成功展示
- 步骤：点"重编译"（当前源码无误）。
- 预期：大字"编译成功"（绿）、错误 0 / 警告 N（N=现存旧警告数），原始输出可展开。
- 证据：结果窗口截图 + `dotnet build` 原始输出。

### C2 编译错误展示
- 步骤：临时注入 `error CS` 样本（如在某 `.cs` 加语法错），点"重编译"。
- 预期：大字"编译失败"（红）、错误计数≥1、列表含文件:行+CS 编号+消息；过滤"仅错误"可用；随后还原源码。
- 证据：结果截图 + 原始输出含 `error CS`。

### C3 异常场景
- 步骤：分别模拟 dotnet 不在 PATH、超时。
- 预期：显示"编译工具不可用"/"编译超时"，不崩溃、保留已捕获输出。
- 证据：提示截图。

### C4 🔴 基线警告不误报失败（规范 v1.5）
- 步骤：在未改任何源码的工程上点"重编译"（基线实测为 `0 错误 / 1 警告 MSB3277`）。
- 预期：判定为**编译成功**（错误数 0 即成功，警告不阻断）；该 MSB3277 警告归入"已知警告白名单"，"新增警告"计数为 0；不得显示"编译失败"或把基线警告当异常。
- 步骤 B：注入一条 `warning CSxxxx`（白名单外），再编译。
- 预期：仍为"编译成功"，但"新增警告 1"被正确统计并逐条列出。
- 证据：两次结果窗口截图 + 原始输出（基线仅 MSB3277；注入后多一条 CS 警告）。

---

## D. 部署换件（T2.5）🔴 致命

### D1 端到端生效闭环
- 步骤：
  1. 关闭 Client.exe。
  2. 打开 `Prguse.Lib`，把 InventoryButton 的 `Index` 1903 改为 1900。
  3. 点"保存改动"（写回→编译→部署一条龙）。
  4. 检查 `Build\Client\Release\` 与 `C:\1.40\mir2-deploy\Client\` 的 7 件。
- 预期：
  - 7 件（Client.exe/dll/pdb/deps.json/runtimeconfig.json/Shared.dll/Shared.pdb）在部署目录时间戳更新，大小与 Build 产物一致。
  - 结果窗口显示 `源码✓ / lib✓ / 编译✓ / 部署✓` 状态矩阵。
  - 提示"已生效，请重启客户端"。
- 证据：两目录 7 件时间戳/大小对比表 + 状态矩阵截图。

### D2 部署前后可观察差异
- 步骤：改一个明显的 UI 帧号（如某按钮 Index 换成另一帧），部署后启动客户端进游戏。
- 预期：游戏内该按钮图标确实变化（用户目视确认）。
- 证据：改前/改后游戏截图各一。

### D3 部署失败不静默
- 步骤：Client.exe 运行时点"部署"。
- 预期：提示"客户端运行中，无法部署"；提供"重试部署"；源码/lib/编译状态如实显示（源码已改但未部署）。
- 证据：提示截图 + 状态矩阵显示"部署未执行"。

### D4 路径白名单
- 步骤：人为把部署目标配到 `C:\Windows` 或 `BuildOut` 外。
- 预期：拒绝写入并报警，无任何文件被写到白名单外。
- 证据：拒绝日志 + 目标目录未产生文件。

### D5 🔴 按载体决定编译/换件 + 门禁提前（规范 v1.4）
- 步骤 A：打开 `Monster\079.Lib`，只改本库自带 Frames 段（Target=LibFrames），不动任何 .cs，点"保存改动"。
- 预期 A：状态矩阵显示 `源码跳过 / lib✓ / 编译跳过 / 部署跳过`，提示"lib 帧表已写入，请重启客户端"；不触发编译，不换件（7 件时间戳不变）。
- 步骤 B：启动 Client.exe（客户端运行中），再打开 `Monster\079.Lib` 改 Frames 段点"保存改动"。
- 预期 B：在**任何写操作之前**被门禁拒绝（lib 帧表未写入、未编译、未换件），提示"客户端运行中，需先关闭客户端"；不得出现"lib✓ 但编译成功未部署"这类掩盖真因的误导状态。
- 证据：两步骤的状态矩阵截图 + `Monster\079.Lib` 修改前后哈希对比 + BuildOut/DeployRoot 7 件时间戳对比。

---

## E. 部分成功与回滚（T2.6）🔴

### E1 状态矩阵如实
- 步骤：制造"源码写成功但编译失败"。
- 预期：显示 `源码✓ / lib✓ / 编译✗ / 部署未执行`；源码确实已改；不谎报全成功。
- 证据：状态矩阵截图 + 源码 diff。

### E2 一键回到备份
- 步骤：在 E1 后点"回到备份"。
- 预期：源码/lib 恢复到改动前；备份目录文件完好。
- 证据：恢复后文件与初始哈希一致。

---

## F. 全局红线（每次交付必检）

- 🔴 不删除 `C:\1.40` 下任何原有项目文件（Diablo3、mvp_zhcn、PKG_Soft、Switch_CN、movie_project、pipelines、textbook_dl 等）。本工具备份清理需用户确认，禁止自动删除。
- 🔴 用户未跟踪文件不动（`GM命令.md`、`doc\20261115UI面板GOD合成预览稿.md`）。
- 🔴 只写白名单路径：源码 `RepoRoot\Client`、lib `DeployRoot\Data`、部署目标 `DeployRoot`、备份/配置 `LibraryEditor\Backups|Config`。
- 🔴 `dotnet build LibraryEditor.csproj -c Release` 0 错误。
- 🔴 全程 UTF-8，中文不乱码，BOM/CRLF 保真。

## 验收通过判定
- A/B/C/D/E 全部用例 PASS，且 F 全部红线未触发 → 整体通过。
- 任一 🔴 用例失败 → 整体不通过，必须修复后重验。
- "用户无需打开代码编辑器即可完成改帧号并生效"为最终体验红线（D1+D2 联合证明）。
