using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;

// 尊者系三 BOSS 刷怪点播种工具（素材补齐自主立项②）。
//
// 背景：尊者之王(Mon562N)/尊者之魔(Mon563N)/尊者之魂(Mon564N) 三 BOSS 怪物资料早已在库
//（Index=1102/1104/1106，Level999/HP32000），但全库零刷怪点——「定义了没上场」。
// 其中尊者之魂的封印技能 Mon564NSealing 已在 f516e7f 接线完毕（300 秒/30 秒冷却，
// 生命值强化/法力值强化 -25），客户端光效/图标齐备，只等 BOSS 上场。
//
// 动作：给 GRAYTEMPLE_3（灰色神殿3层，现仅黑暗之王一个 BOSS 点）追加三个 BOSS 刷怪点，
// 坐标经真实 .map 文件可行走校验（复用 SpawnProbe 的 Map.Load + WalkableCells 判定），
// 无效坐标自动在附近搜索最近可行走点。
//
// 用法: SpawnReseed plan   （只预览，不写库）
//       SpawnReseed apply  （执行并保存）
// 注意: 必须在含 Server.MirDB 与 Maps\ 的目录运行（deploy 根层）；Server.exe 运行时拒绝执行。
Console.OutputEncoding = Encoding.UTF8;

var apply = args.Length == 1 && args[0] == "apply";
if (args.Length != 1 || !(apply || args[0] == "plan"))
{
    Console.WriteLine("用法: SpawnReseed plan|apply");
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

const string targetMapFile = "GRAYTEMPLE_3";
const int bossDelay = 120;   // 分钟（BOSS 惯例 60-180，取中）

// (MonsterIndex, 名称, 期望坐标)——用主名条目（1102/1104/1106），0 后缀是编辑器复制变体留备用
var bosses = new (int Index, string Name, int X, int Y)[]
{
    (1102, "尊者之王", 40, 30),   // 厅内左上（100x100 单厅结构，可行走区 x30-70/y20-70）
    (1104, "尊者之魔", 62, 42),   // 厅内右中
    (1106, "尊者之魂", 50, 62),   // 厅内下中
};

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");
Envir.Main.LoadDB();
var envir = Envir.Main;

// ---- 1. 目标地图 ----
var targetMap = envir.MapInfoList.FirstOrDefault(m => m.FileName == targetMapFile);
if (targetMap == null)
{
    Console.WriteLine($"未找到目标地图 FileName=\"{targetMapFile}\"。");
    return 5;
}
Console.WriteLine($"目标地图: Index={targetMap.Index} \"{targetMap.FileName}\" 标题=\"{targetMap.Title}\" 现有刷怪点={targetMap.Respawns.Count}");

// ---- 2. BOSS 资料在场确认 ----
foreach (var (idx, name, _, _) in bosses)
{
    var info = envir.MonsterInfoList.FirstOrDefault(m => m.Index == idx);
    if (info == null)
    {
        Console.WriteLine($"未找到怪物 Index={idx}（{name}）—— 库数据缺失，中止。");
        return 6;
    }
    Console.WriteLine($"怪物确认: Index={info.Index} \"{info.Name}\" AI={info.AI} Level={info.Level} HP={info.Stats[Stat.HP]}");
}

// ---- 3. 幂等检查：任一目标 BOSS 已有刷怪点即退出 ----
var existing = targetMap.Respawns.Where(r => bosses.Any(b => b.Index == r.MonsterIndex)).ToList();
if (existing.Count > 0)
{
    foreach (var r in existing)
        Console.WriteLine($"已存在: 怪{r.MonsterIndex} ({r.Location.X},{r.Location.Y}) Count={r.Count} Delay={r.Delay}");
    Console.WriteLine("尊者系刷怪点已播种 — 无需重复执行。");
    return 0;
}

// ---- 4. 可行走校验（真实 .map 文件）----
var map = new Map(targetMap);
if (!map.Load())
{
    Console.WriteLine($"地图文件加载失败（Maps\\{targetMapFile}.map 缺失或损坏？）— 无法校验坐标，中止。");
    return 7;
}
Console.WriteLine($"地图尺寸: {map.Width}x{map.Height} 全图可行走格={map.WalkableCells?.Count ?? 0}");

// 10x10 区块热力图（选点依据：三个 BOSS 应分散在不同区域）
if (map.WalkableCells != null)
{
    Console.WriteLine("可行走分布（10x10 区块计数）:");
    for (int by = 0; by < map.Height; by += 10)
    {
        var row = new List<string>();
        for (int bx = 0; bx < map.Width; bx += 10)
        {
            int c = map.WalkableCells.Count(p => p.X >= bx && p.X < bx + 10 && p.Y >= by && p.Y < by + 10);
            row.Add(c == 0 ? "  ." : $"{c,3}");
        }
        Console.WriteLine($"  y{by,3}: {string.Join(" ", row)}");
    }
}

Point Resolve(Point want)
{
    if (map.WalkableCells == null) return want;
    if (map.WalkableCells.Contains(want)) return want;
    // 螺旋扩圈找最近可行走点（步长 5，最多 25 圈 = 半径 125）
    for (int radius = 5; radius <= 125; radius += 5)
    {
        for (int dx = -radius; dx <= radius; dx += 5)
        {
            for (int dy = -radius; dy <= radius; dy += 5)
            {
                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue; // 只扫圈边
                var p = new Point(want.X + dx, want.Y + dy);
                if (p.X < 0 || p.Y < 0 || p.X >= map.Width || p.Y >= map.Height) continue;
                if (map.WalkableCells.Contains(p)) return p;
            }
        }
    }
    return want; // 找不到就原样返回（验证阶段会暴露）
}

var resolved = new List<(int Index, string Name, Point Loc)>();
foreach (var (idx, name, x, y) in bosses)
{
    var want = new Point(x, y);
    var got = Resolve(want);
    if (got != want)
        Console.WriteLine($"坐标纠偏: {name} 期望({want.X},{want.Y}) → 可行走({got.X},{got.Y})");
    resolved.Add((idx, name, got));
}

// ---- 5. 构造刷怪点 ----
var newRespawns = new List<RespawnInfo>();
foreach (var (idx, name, loc) in resolved)
{
    var resp = new RespawnInfo
    {
        MonsterIndex = idx,
        Location = new Point(loc.X, loc.Y),
        Count = 1,                 // BOSS 惯例单只
        Spread = 0,                // 定点
        Delay = bossDelay,         // 分钟（Map.Process: delay * Settings.Minute）
        Direction = 0,
        RoutePath = string.Empty,
        RandomDelay = 0,
        SaveRespawnTime = false,
        RespawnTicks = 0,
        RespawnIndex = ++envir.RespawnIndex,
    };
    newRespawns.Add(resp);
    Console.WriteLine($"新增: 怪{idx} {name} ({loc.X},{loc.Y}) Count=1 Spread=0 Delay={bossDelay} RespawnIndex={resp.RespawnIndex}");
}

Console.WriteLine($"（既有参照: 黑暗之王 {string.Join(" | ", targetMap.Respawns.Where(r => r.MonsterIndex != 1102 && r.MonsterIndex != 1104 && r.MonsterIndex != 1106).Select(r => $"怪{r.MonsterIndex} ({r.Location.X},{r.Location.Y}) Delay={r.Delay}"))}）");

if (!apply)
{
    Console.WriteLine();
    Console.WriteLine("[plan] 预览结束（未写库）。");
    return 0;
}

// ---- 6. 落库 + 重载验证 ----
targetMap.Respawns.AddRange(newRespawns);

Console.WriteLine();
Console.WriteLine("[apply] 保存 Server.MirDB（自动备份在 Back Up\\Database\\）...");
envir.SaveDB();

Console.WriteLine("重新加载验证...");
var verify = new Envir();
verify.LoadDB();
var vMap = verify.MapInfoList.FirstOrDefault(m => m.FileName == targetMapFile);
bool ok = vMap != null && resolved.All(r =>
    vMap.Respawns.Any(x => x.MonsterIndex == r.Index && x.Location.X == r.Loc.X && x.Location.Y == r.Loc.Y && x.Delay == bossDelay));

if (!ok)
{
    Console.WriteLine("[验证失败] 回读数据与预期不符 — 请检查数据库（播种已在文件中，需人工核查）。");
    return 8;
}

Console.WriteLine($"[验证通过] {targetMapFile} 现有刷怪点 {vMap.Respawns.Count} 个，尊者系 3 处全部落位。");
Console.WriteLine("完成。请重启服务端生效，验证路径: @move GRAYTEMPLE_3 后观察三 BOSS 房；尊者之魂攻击触发封印光效（Mon564N 帧 1016 段）。");
return 0;
