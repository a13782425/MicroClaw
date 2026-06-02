using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 游戏世界状态枚举，表示当前游戏世界所处的不同阶段或状态。
/// </summary>
public enum MicroGameWorldStatus
{
    /// <summary>
    /// 活跃运转中。
    /// </summary>
    Active,
    /// <summary>
    /// 暂停（主公离线挂起等）。
    /// </summary>
    Paused,
    /// <summary>
    /// 归档，只读，不再推进节律。
    /// </summary>
    Archived,
}
