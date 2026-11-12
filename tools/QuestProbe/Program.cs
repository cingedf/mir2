using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.MirEnvir;
class P { static void Main() {
  var e = new Envir();
  bool ok = Envir.Main.LoadDB(); Envir.Main.LoadAccounts();
  Console.WriteLine($"LoadDB ok={ok} LoadVersion={Envir.LoadVersion}");
  var quests = Envir.Main.QuestInfoList;
  Console.WriteLine("QuestInfoList.Count=" + quests.Count);

  // 1) NPC 脚本 [Quests] 段挂号全扫描（正=接/负=交）
  var npcDir = Path.Combine(Directory.GetCurrentDirectory(), "Envir", "NPCs");
  var refs = new List<string>(); // 任务号 -> NPC 列表
  var refMap = new Dictionary<int, List<string>>();
  foreach (var f in Directory.GetFiles(npcDir, "*.txt", SearchOption.AllDirectories)) {
    string[] lines; try { lines = File.ReadAllLines(f); } catch { continue; }
    bool inQ = false;
    foreach (var raw in lines) {
      var line = raw.Trim();
      if (line.StartsWith("[")) { inQ = line.Equals("[Quests]", StringComparison.OrdinalIgnoreCase); continue; }
      if (!inQ) continue;
      int n;
      if (int.TryParse(line, out n)) {
        if (!refMap.ContainsKey(Math.Abs(n))) refMap[Math.Abs(n)] = new List<string>();
        refMap[Math.Abs(n)].Add(Path.GetFileNameWithoutExtension(f) + (n > 0 ? "(接)" : "(交)"));
      }
    }
  }
  Console.WriteLine("==== NPC 挂号任务号（有挂号的） ====");
  foreach (var k in refMap.Keys.OrderBy(x => x))
    Console.WriteLine($"  任务{k}: " + string.Join(", ", refMap[k]));

  // 2) 挂号 vs 625 条任务头 匹配检查
  Console.WriteLine("==== 挂号但无任务定义的号（空悬） ====");
  int missing = 0;
  foreach (var k in refMap.Keys.OrderBy(x => x)) {
    if (!quests.Any(q => q.Index == k)) { missing++; Console.WriteLine("  任务" + k + ": " + string.Join(", ", refMap[k])); }
  }
  Console.WriteLine("空挂数=" + missing);

  // 3) 625 条里有定义但无 NPC 挂号的（接取入口缺失）
  Console.WriteLine("==== 有定义但无 NPC 挂号的（前 40 条） ====");
  int orphans = 0;
  foreach (var q in quests) {
    if (!refMap.ContainsKey(q.Index)) { orphans++; if (orphans <= 40) Console.WriteLine($"  Idx={q.Index} Name={q.Name} File={q.FileName}"); }
  }
  Console.WriteLine("无挂号任务数=" + orphans + " / " + quests.Count);

  // 4) 任务详情文件缺失检查（MirDB 头指向的 FileName 是否存在 txt）
  Console.WriteLine("==== 详情文件缺失的任务头 ====");
  int nofile = 0;
  foreach (var q in quests) {
    if (string.IsNullOrEmpty(q.FileName)) continue;
    var p = Path.Combine(Directory.GetCurrentDirectory(), "Envir", "Quests", q.FileName.Replace('/', Path.DirectorySeparatorChar) + ".txt");
    // FileName 含子目录（如 比奇/边界村/1）
    if (!File.Exists(p)) { nofile++; if (nofile <= 20) Console.WriteLine("  Idx=" + q.Index + " Name=" + q.Name + " File=" + q.FileName); }
  }
  Console.WriteLine("详情文件缺失数=" + nofile);
}}
