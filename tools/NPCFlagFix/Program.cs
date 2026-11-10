using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// NPC 标志批量设置工具：批量勾选 NPC 的 ShowOnBigMap（大地图显示）与 CanTeleportTo（大地图传送）。
//
// 背景：大地图右侧 NPC 列表只显示零星几个、选中 NPC 后「移动/传送」按钮灰死——
// 均因 NPC DB 标志默认 false（10-01 导出：ShowOnBigMap True=187/644，CanTeleportTo True=28/616）。
// 非引擎故障，是数据标志没人勾过。用户决策：两标志全勾（644 个 NPC 全部置 true）。
//
// 按钮启用条件（客户端 BigMapDialog.cs@68）：
//   TargetMapIndex == 当前图 && selectedNPC.Info.CanTeleportTo
// 服务端校验（PlayerObject.TeleportToNPC@7883-7914）：
//   !ob.Info.CanTeleportTo → return；金币 >= Settings.TeleportToNPCCost（部署=3000）。
//
// 用法: NPCFlagFix plan                    （预览当前两标志的分布统计，不写库）
//       NPCFlagFix fix [bigmap|teleport|all] （执行：bigmap=只勾 ShowOnBigMap，
//                                              teleport=只勾 CanTeleportTo，
//                                              all/缺省=两个都勾）
// 注意: 必须在含 Server.MirDB 的目录运行；Server.exe 运行时拒绝执行（避免 SaveDB 冲突）。
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 1 || (args[0] != "plan" && args[0] != "fix"))
{
    Console.WriteLine("用法: NPCFlagFix plan");
    Console.WriteLine("      NPCFlagFix fix [bigmap|teleport|all]   （缺省=all）");
    Console.WriteLine("  plan: 预览两标志当前分布（不写库）");
    Console.WriteLine("  fix : 批量置 true 并保存 Server.MirDB");
    return 2;
}

var mode = args[0];
var target = args.Length >= 2 ? args[1].ToLowerInvariant() : "all";
if (target != "bigmap" && target != "teleport" && target != "all")
{
    Console.WriteLine($"未知目标: {args[1]}（可用: bigmap | teleport | all）");
    return 2;
}

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
var npcs = envir.NPCInfoList;

Console.WriteLine($"NPC 总数: {npcs.Count}");
Console.WriteLine();

if (mode == "plan")
{
    int bmTrue = npcs.Count(n => n.ShowOnBigMap);
    int tpTrue = npcs.Count(n => n.CanTeleportTo);
    Console.WriteLine($"[plan] ShowOnBigMap  = true: {bmTrue} / false: {npcs.Count - bmTrue}");
    Console.WriteLine($"[plan] CanTeleportTo = true: {tpTrue} / false: {npcs.Count - tpTrue}");
    Console.WriteLine();
    Console.WriteLine("CanTeleportTo 已勾的 NPC（现状）:");
    foreach (var n in npcs.Where(n => n.CanTeleportTo).OrderBy(n => n.Index))
        Console.WriteLine($"   {n.Index} {n.Name}  (MapIndex={n.MapIndex})");
    return 0;
}

// ---- fix 模式 ----
var setBigMap = target == "bigmap" || target == "all";
var setTeleport = target == "teleport" || target == "all";

int changedBigMap = 0, changedTeleport = 0;
foreach (var n in npcs)
{
    if (setBigMap && !n.ShowOnBigMap) { n.ShowOnBigMap = true; changedBigMap++; }
    if (setTeleport && !n.CanTeleportTo) { n.CanTeleportTo = true; changedTeleport++; }
}

Console.WriteLine($"[fix] 变更统计: ShowOnBigMap 置 true {changedBigMap} 个, CanTeleportTo 置 true {changedTeleport} 个。");

if (changedBigMap + changedTeleport == 0)
{
    Console.WriteLine("[fix] 无需修改。");
    return 0;
}

Console.WriteLine($"[fix] 保存 Server.MirDB（自动备份在 DatabaseBackup）...");
Envir.Main.SaveDB();
Console.WriteLine("完成。请重启服务端，进游戏在大地图选中 NPC 验证列表与传送按钮（传送费 TeleportToNPCCost=3000 金币/次）。");
return 0;
