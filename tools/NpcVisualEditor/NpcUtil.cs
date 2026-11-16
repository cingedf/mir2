using Server.MirDatabase;

namespace NpcVisualEditor
{
    /// <summary>
    /// NPC 显示名安全取值。引擎 NPCInfo.GameName 内部直接对 Name 调用 Contains("_")，
    /// 当 Name 为 null（脏数据 / 未初始化 NPC）时抛 NullReferenceException。
    /// 编辑器侧统一走此 helper：复刻引擎语义（按 _ 切分取最后一段）+ null 防御，不改引擎文件。
    /// </summary>
    internal static class NpcUtil
    {
        public static string SafeName(NPCInfo npc)
        {
            if (npc == null) return "(空NPC)";
            string s = npc.Name;
            if (string.IsNullOrEmpty(s))
                return string.IsNullOrEmpty(npc.FileName) ? $"NPC#{npc.Index}" : npc.FileName;
            var parts = s.Split('_');
            return parts[parts.Length - 1];
        }
    }
}
