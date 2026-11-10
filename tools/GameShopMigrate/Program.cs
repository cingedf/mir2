using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Server.MirEnvir;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("用法: GameShopMigrate export|import <json文件>");
    Console.WriteLine("注意: 必须在目标库所在目录作为当前工作目录运行（Envir.DatabasePath = \".\\Server.MirDB\"）。");
    return 2;
}

var mode = args[0];
var jsonPath = Path.GetFullPath(args[1]);
var dbPath = Path.Combine(".", "Server.MirDB");

if (!File.Exists(dbPath))
{
    Console.WriteLine($"未找到数据库文件: {Path.GetFullPath(dbPath)} — 请在含 Server.MirDB 的目录下运行。");
    return 3;
}

Console.WriteLine($"数据库: {Path.GetFullPath(dbPath)}");

switch (mode)
{
    case "export": Export(jsonPath); break;
    case "import": Import(jsonPath); break;
    case "seed": Seed(jsonPath, args.Length > 2 ? args[2] : null); break;
    case "items": Items(jsonPath); break;
    case "clear": Clear(); break;
    default:
        Console.WriteLine($"未知模式: {mode}（可用 export / import / seed / items / clear）");
        return 2;
}

return 0;


// 无头工具的场景：Envir.LoadDB 里 GameShopList 的绑定永远查 Envir.Edit.ItemInfoList
// （Envir.cs@1445 → Main.BindGameShop(item) → 遍历 Edit.ItemInfoList），
// 而无头进程没人去加载 Edit，所以第一遍读取会把货架全部静默丢弃。
// 解法：先跑一遍 LoadDB 只为取到 Main.ItemInfoList，把它灌进 Edit，再正式 LoadDB 一次，
// 让 BindGameShop 有数据可查，货架得以完整恢复。
void LoadDbTwoPass()
{
    Envir.Main.LoadDB();
    var edit = Envir.Edit;
    edit.ItemInfoList.Clear();
    edit.ItemInfoList.AddRange(Envir.Main.ItemInfoList);
    Envir.Main.LoadDB();
}


// 导出全部物品名清单，便于跨库对照
void Items(string outFile)
{
    Console.WriteLine("== items 模式: 导出 ItemInfoList 清单 ==");
                LoadDbTwoPass();
    var sb = new StringBuilder();
    sb.AppendLine("Index\tName\tType\tPrice");
    foreach (var info in Envir.Main.ItemInfoList)
        sb.AppendLine($"{info.Index}\t{info.Name}\t{info.Type}\t{info.Price}");
    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"共 {Envir.Main.ItemInfoList.Count} 条 → {outFile}");
}

void Export(string outJson)
{
    Console.WriteLine("== Phase A: 加载源库并导出 GameShopList ==");
                LoadDbTwoPass();

    var list = new List<ShopEntry>();
    foreach (var i in Envir.Main.GameShopList)
    {
        list.Add(new ShopEntry
        {
            Name = i.Info?.Name,
            SourceItemIndex = i.Info?.Index ?? i.ItemIndex,
            GoldPrice = i.GoldPrice,
            CreditPrice = i.CreditPrice,
            Count = i.Count,
            Class = i.Class,
            Category = i.Category,
            Stock = i.Stock,
            IStock = i.iStock,
            Deal = i.Deal,
            TopItem = i.TopItem,
            CanBuyGold = i.CanBuyGold,
            CanBuyCredit = i.CanBuyCredit,
        });
    }

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    File.WriteAllText(outJson, JsonSerializer.Serialize(list, options), new UTF8Encoding(false));
    Console.WriteLine($"已导出 {list.Count} 条到 {outJson}（其中名称为空的 {list.Count(e => e.Name == null)} 条）");
}

void Import(string inJson)
{
    Console.WriteLine($"== Phase B: 加载目标库并按物品名上架 ==");
    var entries = JsonSerializer.Deserialize<List<ShopEntry>>(File.ReadAllText(inJson), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    Console.WriteLine($"清单共 {entries.Count} 条");

                LoadDbTwoPass();

    var byName = new Dictionary<string, ItemInfo>(Envir.Main.ItemInfoList.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var info in Envir.Main.ItemInfoList)
        if (!string.IsNullOrEmpty(info.Name) && !byName.ContainsKey(info.Name))
            byName.Add(info.Name, info);

    // 目标库已有货架（幂等重放保护）
    var existing = new HashSet<string>(Envir.Main.GameShopList.Select(g => g.Info?.Name ?? ""), StringComparer.OrdinalIgnoreCase);

    int added = 0, skippedDup = 0, skippedMissing = 0;
    var missing = new List<string>();

    foreach (var e in entries)
    {
        if (e == null || string.IsNullOrWhiteSpace(e.Name)) { skippedMissing++; missing.Add("<清单条目名为空>"); continue; }
        if (existing.Contains(e.Name)) { skippedDup++; Console.WriteLine($"[跳过-已在架] {e.Name}"); continue; }
        if (!byName.TryGetValue(e.Name, out var info)) { skippedMissing++; missing.Add(e.Name); Console.WriteLine($"[跳过-目标库无此物品] {e.Name}"); continue; }

        var item = new GameShopItem
        {
            Info = info,
            ItemIndex = info.Index,
            GIndex = ++Envir.Main.GameshopIndex,
            GoldPrice = e.GoldPrice,
            CreditPrice = e.CreditPrice,
            Count = e.Count,
            Class = e.Class,
            Category = e.Category ?? info.Type.ToString(),
            Stock = e.Stock,
            iStock = e.IStock,
            Deal = e.Deal,
            TopItem = e.TopItem,
            Date = DateTime.Now,
            CanBuyGold = e.CanBuyGold,
            CanBuyCredit = e.CanBuyCredit,
        };
        Envir.Main.GameShopList.Add(item);
        existing.Add(e.Name);
        added++;
        Console.WriteLine($"[上架] {info.Name} (Index={info.Index}, G={item.GIndex}, 金币={item.GoldPrice}, 元宝={item.CreditPrice}, 数量={item.Count}, 库存={item.Stock})");
    }

    Console.WriteLine($"映射结果: 上架 {added} / 已在架跳过 {skippedDup} / 目标库缺物跳过 {skippedMissing}");
    if (missing.Count > 0)
    {
        Console.WriteLine("目标库缺失物品: " + string.Join(", ", missing));
    }

    Console.WriteLine("保存目标库 ...");
    Envir.Main.SaveDB();
    Console.WriteLine("SaveDB 完成，货架状态已写入 Server.MirDB（自动备份在 DatabaseBackup 目录）。");
}

// P安全检查: 目标 Server.exe 正在运行时拒绝写库
static void GuardTargetStopped()
{
    if (Process.GetProcessesByName("Server").Length > 0)
        throw new InvalidOperationException("检测到 Server.exe 正在运行 — 为避免写库冲突请先关闭服务端再执行 seed。");
}

static (string name, long gameGold, long gold)? ParseShopLine(string line)
{
    var m = System.Text.RegularExpressions.Regex.Match(
        line, @"^(.+?):\s*GameGold:\s*([\d,]+)\s+Gold:\s*([\d,]+)\s*$");
    if (!m.Success) return null;
    return (m.Groups[1].Value.Trim(),
            long.Parse(m.Groups[2].Value.Replace(",", "")),
            long.Parse(m.Groups[3].Value.Replace(",", "")));
}

void Seed(string txtPath, string mapPath)
{
    GuardTargetStopped();

    Console.WriteLine("== seed 模式: 参考 run\\Server 商城导出单，为 deploy 库上架 ==");
    Console.WriteLine($"读单: {txtPath}" + (mapPath != null ? $" | 映射: {mapPath}" : " | (无映射表)"));

    // ---- 解析参考清单 ----
    string[] lines = File.ReadAllLines(txtPath, Encoding.UTF8);
    var txtEntries = new List<(string name, long gameGold, long gold)>();
    foreach (var raw in lines)
    {
        if (string.IsNullOrWhiteSpace(raw)) continue;
        var p = ParseShopLine(raw);
        if (p.HasValue) txtEntries.Add(p.Value);
    }
    Console.WriteLine($"解析条目: {txtEntries.Count}");
    if (txtEntries.Count == 0)
    {
        Console.WriteLine("解析到 0 条（可能编码问题，尝试 GBK 重读）...");
        lines = File.ReadAllLines(txtPath, Encoding.GetEncoding("GBK"));
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = ParseShopLine(raw);
            if (p.HasValue) txtEntries.Add(p.Value);
        }
        Console.WriteLine($"GBK 重读后条目: {txtEntries.Count}");
        if (txtEntries.Count == 0) { Console.WriteLine("放弃：无法解析任何条目。"); return; }
    }
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("目标库不存在。");
        return;
    }

    // ---- 读取映射表 ----
    Dictionary<string, string[]> map = new(StringComparer.Ordinal);
    Dictionary<string, string[]> extras = new(StringComparer.Ordinal);
    if (mapPath != null)
    {
        var rawMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(mapPath), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        foreach (var (k, v) in rawMap)
        {
            if (k == "//说明") continue;
            if (k == "__extra__")
            {
                foreach (var (ek, ev) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(v.GetRawText(), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    var parts2 = JsonSerializer.Deserialize<string[]>(ev.GetRawText(), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    if (parts2 != null && parts2.Length >= 2) extras[ek] = parts2;
                }
                continue;
            }
            var parts = JsonSerializer.Deserialize<string[]>(v.GetRawText(), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (parts != null && parts.Length >= 2) map[k] = parts;
        }
        Console.WriteLine($"映射表: {map.Count} 条参考映射 + {extras.Count} 条额外单");
    }

                LoadDbTwoPass();

    var byName = new Dictionary<string, ItemInfo>(Envir.Main.ItemInfoList.Count, StringComparer.OrdinalIgnoreCase);
    var allNames = new List<string>();
    foreach (var info in Envir.Main.ItemInfoList)
        if (!string.IsNullOrEmpty(info.Name))
        {
            if (!byName.ContainsKey(info.Name)) byName.Add(info.Name, info);
            allNames.Add(info.Name);
        }

    var existing = new HashSet<string>(Envir.Main.GameShopList.Select(g => g.Info?.Name ?? ""), StringComparer.OrdinalIgnoreCase);

    int added = 0, missing = 0, dup = 0;
    var missingNames = new List<string>();
    var newEntries = new List<(string targetName, string lane)>();
    foreach (var (name, _, _) in txtEntries) newEntries.Add((name, null));
    foreach (var e in extras) newEntries.Add((e.Key, e.Value[1]));

    foreach (var (name, laneOverride) in newEntries)
    {
        if (existing.Contains(name)) { dup++; Console.WriteLine($"[跳过-已在架] {name}"); continue; }

        string deployName = null; string lane = "gold";
        if (map.TryGetValue(name, out var mv)) { deployName = mv[0]; lane = mv[1]; }
        else if (laneOverride != null && extras.TryGetValue(name, out var ev2)) { deployName = ev2[0]; lane = ev2[1]; }

        ItemInfo info = null;
        if (deployName != null)
            byName.TryGetValue(deployName, out info);
        if (info == null && deployName == null)
        {
            // 无映射时才做名字模糊回退
            byName.TryGetValue(name, out info);
            if (info == null)
            {
                var alt = name.Replace('[', '（').Replace(']', '）');
                byName.TryGetValue(alt, out info);
                if (info == null)
                {
                    alt = name.Replace('（', '[').Replace('）', ']');
                    byName.TryGetValue(alt, out info);
                }
            }
            if (info == null)
            {
                var baseName = System.Text.RegularExpressions.Regex.Replace(name, @"[\d一二三四五六七八九]+$", "").TrimEnd();
                if (baseName.Length >= 2)
                {
                    var cand = allNames.FirstOrDefault(n => n.StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
                    if (cand != null) info = byName[cand];
                }
            }
        }

        if (info == null) { missing++; missingNames.Add(name); Console.WriteLine($"[跳过-未映射且目标库无此物品] {name}"); continue; }

        // ---- 定价 ----
        long goldPrice, gameGold;
        if (lane == "premium")
        {
            goldPrice = Math.Max((long)info.Price, 500);                       // 珍藏档：金币=基价
            gameGold = Math.Max(((goldPrice + 299) / 300 + 9) / 10 * 10, 10);   // 元宝=金币÷300（向上取整到10，下限10）
        }
        else
        {
            goldPrice = (long)info.Price * 3;                                   // 功能档：金币=基价×3
            if (info.Price <= 1) goldPrice = 30000;                             // 零基价（时长类）统一 3 万
            goldPrice = Math.Max(((goldPrice + 99) / 100) * 100, 1000);
            gameGold = goldPrice >= 75000 ? ((goldPrice + 749) / 750 * 750) : 0; // 元宝=金币÷750，仅在金币价≥7.5万时开放
        }

        var item = new GameShopItem
        {
            Info = info,
            ItemIndex = info.Index,
            GIndex = ++Envir.Main.GameshopIndex,
            GoldPrice = (uint)goldPrice,
            CreditPrice = (uint)gameGold,
            Count = 1,
            Class = "All",
            Category = info.Type.ToString(),
            Stock = 0,
            iStock = false,
            Deal = false,
            TopItem = false,
            Date = DateTime.Now,
            CanBuyGold = true,
            CanBuyCredit = gameGold > 0,
        };
        Envir.Main.GameShopList.Add(item);
        existing.Add(name);
        added++;
        Console.WriteLine($"[上架·{lane}] {info.Name} (Index={info.Index}, 基价={info.Price}, 金币={goldPrice}, 元宝={gameGold}) 参考={name}");
    }

    Console.WriteLine($"结果: 上架 {added} / 已在架 {dup} / 缺货 {missing}");
    if (missingNames.Count > 0)
        Console.WriteLine("未上架的参考物品:\n  " + string.Join("\n  ", missingNames));

    Console.WriteLine("保存目标库 ...");
    Envir.Main.SaveDB();
    Console.WriteLine("SaveDB 完成，货架已写入 Server.MirDB（自动备份在 DatabaseBackup 目录）。");
}

// 清空货架（不重置 GameshopIndex，保持与历史计数一致）
void Clear()
{
    GuardTargetStopped();
    Console.WriteLine("== clear 模式: 清空 GameShopList ==");
                LoadDbTwoPass();
    int n = Envir.Main.GameShopList.Count;
    Envir.Main.GameShopList.Clear();
    Console.WriteLine($"已清空 {n} 条货架，GameshopIndex 保持 {Envir.Main.GameshopIndex} 不变。");
    Envir.Main.SaveDB();
    Console.WriteLine("SaveDB 完成。");
}

public class ShopEntry
{
    public string Name { get; set; }
    public int SourceItemIndex { get; set; }
    public uint GoldPrice { get; set; }
    public uint CreditPrice { get; set; }
    public ushort Count { get; set; }
    public string Class { get; set; }
    public string Category { get; set; }
    public int Stock { get; set; }
    public bool IStock { get; set; }
    public bool Deal { get; set; }
    public bool TopItem { get; set; }
    public bool CanBuyGold { get; set; }
    public bool CanBuyCredit { get; set; }
}
