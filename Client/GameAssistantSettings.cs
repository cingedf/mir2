using System;

namespace Client
{
    /// <summary>
    /// 内挂助手设置（内存占位变量）。
    /// 勾选状态仅存内存，供二期功能实现时读取；不持久化、不影响任何游戏逻辑。
    /// </summary>
    public static class GameAssistantSettings
    {
        /// <summary>内挂功能开关状态（与 AssistantDialog.AssistantNames 一一对应）。</summary>
        public static readonly bool[] AssistantOptions = new bool[3];

        /// <summary>内挂功能名称（占位三项，二期按需扩展）。</summary>
        public static readonly string[] AssistantNames = { "自动喝药保护", "范围自动捡物", "怪物血条增强" };
    }
}
