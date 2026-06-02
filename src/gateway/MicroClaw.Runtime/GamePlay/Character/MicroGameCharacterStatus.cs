using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 角色通用生命状态。具体子类可按需细化（如门派的 AtRisk、弟子的 Dead）。
/// </summary>
public enum MicroGameCharacterStatus
{
    /// <summary>
    /// 正常存续。
    /// </summary>
    Active,
    /// <summary>
    /// 风险观察期（门派濒临淘汰、弟子濒死等）。
    /// </summary>
    AtRisk,
    /// <summary>
    /// 已退场（门派解散、弟子死亡/离派）。
    /// </summary>
    Retired,
}