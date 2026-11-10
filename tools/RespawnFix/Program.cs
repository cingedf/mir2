using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// 刷怪点修复工具：修复“刷怪框落在地图可行走区域之外”导致的怪物刷新失败风暴
// （现象：Logs\Spawn 每分钟成片“怪物刷新失败 地图452/453”）。
//
// 根因：地图 D2075/D2076（般若神殿5层东/西，Index=452/453）.map 实际尺寸 200×200，
// 但 DB 刷怪点按 400×400 设计网格布置：坐标 (50/150/250/350)²、spread=100，
// 凡坐标含 350 的框全部落图外（框内可行走格=0 → Spawn 必败）。
//
// 修复：将目标地图的全部刷怪点等比映射 ×0.5（坐标与 spread 同时折半），
// 保存前用真实 .map 复算每个刷怪框的可行走格数，任何框=0 则拒绝保存。
//
// 用法: RespawnFix fix <地图Index...>      （执行修复并保存，策略=×0.5 等比映射）
//       RespawnFix plan <地图Index...>     （只预览变换结果，不写库）
//       RespawnFix snap <地图Index...>     （策略=坏框中心吸附最近可行走格，spread 不变）
// 注意: 必须在含 Server.MirDB 的目录运行；Server.exe 运行时拒绝执行（避免 SaveDB 冲突）。
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2 || (args[0] != "fix" && args[0] != "plan" && args[0] != "snap"))
{
    Console.WriteLine("用法: RespawnFix fix|plan|snap <地图Index...>");
    Console.WriteLine("  plan: 预览变换结果（不写库）");
    Console.WriteLine("  fix : 执行修复并保存 Server.MirDB（策略=×0.5 等比映射）");
    Console.WriteLine("  snap: 执行修复并保存 Server.MirDB（策略=坏框中心吸附最近可行走格）");
    return 2;
}

var mode = args[0];
var snapMode = mode == "snap";
var wanted = args.Skip(1).Select(int.Parse).ToHashSet();

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

var targets = envir.MapInfoList.Where(m => wanted.Contains(m.Index)).ToList();
if (targets.Count == 0)
{
    Console.WriteLine($"未找到目标地图: {string.Join(", ", wanted)}");
    return 5;
}

int changedTotal = 0, zeroBoxTotal = 0;

foreach (var mi in targets)
{
    Console.WriteLine();
    Console.WriteLine($"== 地图 Index={mi.Index} 文件=\"{mi.FileName}\" 标题=\"{mi.Title}\" 刷怪点={mi.Respawns.Count}");

    // 加载真实地图，取实际尺寸与可行走格集合（复用引擎 Map.Load 的判定）
    var map = new Map(mi);
    if (!map.Load() || map.WalkableCells == null)
    {
        Console.WriteLine("   .map 加载失败，跳过。");
        continue;
    }
    Console.WriteLine($"   实际尺寸={map.Width}x{map.Height} 全图可行走格={map.WalkableCells.Count}");
    var walkable = new HashSet<(int X, int Y)>(map.WalkableCells.Select(p => (p.X, p.Y)));

    int changed = 0, zeroBox = 0;
    foreach (var r in mi.Respawns)
    {
        int oldX = r.Location.X, oldY = r.Location.Y, oldSpread = r.Spread;
        int boxBefore = CountBox(walkable, oldX, oldY, oldSpread);

        int newX, newY, newSpread;
        string strategy;
        if (snapMode)
        {
            // 吸附策略：坏框中心吸附到最近可行走格，spread 不变（贴近原设计分布）
            var best = FindNearestWalkable(walkable, oldX, oldY);
            newX = best.X; newY = best.Y; newSpread = oldSpread;
            strategy = "snap";
        }
        else
        {
            // 等比映射 ×0.5：坐标与 spread 同时折半（400×400 设计网格 → 实际地图）
            newX = Math.Max(0, oldX / 2);
            newY = Math.Max(0, oldY / 2);
            newSpread = Math.Max(1, oldSpread / 2);
            strategy = "map";
        }

        int boxAfter = CountBox(walkable, newX, newY, newSpread);

        string verdict;
        if (boxBefore > 0)
        {
            verdict = "不变(原框已有效)";  // 原框已有效则不动，保持最小改动
        }
        else if (boxAfter == 0)
        {
            verdict = "!!变换后仍为0，拒绝";
            zeroBox++;
        }
        else
        {
            verdict = strategy == "snap" ? "吸附" : "映射";
        }

        Console.WriteLine($"   怪{r.MonsterIndex} ({oldX},{oldY})±{oldSpread} 框内格={boxBefore}" +
                          $"  →  ({newX},{newY})±{newSpread} 框内格={boxAfter}  [{verdict}]");

        if (verdict == "映射" || verdict == "吸附")
        {
            r.Location = new System.Drawing.Point(newX, newY);
            r.Spread = (ushort)newSpread;
            changed++;
        }
    }

    Console.WriteLine($"   小计: 映射 {changed} / 变换后仍为0 {zeroBox} / 保持 {mi.Respawns.Count - changed}");
    changedTotal += changed;
    zeroBoxTotal += zeroBox;
}

if (mode == "plan")
{
    Console.WriteLine();
    Console.WriteLine($"[plan] 预览结束（未写库）: 映射 {changedTotal} 个刷怪点，仍有 {zeroBoxTotal} 个框为0。");
    return 0;
}

if (zeroBoxTotal > 0)
{
    Console.WriteLine();
    Console.WriteLine($"[fix] 存在 {zeroBoxTotal} 个变换后仍为0的刷怪框 — 拒绝保存，请人工调整。");
    return 6;
}

if (changedTotal == 0)
{
    Console.WriteLine();
    Console.WriteLine("[fix] 无需修改。");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"[fix] 保存 Server.MirDB（映射 {changedTotal} 个刷怪点，自动备份在 DatabaseBackup）...");
Envir.Main.SaveDB();
Console.WriteLine("完成。请重启服务端验证 Logs\\Spawn 刷怪失败日志是否停刷。");
return 0;

static int CountBox(HashSet<(int X, int Y)> walkable, int cx, int cy, int spread)
{
    int n = 0;
    foreach (var p in walkable)
    {
        if (p.X <= cx + spread && p.X >= cx - spread && p.Y <= cy + spread && p.Y >= cy - spread)
            n++;
    }
    return n;
}

static (int X, int Y) FindNearestWalkable(HashSet<(int X, int Y)> walkable, int cx, int cy)
{
    // 从目标点螺旋外扩，找最近可行走格（地图不大，直接线性扫也可以，但螺旋更快且确定性）
    int bestX = 0, bestY = 0, bestDist = int.MaxValue;
    foreach (var p in walkable)
    {
        int d = Math.Abs(p.X - cx) + Math.Abs(p.Y - cy);
        if (d < bestDist)
        {
            bestDist = d;
            bestX = p.X; bestY = p.Y;
        }
    }
    return (bestX, bestY);
}
