using System.Collections.Generic;

namespace Client
{
    /// <summary>
    /// 内挂（游戏助手）功能设置——F12 面板勾选状态的内存存放处。
    /// 当前为占位：勾选不触发任何功能逻辑，二期实现各功能时读取对应开关。
    /// </summary>
    public static class GameAssistantSettings
    {
        /// <summary>内挂功能开关（索引与 OptionDialog.AssistantOptions 一一对应）</summary>
        public static readonly bool[] AssistantOptions = new bool[3];

        /// <summary>开关中文名（与 OptionDialog 内挂区块顺序一致）</summary>
        public static readonly string[] AssistantNames =
        {
            "自动喝药保护",
            "范围自动捡物",
            "怪物血条增强",
        };
    }
}
