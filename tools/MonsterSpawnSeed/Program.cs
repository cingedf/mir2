using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// 怪物/刷怪点种子工具：向 Server.MirDB 播种「Effect=2 弓手变体」及其刷怪点（素材补齐自主立项①）。
//
// 背景：体检修复 73968ff（弓手 Effect==2 分支 AddBuff 授错 ColdArcherBuff→HornedColdArcherBuff）落地后，
// 客户端光效/帧资源已就绪，但部署区没有任何 Effect=2 的 AI=401 弓手——本工具补齐该素材变体，
// 激活已修好的 HornedColdArcherBuff 同伴增益链路（MAC+DC，增益源取 MinMC/MaxMC）。
//
// 动作：
//   1. 克隆现有「咒魔火弓师」(Index=808, AI=401, Effect=0) 全字段；
//   2. 改 4 项：Index=下一个空闲 MonsterIndex / Name=咒魔罡风弓师 / Effect=2 / Stats[MinMC]=5（MaxMC=60 保留）；
//   3. 给 FileName=="morae" 地图加刷怪点 (209,207) Count=8 Spread=40 Delay=8（与既有火/风弓师同区）；
//   4. SaveDB → 重新 LoadDB 验证（计数 +1、名字/Effect/刷怪点 dump）。
//
// 用法: MonsterSpawnSeed plan   （只预览，不写库）
//       MonsterSpawnSeed apply  （执行并保存）
// 注意: 必须在含 Server.MirDB 的目录运行；Server.exe 运行时拒绝执行（避免 SaveDB 冲突）。
Console.OutputEncoding = Encoding.UTF8;

var apply = args.Length == 1 && args[0] == "apply";
if (args.Length != 1 || !(apply || args[0] == "plan"))
{
    Console.WriteLine("用法: MonsterSpawnSeed plan|apply");
    return 2;
}

var dbPath = Path.Combine(".", "Server.MirDB");
if (!File.Exists(dbPath))
{
    Console.WriteLine($"未找到 {Path.GetFullPath(dbPath)} — 请在含 Server.MirDB 的目录运行。");
    return 3;
}

// 安全检查: Server.exe 正在运行时拒绝执行（运行中的服每 5 分钟 SaveDB 会覆盖修改）
if (System.Diagnostics.Process.GetProcessesByName("Server").Length > 0)
{
    Console.WriteLine("检测到 Server.exe 正在运行 — 请先关闭服务端再执行（避免 SaveDB 覆盖）。");
    return 4;
}

const string sourceName = "咒魔火弓师";
const string newName = "咒魔罡风弓师";
const int newEffect = 2;
const int newMinMC = 5;
const string targetMapFile = "morae";

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");
Envir.Main.LoadDB();
var envir = Envir.Main;

// ---- 1. 找克隆源（按名取第一个，火弓师有两行 808/809 但字段除 Index 外一致）----
var source = envir.MonsterInfoList.FirstOrDefault(m => m.Name == sourceName);
if (source == null)
{
    Console.WriteLine($"未找到克隆源怪物「{sourceName}」。");
    return 5;
}

// 幂等检查：已播种则直接退出
if (envir.MonsterInfoList.Any(m => m.Name == newName))
{
    Console.WriteLine($"怪物「{newName}」已存在 — 无需重复播种。");
    return 0;
}

int newIndex = envir.MonsterInfoList.Max(m => m.Index) + 1;
Console.WriteLine($"克隆源: Index={source.Index} \"{source.Name}\" AI={source.AI} Effect={source.Effect}");
Console.WriteLine($"新怪物: Index={newIndex} \"{newName}\" Effect={newEffect} MinMC={newMinMC}");

// ---- 2. 逐字段克隆（不碰枚举数值；Stats 用拷贝构造）----
var clone = new MonsterInfo
{
    Index = newIndex,
    Name = newName,
    Image = source.Image,
    AI = source.AI,
    Effect = newEffect,
    Level = source.Level,
    ViewRange = source.ViewRange,
    CoolEye = source.CoolEye,
    Light = source.Light,
    AttackSpeed = source.AttackSpeed,
    MoveSpeed = source.MoveSpeed,
    Experience = source.Experience,
    DropPath = source.DropPath,
    Drops = source.Drops,          // 共享同一 DropInfo 列表引用（存档仅存 DropPath，Drops 运行期由 DropPath 加载，不写入 MirDB）
    CanTame = source.CanTame,
    CanPush = source.CanPush,
    AutoRev = source.AutoRev,
    Undead = source.Undead,
    CanRecall = source.CanRecall,
    IsBoss = source.IsBoss,
    HasSpawnScript = source.HasSpawnScript,
    HasDieScript = source.HasDieScript,
    Stats = new Stats(source.Stats),
};
clone.Stats[Stat.MinMC] = newMinMC;

Console.WriteLine($"Stats: MinAC={clone.Stats[Stat.MinAC]} MaxAC={clone.Stats[Stat.MaxAC]} MinMAC={clone.Stats[Stat.MinMAC]} " +
                  $"MaxMAC={clone.Stats[Stat.MaxMAC]} MinDC={clone.Stats[Stat.MinDC]} MaxDC={clone.Stats[Stat.MaxDC]} " +
                  $"MinMC={clone.Stats[Stat.MinMC]} MaxMC={clone.Stats[Stat.MaxMC]} HP={clone.Stats[Stat.HP]}");

// ---- 3. 刷怪点（morae 与既有火/风弓师同区）----
var targetMap = envir.MapInfoList.FirstOrDefault(m => m.FileName == targetMapFile);
if (targetMap == null)
{
    Console.WriteLine($"未找到目标地图 FileName=\"{targetMapFile}\"。");
    return 6;
}

Console.WriteLine($"目标地图: Index={targetMap.Index} \"{targetMap.FileName}\" 标题=\"{targetMap.Title}\" 现有刷怪点={targetMap.Respawns.Count}");
var resp = new RespawnInfo
{
    MonsterIndex = newIndex,
    Location = new System.Drawing.Point(209, 207),
    Count = 8,
    Spread = 40,
    Delay = 8,                 // 分钟（Map.Process: delay * Settings.Minute）
    Direction = 0,
    RoutePath = string.Empty,
    RandomDelay = 0,
    SaveRespawnTime = false,
    RespawnTicks = 0,
    RespawnIndex = ++envir.RespawnIndex,
};

foreach (var r in targetMap.Respawns.Where(r => r.MonsterIndex == source.Index || r.MonsterIndex == 810 || r.MonsterIndex == 811))
    Console.WriteLine($"   既有: 怪{r.MonsterIndex} ({r.Location.X},{r.Location.Y}) Count={r.Count} Spread={r.Spread} Delay={r.Delay}");
Console.WriteLine($"   新增: 怪{newIndex} (209,207) Count=8 Spread=40 Delay=8 RespawnIndex={resp.RespawnIndex}");

if (!apply)
{
    Console.WriteLine();
    Console.WriteLine("[plan] 预览结束（未写库）。");
    return 0;
}

// ---- 4. 落库 + 重载验证 ----
envir.MonsterInfoList.Add(clone);
targetMap.Respawns.Add(resp);

Console.WriteLine();
Console.WriteLine("[apply] 保存 Server.MirDB（自动备份在 Back Up\\Database\\）...");
envir.SaveDB();

Console.WriteLine("重新加载验证...");
var verify = new Envir();
verify.LoadDB();
var vClone = verify.MonsterInfoList.FirstOrDefault(m => m.Name == newName);
var vMap = verify.MapInfoList.FirstOrDefault(m => m.FileName == targetMapFile);
bool ok = vClone != null && vClone.Index == newIndex && vClone.Effect == newEffect
          && vClone.Stats[Stat.MinMC] == newMinMC
          && vMap != null && vMap.Respawns.Any(r => r.MonsterIndex == newIndex && r.Location.X == 209 && r.Location.Y == 207);

if (!ok)
{
    Console.WriteLine("[验证失败] 回读数据与预期不符 — 请检查数据库（播种已在文件中，需人工核查）。");
    return 7;
}

Console.WriteLine($"[验证通过] 怪物 Index={vClone.Index} \"{vClone.Name}\" Effect={vClone.Effect} MinMC={vClone.Stats[Stat.MinMC]} " +
                  $"MaxMC={vClone.Stats[Stat.MaxMC]}；刷怪点 {vMap.Respawns.Count(r => r.MonsterIndex == newIndex)} 处。");
Console.WriteLine("完成。请重启服务端生效，验证路径: @move morae 209 207 观察同伴增益光效（MAC+DC 增益，20 秒周期）。");
return 0;
