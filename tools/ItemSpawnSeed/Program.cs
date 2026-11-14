using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server.MirEnvir;

// 物品种子工具：向 Server.MirDB 播种「高级通行证」特殊消耗品（素材③ 英雄双倍经验链路的触发物品）。
//
// 背景：HeroUseItems.txt 的 [@_UseItem(500)] 页（GIVEBUFF/GIVEHEROBUFF 双倍经验）此前是哑弹——
//   数据先行、物品未造（Server.MirDB 无 Shape=500 的 Type=21 物品），且枚举无「经验丰富」。
//   commit c6ae5ae（20261115实装英雄双倍经验链路与GIVEHEROBUFF）补齐 C# 侧后，
//   本工具补齐最后一环：造出可被玩家/英雄使用的触发物品。
//
// 物品规格（考证定稿，见 doc\20261115播种高级通行证物品.md）：
//   Index = ++ItemIndex（CreateItemInfo 先例：计数器可能大于现存最大，勿用 Max+1）
//   Name  = 高级通行证（幂等键，全库无同名）
//   Type  = 特殊消耗品(21)（UseItem:6470 case 命中 → CallDefaultNPC(UseItem, Shape)）
//   Shape = 500（全库空闲；[@_UseItem(500)] 页括号里是 Shape）
//   Effect= 0（Effect=1 会触发客户端快捷栏特殊逻辑，必须 0）
//   StackSize=20 / Price=1000 / Image=4570（与古代宗师祝福同图）/ Durability=0
//   Stats 空 / ToolTip null（Save 写 false 最干净）/ 其余全默认
//
// 用法: ItemSpawnSeed plan   （只预览，不写库）
//       ItemSpawnSeed apply  （执行并保存）
// 注意: 必须在含 Server.MirDB 的目录运行；Server.exe 运行时拒绝执行（避免 SaveDB 冲突）。
Console.OutputEncoding = Encoding.UTF8;

var apply = args.Length == 1 && args[0] == "apply";
if (args.Length != 1 || !(apply || args[0] == "plan"))
{
    Console.WriteLine("用法: ItemSpawnSeed plan|apply");
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

const string newName = "高级通行证";
const int newShape = 500;

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");
Envir.Main.LoadDB();
var envir = Envir.Main;

// 幂等检查：同名已存在直接退出
var existing = envir.ItemInfoList.FirstOrDefault(i => i.Name == newName);
if (existing != null)
{
    if (existing.Type == ItemType.特殊消耗品 && existing.Shape == newShape)
        Console.WriteLine($"物品「{newName}」(Index={existing.Index}, Type=特殊消耗品, Shape={newShape}) 已存在 — 无需重复播种。");
    else
        Console.WriteLine($"[异常] 存在同名但规格不符的物品 Index={existing.Index} Type={existing.Type} Shape={existing.Shape} — 请人工核查！");
    return 0;
}

// 异物报警：Shape=500 被其他物品占用
var shapeHolder = envir.ItemInfoList.FirstOrDefault(i => i.Shape == newShape);
if (shapeHolder != null)
{
    Console.WriteLine($"[异常] Shape={newShape} 已被 Index={shapeHolder.Index}「{shapeHolder.Name}」占用 — 请人工核查！");
    return 5;
}

// 计数器自愈：库内 ItemIndex 计数器与现存物品脱节（实测 deploy MirDB 存 ItemIndex=1 而物品 4320 件，
// 历史批量导入未维护计数器）。取 Max(ItemIndex, 最大Index)+1，并把计数器回填到正确值——
// 运行服 CreateItemInfo（Envir.cs:3016）依赖 ItemIndex 语义，落盘后漂移一并修复。
int maxExistingIndex = envir.ItemInfoList.Count > 0 ? envir.ItemInfoList.Max(i => i.Index) : 0;
Console.WriteLine($"计数器状态: ItemIndex={envir.ItemIndex} 物品数={envir.ItemInfoList.Count} 最大Index={maxExistingIndex}");

if (envir.ItemIndex < maxExistingIndex)
{
    Console.WriteLine($"[修复] ItemIndex 计数器({envir.ItemIndex})落后于最大 Index({maxExistingIndex}) — 回填计数器后播种。");
    envir.ItemIndex = maxExistingIndex;
}

int newIndex = ++envir.ItemIndex;
Console.WriteLine($"新物品: Index={newIndex}（ItemIndex 计数器推进，落盘后计数器={envir.ItemIndex}） \"{newName}\" Type=特殊消耗品 Shape={newShape}");

var item = new ItemInfo
{
    Index = newIndex,
    Name = newName,
    Type = ItemType.特殊消耗品,
    Grade = ItemGrade.普通,
    RequiredType = RequiredType.Level,
    RequiredAmount = 0,          // 0 = 无等级要求
    RequiredClass = RequiredClass.全职业,
    RequiredGender = RequiredGender.性别不限,
    Set = ItemSet.非套装,
    Shape = newShape,
    Weight = 1,
    Light = 0,
    Image = 4570,                // 古代宗师祝福同图（道具箱/通行证风格）
    Durability = 0,
    StackSize = 20,              // 可堆叠，方便批量发放
    Price = 1000,
    StartItem = false,
    Effect = 0,                  // 必须 0：Effect=1 触发客户端快捷栏特殊逻辑
    NeedIdentify = false,
    ShowGroupPickup = false,
    GlobalDropNotify = false,
    ClassBased = false,
    LevelBased = false,
    CanMine = false,
    CanFastRun = false,
    CanAwakening = false,
    Bind = BindMode.None,
    Unique = SpecialItemMode.None,
    RandomStatsId = 255,         // CreateItemInfo 先例（255=无随机属性池，越界安全）
    Slots = 0,
    Stats = new Stats(),         // 全空：触发逻辑只看 Shape
    ToolTip = null,              // Save 时写 false，最干净
};

if (!apply)
{
    Console.WriteLine();
    Console.WriteLine("[plan] 预览结束（未写库）。");
    return 0;
}

// ---- 落库 + 重载验证 ----
envir.ItemInfoList.Add(item);

Console.WriteLine();
Console.WriteLine("[apply] 保存 Server.MirDB（自动备份在 Back Up\\Database\\）...");
envir.SaveDB();

Console.WriteLine("重新加载验证...");
var verify = new Envir();
verify.LoadDB();
var vItem = verify.ItemInfoList.FirstOrDefault(i => i.Name == newName);
bool ok = vItem != null && vItem.Index == newIndex && vItem.Type == ItemType.特殊消耗品
          && vItem.Shape == newShape && vItem.StackSize == 20;

if (!ok)
{
    Console.WriteLine("[验证失败] 回读数据与预期不符 — 请检查数据库（播种已在文件中，需人工核查）。");
    return 7;
}

Console.WriteLine($"[验证通过] 物品 Index={vItem.Index} \"{vItem.Name}\" Type={vItem.Type} Shape={vItem.Shape} StackSize={vItem.StackSize} Price={vItem.Price}。");
Console.WriteLine("完成。请重启服务端生效；发放后玩家/英雄使用即触发 HeroUseItems.txt [@_UseItem(500)] 页（主人+英雄双倍经验 24 小时）。");
return 0;
