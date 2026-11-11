using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// 攻城配置写入工具：往 Server.MirDB 的 ConquestInfoList 写入沙巴克攻城配置（Index=1）。
//
// 背景：引擎攻城全链路（ConquestObject/ConquestInfo/怪物硬件 AI960/950/951/沙城管理 NPC/战旗 NPC）齐全，
// 但 MirDB 的 ConquestInfoList 为空（Conquests 目录无 .mcd），攻城系统无配置不可用。
// 配置坐标全部来自部署区 Conquests\沙巴克设置说明.txt（官方说明文件）。
//
// 用法: ConquestSetup plan    （预览当前 ConquestInfoList 与相关地图/怪物/NPC 绑定情况，不写库）
//       ConquestSetup fix     （写入沙巴克 Index=1 攻城配置并 SaveDB）
//
// 注意: 必须在含 Server.MirDB 的目录运行；Server.exe 运行时拒绝执行（避免 SaveDB 冲突）。
// 怪物硬件要求（ConquestGuildInfo 运行时校验）：弓箭手 AI=960、城门 AI=950、城墙 AI=951，否则静默不刷。
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 1 || (args[0] != "plan" && args[0] != "fix"))
{
    Console.WriteLine("用法: ConquestSetup plan");
    Console.WriteLine("      ConquestSetup fix    （写入沙巴克 Index=1 攻城配置）");
    Console.WriteLine("  plan: 预览当前攻城配置与硬件匹配情况（不写库）");
    Console.WriteLine("  fix : 写入配置并保存 Server.MirDB");
    return 2;
}

var mode = args[0];

var dbPath = Path.Combine(".", "Server.MirDB");
if (!File.Exists(dbPath))
{
    Console.WriteLine($"未找到 {Path.GetFullPath(dbPath)} — 请在含 Server.MirDB 的目录运行。");
    return 3;
}

// 安全检查: Server.exe 正在运行时拒绝执行（运行中的服退出时会 SaveDB 覆盖修改）
if (System.Diagnostics.Process.GetProcessesByName("Server").Length > 0)
{
    Console.WriteLine("检测到 Server.exe 正在运行 — 请先关闭服务端再执行（避免 SaveDB 覆盖）。");
    return 4;
}

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");
Envir.Main.LoadDB();
var envir = Envir.Main;

// ---- 关键 MobIndex（怪物硬件，须满足运行时 AI 校验：弓箭手 960 / 城门 950 / 墙 951）----
const int GuardMobIndex = 187;   // 弓箭手 AI960
const int GateMobIndex = 1279;   // 沙巴克城门 AI950
const int WallMobIndexL = 1286;  // 沙巴克左城墙 AI951
const int WallMobIndexM = 1287;  // 沙巴克中城墙 AI951
const int WallMobIndexR = 1288;  // 沙巴克右城墙 AI951

// ---- 沙巴克设置说明.txt 全坐标 ----
// 中心(644,290)范围100；城门(671,329)；左墙(624,278)/中墙(627,278)/右墙(634,271)；
// 弓箭手 12 个坐标。
(int X, int Y)[] guardSpots =
{
    (662, 333), (664, 331), (666, 329), (676, 319), (678, 317), (681, 314),
    (628, 271), (632, 267), (670, 335), (671, 334), (675, 330), (676, 329),
};

// 地图解析：0150（沙巴克皇宫）在 MapInfoList 的实际 Index
var mapSabuk = envir.MapInfoList.FirstOrDefault(m => m.FileName != null && m.FileName.Contains("SABUK_CASTLE1"));
var mapPalace = envir.MapInfoList.FirstOrDefault(m => m.FileName != null && m.FileName.Contains("0150"));
if (mapPalace == null)
    mapPalace = envir.MapInfoList.FirstOrDefault(m => m.Title != null && m.Title.Contains("沙巴克皇宫"));

// 怪物硬件核验
var mobGuard = envir.GetMonsterInfo(GuardMobIndex);
var mobGate = envir.GetMonsterInfo(GateMobIndex);
var mobWallL = envir.GetMonsterInfo(WallMobIndexL);
var mobWallM = envir.GetMonsterInfo(WallMobIndexM);
var mobWallR = envir.GetMonsterInfo(WallMobIndexR);

Console.WriteLine();
Console.WriteLine("==== 硬件核验 ====");
Console.WriteLine($"战场: {(mapSabuk != null ? $"Index={mapSabuk.Index} {mapSabuk.Title} ({mapSabuk.FileName})" : "缺失!")}");
Console.WriteLine($"皇宫 0150: {(mapPalace != null ? $"Index={mapPalace.Index} {mapPalace.Title} ({mapPalace.FileName})" : "未找到! 可用候选:")}");
if (mapPalace == null)
{
    foreach (var m in envir.MapInfoList.Where(m => (m.FileName ?? "").Contains("0150") || (m.Title ?? "").Contains("皇宫")).Take(10))
        Console.WriteLine($"   Index={m.Index} Title={m.Title} File={m.FileName}");
}
void CheckMob(string label, int index, int needAi)
{
    var mi = envir.GetMonsterInfo(index);
    string verdict;
    if (mi == null) verdict = "缺失!";
    else if (mi.AI != needAi) verdict = $"AI={mi.AI} 不符（需 {needAi}）— 运行时不会刷!";
    else verdict = $"AI={mi.AI} OK";
    Console.WriteLine($"{label}: MobIndex={index} {(mi != null ? mi.Name : "?")} — {verdict}");
}
CheckMob("弓箭手", GuardMobIndex, 960);
CheckMob("城门  ", GateMobIndex, 950);
CheckMob("左城墙", WallMobIndexL, 951);
CheckMob("中城墙", WallMobIndexM, 951);
CheckMob("右城墙", WallMobIndexR, 951);

Console.WriteLine();
var list = envir.ConquestInfoList;
Console.WriteLine($"==== 当前 ConquestInfoList: {list.Count} 条 ====");
foreach (var c in list)
    Console.WriteLine($"   Index={c.Index} Name={c.Name} Map={c.MapIndex} Palace={c.PalaceIndex} Type={c.Type} Game={c.Game} 守卫={c.ConquestGuards.Count} 门={c.ConquestGates.Count} 墙={c.ConquestWalls.Count}");

// 绑定 Conquest 字段的 NPC（LoadNPCs 按 NPCInfo.Conquest == 攻城 Index 挂接）
Console.WriteLine();
Console.WriteLine("==== NPCInfo.Conquest 已绑定的 NPC ====");
var bound = envir.NPCInfoList.Where(n => n.Conquest != 0).ToList();
if (bound.Count == 0) Console.WriteLine("   （无 — 战旗/沙城管理等 NPC 未挂接攻城对象，不影响开战核心）");
foreach (var n in bound)
    Console.WriteLine($"   {n.Index} {n.Name} Conquest={n.Conquest} Map={n.MapIndex} ({n.Location})");

if (mode == "plan")
{
    Console.WriteLine();
    Console.WriteLine("[plan] 预览完毕（未写库）。");
    return 0;
}

// ---- fix 模式 ----
if (mapSabuk == null || mapPalace == null)
{
    Console.WriteLine("[fix] 地图缺失，拒绝写入。");
    return 5;
}
if (list.Any(c => c.Index == 1))
{
    Console.WriteLine("[fix] 已存在 Index=1 的攻城配置，拒绝重复写入（用 plan 查看）。");
    return 6;
}

var info = new ConquestInfo
{
    Index = 1,
    FullMap = false,
    Location = new Point(644, 290),
    Size = 100,
    Name = "沙巴克",
    MapIndex = mapSabuk.Index,
    PalaceIndex = mapPalace.Index,
    ExtraMaps = new List<int>(),
    GuardIndex = GuardMobIndex,
    GateIndex = GateMobIndex,
    WallIndex = WallMobIndexL,
    SiegeIndex = 0,
    FlagIndex = 0,
    ConquestGuards = guardSpots.Select((s, i) => new ConquestArcherInfo
    {
        Index = i + 1,
        Location = new Point(s.X, s.Y),
        MobIndex = GuardMobIndex,
        Name = "沙巴克弓箭手",
        RepairCost = 1000,
    }).ToList(),
    ConquestGates = new List<ConquestGateInfo>
    {
        new() { Index = 1, Location = new Point(671, 329), MobIndex = GateMobIndex, Name = "沙巴克城门", RepairCost = 1000 },
    },
    ConquestWalls = new List<ConquestWallInfo>
    {
        new() { Index = 1, Location = new Point(624, 278), MobIndex = WallMobIndexL, Name = "沙巴克左城墙", RepairCost = 1000 },
        new() { Index = 2, Location = new Point(627, 278), MobIndex = WallMobIndexM, Name = "沙巴克中城墙", RepairCost = 1000 },
        new() { Index = 3, Location = new Point(634, 271), MobIndex = WallMobIndexR, Name = "沙巴克右城墙", RepairCost = 1000 },
    },
    ConquestSieges = new List<ConquestSiegeInfo>(), // 攻城车 AI 校验为 243，沙巴克无匹配硬件，留空
    ConquestFlags = new List<ConquestFlagInfo>(),   // 占领皇宫模式不需要旗
    StartHour = 0,
    WarLength = 60,
    Type = ConquestType.申请启动,
    Game = ConquestGame.占领皇宫,
    Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
    KingLocation = new Point(644, 290),
    KingSize = 100,
    ControlPoints = new List<ConquestFlagInfo>(),
    ControlPointIndex = 0,
};
list.Add(info);

Console.WriteLine($"[fix] 已写入: Index={info.Index} {info.Name} Map={info.MapIndex} Palace={info.PalaceIndex} 守卫={info.ConquestGuards.Count} 门={info.ConquestGates.Count} 墙={info.ConquestWalls.Count} 类型={info.Type} 模式={info.Game}");
Console.WriteLine("[fix] 保存 Server.MirDB（自动备份在 DatabaseBackup）...");
Envir.Main.SaveDB();
Console.WriteLine("完成。请重启服务端，用 GM @STARTCONQUEST 1 测试开战，或建公会后在沙巴克战旗申请攻城。");
return 0;
