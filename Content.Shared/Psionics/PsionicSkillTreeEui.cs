using Content.Shared.Actions;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Psionics;

public sealed partial class OpenPsionicSkillTreeActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public enum PsionicSkillAvailability : byte
{
    Locked,
    Available,
    Owned,
    Excluded,
    InsufficientLevel,
    InsufficientPoints,
}

[Serializable, NetSerializable]
public sealed class PsionicSkillNodeState
{
    public string SkillId { get; }
    public PsionicSkillAvailability Availability { get; }
    public string? Reason { get; }

    public PsionicSkillNodeState(string skillId, PsionicSkillAvailability availability, string? reason = null)
    {
        SkillId = skillId;
        Availability = availability;
        Reason = reason;
    }
}

[Serializable, NetSerializable]
public sealed class PsionicSkillTreeEuiState : EuiStateBase
{
    public string TreeId { get; }
    public int Level { get; }
    public int SkillPoints { get; }
    public float Potentia { get; }
    public float NextLevelCost { get; }
    public List<PsionicSkillNodeState> Skills { get; }

    public PsionicSkillTreeEuiState(
        string treeId,
        int level,
        int skillPoints,
        float potentia,
        float nextLevelCost,
        List<PsionicSkillNodeState> skills)
    {
        TreeId = treeId;
        Level = level;
        SkillPoints = skillPoints;
        Potentia = potentia;
        NextLevelCost = nextLevelCost;
        Skills = skills;
    }
}

[Serializable, NetSerializable]
public sealed class UnlockPsionicSkillMessage : EuiMessageBase
{
    public string SkillId { get; }

    public UnlockPsionicSkillMessage(string skillId)
    {
        SkillId = skillId;
    }
}
