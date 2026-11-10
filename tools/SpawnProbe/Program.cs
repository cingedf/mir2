using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// 一次性诊断探针：读取当前目录 Server.MirDB + Maps，复现 Map.Load 的可行走格子过滤，
// 定位“怪物刷新失败”根因（刷怪框内 0 可行走格子）。
// 用法: SpawnProbe <地图Index...>  （不带参数则只列出有刷怪框问题的前 20 张地图）
Console.OutputEncoding = Encoding.UTF8;

var dbPath = Path.Combine(".", "Server.MirDB");
if (!File.Exists(dbPath))
{
    Console.WriteLine($"未找到 {Path.GetFullPath(dbPath)} — 请在含 Server.MirDB 的目录运行。");
    return 3;
}

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");
Envir.Main.LoadDB();

var envir = Envir.Main;
var targets = new List<MapInfo>();
if (args.Length > 0)
{
    var wanted = args.Select(int.Parse).ToHashSet();
    targets = envir.MapInfoList.Where(m => wanted.Contains(m.Index)).ToList();
}
else
{
    targets = envir.MapInfoList.Take(0).ToList(); // 无参模式：不逐张分析，仅汇总
}

Console.WriteLine($"MapInfoList 共 {envir.MapInfoList.Count} 条");
Console.WriteLine();

// 1) 目标地图元信息
foreach (var mi in targets)
{
    Console.WriteLine($"== 地图 Index={mi.Index} 文件名=\"{mi.FileName}\" 标题=\"{mi.Title}\" 刷怪点数={mi.Respawns.Count}");
    foreach (var r in mi.Respawns)
        Console.WriteLine($"   刷怪: monster={r.MonsterIndex} at({r.Location.X},{r.Location.Y}) count={r.Count} spread={r.Spread}");
}

// 2) 无参模式：扫全库找“刷怪框可能越界”的地图（框右下角超出常见 1000 上限无法判——这里改判：文件缺失 + 坐标>=300 的框）
if (args.Length == 0)
{
    Console.WriteLine("---- 坐标>=300 或 spread>=100 的刷怪点（前 30 条） ----");
    int shown = 0;
    foreach (var mi in envir.MapInfoList)
        foreach (var r in mi.Respawns)
        {
            if (r.Location.X >= 300 || r.Location.Y >= 300 || r.Spread >= 100)
            {
                Console.WriteLine($"map {mi.Index} \"{mi.FileName}\" [{r.MonsterIndex}] at({r.Location.X},{r.Location.Y}) spread={r.Spread} count={r.Count}");
                if (++shown >= 30) break;
            }
            if (shown >= 30) break;
        }
    Console.WriteLine($"（共显示 {shown} 条）");
}

// 3) 对带参地图：加载真实 .map 文件并统计框内可行走格子
foreach (var mi in targets)
{
    Console.WriteLine();
    Console.WriteLine($"== 实测地图 Index={mi.Index} file=\"{mi.FileName}\"");
    var map = new Map(mi);
    bool loaded = map.Load();
    Console.WriteLine($"   Load()={loaded}  尺寸={map.Width}x{map.Height}  全图可行走格={map.WalkableCells?.Count ?? -1}");
    if (!loaded || map.WalkableCells == null) continue;

    foreach (var r in mi.Respawns)
    {
        var inBox = map.WalkableCells.Where(p =>
            p.X <= r.Location.X + r.Spread && p.X >= r.Location.X - r.Spread &&
            p.Y <= r.Location.Y + r.Spread && p.Y >= r.Location.Y - r.Spread).ToList();
        Console.WriteLine($"   刷怪框 ({r.Location.X},{r.Location.Y})±{r.Spread} 怪{r.MonsterIndex}: 框内可行走格 = {inBox.Count}");
    }
}
return 0;
