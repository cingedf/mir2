# 20261114新增怪物播种维护工具MonsterSpawnSeed

> 素材补齐自主立项①（用户授权全权自主执行）落地载体：向 Server.MirDB 播种「Effect=2 弓手变体」怪物及其刷怪点，激活体检修复 73968ff 已接好的 HornedColdArcherBuff 链路。首次运行即完成素材落地。

## 现象

- 修复 73968ff（弓手 Effect==2 分支 AddBuff 授错 ColdArcherBuff→HornedColdArcherBuff）后，客户端光效/帧资源（MapObject.cs 帧 543-550 段）已就绪，但部署区没有任何 Effect=2 的 AI=401 弓手怪物——修复链路无触发实体，属「代码已修、素材缺失」。
- 怪物资料与刷怪点只存二进制 Server.MirDB（Envir 目录无文本源；Exports\*.csv/*.txt 是编辑器导出快照且滞后于 MirDB）；手拼字节直改二进制风险极高（版本门控拒载=全服起不来）。

## 排查过程

- 子代理全链考证（加载链/格式/编辑器窗体/导出导入路径）+ 主会话逐文件确认 API：Envir.Main/Edit 双实例与 DatabasePath 相对 CWD（Envir.cs:103-115）；LoadDB/SaveDB 读写对称（LoadDB 不读 GTMapList 尾段，往返产出与服务器自身 5 分钟 SaveDB 同构）；MonsterInfo v118 二进制格式（MonsterInfo.cs:53-148）；RespawnInfo 全字段与 Save 序列（RespawnInfo.cs:27-100）；CreateRespawnInfo 的 ++Envir.RespawnIndex 语义（MapInfo.cs:222-225，RespawnIndex 为 Envir 实例字段）。
- 复用仓库既有工具先例 RespawnFix（tools\RespawnFix）：顶层语句控制台 + 引用 Server.Library + Envir.Main.LoadDB/SaveDB + 运行中 Server.exe 拒绝执行 + 必须在 MirDB 目录运行。
- HornedArcher.CompleteAttack（75-118）三分支增益源全部取 `Stats[MinMC/MaxMC]`（新怪 MinMC=5/MaxMC=60 → 同伴获 MAC5-60+DC5-60 的 HornedColdArcherBuff，10 秒，20 秒攻击周期）。
- 服务器运行状态前置检查：Get-Process 无 Server 进程，MirDB 时间戳稳定。

## 修复内容

1. **`tools\MonsterSpawnSeed\`**（新工具，csproj+Program.cs，编译 0 警告 0 错误）：
   - 克隆现有「咒魔火弓师」（Index=808, AI=401, Effect=0）全字段（Stats 用 Stats(Stats) 拷贝构造，Drops 共享引用——存档不落 Drops，运行期由 DropPath 加载）；
   - 改 4 项：Index=最大 Index+1（1316）/ Name=咒魔罡风弓师（GameName 去数字规则兼容）/ Effect=2 / Stats[MinMC]=5；
   - 给 morae（沙遗迹，Index=194）加刷怪点 (209,207) Count=8 Spread=40 Delay=8 分钟（与既有火/风弓师同区，小规模试点）；
   - **安全设计**：Server.exe 运行中拒绝执行（防 5 分钟 SaveDB 竞态）；按名幂等（已播种即退出）；SaveDB 前自动备份（引擎内置 Back Up\Database）；apply 后 new Envir().LoadDB() 独立实例回读验证（名字/Index/Effect/MinMC/刷怪点坐标全部比对）。
2. **素材已落地**（deploy Server.MirDB）：咒魔罡风弓师 Index=1316 已入库，morae 刷怪点已挂；落库前手工备份 `Server.MirDB.pre-monster1316.bak`；沙盒副本全流程预演通过（plan→apply→回读一致）。

## 验证证据

- `dotnet build tools\MonsterSpawnSeed\MonsterSpawnSeed.csproj -c Release`：0 警告 0 错误。
- 沙盒（Temp 副本）：plan 输出与设计一致 → apply → `[验证通过] 怪物 Index=1316 "咒魔罡风弓师" Effect=2 MinMC=5 MaxMC=60；刷怪点 1 处`。
- deploy 实跑：同输出验证通过；Server.MirDB 939,995B → 939,823B（结构化重写，条目 +1 怪 +1 刷怪点）。
- 上线验证路径（重启 server.exe 后）：`@move morae 209 207` 观察咒魔罡风弓师对同伴施放的 HornedColdArcherBuff 光效（Monsters 帧 543-550，MAC+DC 增益）。

## 回滚方法

```
copy /y C:\1.40\mir2-deploy\Server\Server.MirDB.pre-monster1316.bak C:\1.40\mir2-deploy\Server\Server.MirDB
```
（或 `git revert <本提交>` 删工具；引擎自动备份 Back Up\Database\*.bak 亦可恢复。）
