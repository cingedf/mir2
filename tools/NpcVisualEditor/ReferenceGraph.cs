using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 对象引用图谱 + 影响分析（纯逻辑，只读）。从离线 Envir.Edit 快照 + 文本提取静态引用边：
    /// 强类型边（地图↔怪物/NPC/任务、NPC↔任务、任务前置）+ 任务文本边（按名字典解析，
    /// 不依赖 QuestInfo 静态 Envir——它指向 Envir.Main，与编辑器加载的 Envir.Edit 是两个实例）。
    /// 影响分析 = 给定对象的入边引用闭包，分强类型/文本两类，支持正/反向。
    /// </summary>
    public static class ReferenceGraph
    {
        public enum NodeKind { Map, Monster, NPC, Quest, Item, Script }
        public enum EdgeKind
        {
            MapRespawn,       // E1 地图→怪物（刷怪点）
            MapMovement,      // E2 地图→地图（传送门）
            NPCMap,           // E3 NPC→地图
            NPCScript,        // E4 NPC→脚本
            NPCCollectQuest,  // E5a NPC→任务（收集）
            NPCFinishQuest,   // E5b NPC→任务（交付）
            QuestRequired,    // E6 任务→前置任务
            QuestNpc,         // E7 任务→NPC
            MonsterDrop,      // E8/E9 怪物→掉落物品（文本解析）
            QuestRewardItem,  // E10a 任务→奖励物品（文本）
            QuestKillMonster, // E10b 任务→击杀怪物（文本）
            QuestItemTask     // E10c 任务→物品任务（文本）
        }

        public class GraphNode
        {
            public NodeKind Kind;
            public string Key;          // 唯一键：Kind:Id
            public int Id;
            public string DisplayName;
            public int OutDegree, InDegree;
        }

        public class GraphEdge
        {
            public EdgeKind Kind;
            public string FromKey, ToKey;
            public string Evidence;     // 证据位置（字段名或文本行）
            public bool IsText;         // 是否文本推导边
        }

        public class Graph
        {
            public Dictionary<string, GraphNode> Nodes = new(StringComparer.OrdinalIgnoreCase);
            public List<GraphEdge> Edges = new();
            public Dictionary<string, List<GraphEdge>> ByTarget = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<GraphEdge>> BySource = new(StringComparer.OrdinalIgnoreCase);
        }

        private static string Key(NodeKind k, int id) => $"{k}:{id}";

        /// <summary>从离线 Envir 快照构建引用图。serverDir 用于任务/掉落文本解析。</summary>
        public static Graph Build(Envir envir, string serverDir)
        {
            var g = new Graph();
            var maps = envir.MapInfoList.ToList();
            var mons = envir.MonsterInfoList.ToList();
            var npcs = envir.NPCInfoList.ToList();
            var quests = envir.QuestInfoList.ToList();
            var items = envir.ItemInfoList.ToList();

            // 名字典（避开 QuestInfo 静态 Envir.Main 陷阱：自己按名字典解析引用）
            var monsterByName = BuildNameMap(mons.Select(m => (m.Name, m.Index)));
            var itemByName = BuildNameMap(items.Select(i => (i.Name, i.Index)));
            var mapByIndex = maps.ToDictionary(m => m.Index, m => m);

            // ---- 节点 ----
            foreach (var m in maps) AddNode(g, NodeKind.Map, m.Index, m.Title ?? m.FileName);
            foreach (var m in mons) AddNode(g, NodeKind.Monster, m.Index, m.Name);
            foreach (var n in npcs) AddNode(g, NodeKind.NPC, n.Index, n.Name ?? n.FileName);
            foreach (var q in quests) AddNode(g, NodeKind.Quest, q.Index, q.Name ?? ("任务" + q.Index));
            foreach (var i in items) AddNode(g, NodeKind.Item, i.Index, i.Name);

            // ---- E1 地图→怪物（刷怪）----
            foreach (var m in maps)
                foreach (var rp in m.Respawns)
                    if (mapByIndex.ContainsKey(m.Index))
                        AddEdge(g, EdgeKind.MapRespawn, Key(NodeKind.Map, m.Index),
                            Key(NodeKind.Monster, rp.MonsterIndex), $"Respawns[{rp.MonsterIndex}]", false);

            // ---- E2 地图→地图（传送门）----
            foreach (var m in maps)
                foreach (var mv in m.Movements)
                    if (mapByIndex.ContainsKey(mv.MapIndex))
                        AddEdge(g, EdgeKind.MapMovement, Key(NodeKind.Map, m.Index),
                            Key(NodeKind.Map, mv.MapIndex), $"Movements[{mv.MapIndex}]", false);

            // ---- E3 NPC→地图 ----
            foreach (var n in npcs)
                if (mapByIndex.ContainsKey(n.MapIndex))
                    AddEdge(g, EdgeKind.NPCMap, Key(NodeKind.NPC, n.Index),
                        Key(NodeKind.Map, n.MapIndex), "MapIndex", false);

            // ---- E4 NPC→脚本（脚本节点懒建）----
            foreach (var n in npcs)
                if (!string.IsNullOrEmpty(n.FileName))
                {
                    string sk = "Script:" + n.FileName;
                    if (!g.Nodes.ContainsKey(sk))
                        g.Nodes[sk] = new GraphNode { Kind = NodeKind.Script, Key = sk, DisplayName = n.FileName };
                    AddEdge(g, EdgeKind.NPCScript, Key(NodeKind.NPC, n.Index), sk, n.FileName, false);
                }

            // ---- E5 NPC→任务（收集/交付）----
            foreach (var n in npcs)
            {
                foreach (var q in n.CollectQuestIndexes)
                    AddEdge(g, EdgeKind.NPCCollectQuest, Key(NodeKind.NPC, n.Index),
                        Key(NodeKind.Quest, q), "CollectQuestIndexes", false);
                foreach (var q in n.FinishQuestIndexes)
                    AddEdge(g, EdgeKind.NPCFinishQuest, Key(NodeKind.NPC, n.Index),
                        Key(NodeKind.Quest, q), "FinishQuestIndexes", false);
            }

            // ---- E6 任务→前置任务 ----
            foreach (var q in quests)
                if (q.RequiredQuest > 0)
                    AddEdge(g, EdgeKind.QuestRequired, Key(NodeKind.Quest, q.Index),
                        Key(NodeKind.Quest, q.RequiredQuest), "RequiredQuest", false);

            // ---- E7 任务→NPC ----
            foreach (var q in quests)
            {
                if (q.NpcIndex > 0)
                    AddEdge(g, EdgeKind.QuestNpc, Key(NodeKind.Quest, q.Index),
                        Key(NodeKind.NPC, (int)q.NpcIndex), "NpcIndex", false);
            }

            // ---- E8/E9 怪物→掉落物品（文本解析，MDB 中 Drops 恒空）----
            var dropDir = Path.Combine(serverDir, "Envir", "Drops");
            if (Directory.Exists(dropDir))
            {
                var dropFiles = Directory.GetFiles(dropDir, "*.txt");
                var byStem = dropFiles.ToDictionary(f => Path.GetFileNameWithoutExtension(f),
                    f => f, StringComparer.OrdinalIgnoreCase);
                foreach (var m in mons)
                {
                    string dropName = string.IsNullOrEmpty(m.DropPath) ? m.Name : m.DropPath;
                    if (string.IsNullOrEmpty(dropName) || !byStem.TryGetValue(dropName, out var path)) continue;
                    var parsed = DropAnalyzer.ParseFile(path, null);
                    foreach (var e in DropAnalyzer.Flatten(parsed.Entries))
                    {
                        if (e.IsGold || e.IsGroup || string.IsNullOrEmpty(e.ItemName)) continue;
                        if (itemByName.TryGetValue(e.ItemName, out int itemId))
                            AddEdge(g, EdgeKind.MonsterDrop, Key(NodeKind.Monster, m.Index),
                                Key(NodeKind.Item, itemId), $"{Path.GetFileName(path)}:{e.Raw}", true);
                    }
                }
            }

            // ---- E10 任务→奖励/击杀/物品（任务文本，自己解析）----
            var questDir = Path.Combine(serverDir, "Envir", "Quests");
            if (Directory.Exists(questDir))
            {
                foreach (var q in quests)
                {
                    if (string.IsNullOrEmpty(q.FileName)) continue;
                    string qpath = Path.Combine(questDir, q.FileName.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(qpath)) continue;
                    foreach (var raw in File.ReadAllLines(qpath, DropFormatter.DetectEncoding(qpath).enc))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;
                        // 每行首个词通常是物品/怪物名，后续是数量；按名字典归一化匹配
                        string name = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (monsterByName.TryGetValue(name, out int mid))
                            AddEdge(g, EdgeKind.QuestKillMonster, Key(NodeKind.Quest, q.Index),
                                Key(NodeKind.Monster, mid), q.FileName + ":" + line, true);
                        if (itemByName.TryGetValue(name, out int iid))
                            AddEdge(g, EdgeKind.QuestRewardItem, Key(NodeKind.Quest, q.Index),
                                Key(NodeKind.Item, iid), q.FileName + ":" + line, true);
                    }
                }
            }

            // 度统计 + 边索引
            foreach (var e in g.Edges)
            {
                if (g.Nodes.TryGetValue(e.FromKey, out var from)) from.OutDegree++;
                if (g.Nodes.TryGetValue(e.ToKey, out var to)) to.InDegree++;
                if (!g.ByTarget.ContainsKey(e.ToKey)) g.ByTarget[e.ToKey] = new List<GraphEdge>();
                g.ByTarget[e.ToKey].Add(e);
                if (!g.BySource.ContainsKey(e.FromKey)) g.BySource[e.FromKey] = new List<GraphEdge>();
                g.BySource[e.FromKey].Add(e);
            }
            return g;
        }

        /// <summary>名字典归一化：去空格 + 忽略大小写 + 去 (M)/(F) 后缀。</summary>
        private static Dictionary<string, int> BuildNameMap(IEnumerable<(string name, int id)> src)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, id) in src)
            {
                if (string.IsNullOrEmpty(name)) continue;
                string key = Normalize(name);
                if (!d.ContainsKey(key)) d[key] = id;
            }
            return d;
        }

        private static string Normalize(string s)
        {
            s = s.Replace(" ", "").Replace("\t", "");
            foreach (var suf in new[] { "(M)", "(F)", "（男）", "（女）" })
                if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) s = s[..^suf.Length];
            return s;
        }

        private static void AddNode(Graph g, NodeKind k, int id, string name)
        {
            string key = Key(k, id);
            if (!g.Nodes.ContainsKey(key))
                g.Nodes[key] = new GraphNode { Kind = k, Key = key, Id = id, DisplayName = name };
        }

        private static void AddEdge(Graph g, EdgeKind k, string from, string to, string ev, bool text)
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return; // 去重自环
            // 去重重复边
            if (g.Edges.Any(e => e.Kind == k && e.FromKey == from && e.ToKey == to)) return;
            g.Edges.Add(new GraphEdge { Kind = k, FromKey = from, ToKey = to, Evidence = ev, IsText = text });
        }

        /// <summary>影响分析：给定对象 key，返回引用它的入边闭包（上游依赖者）。</summary>
        public static List<GraphEdge> ImpactClosure(Graph g, string key, bool reverse)
        {
            var result = new List<GraphEdge>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
            var queue = new Queue<string>();
            queue.Enqueue(key);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var index = reverse ? g.ByTarget : g.BySource;
                if (!index.TryGetValue(cur, out var edges)) continue;
                foreach (var e in edges)
                {
                    result.Add(e);
                    string next = reverse ? e.FromKey : e.ToKey;
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            return result;
        }
    }
}
