using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Psionics;

/// <summary>
/// A branch used to group and color psionic skills in the skill-tree UI.
/// </summary>
[Prototype("psionicSkillBranch")]
public sealed partial class PsionicSkillBranchPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    [DataField(required: true)]
    public LocId Description = string.Empty;

    [DataField(required: true)]
    public Color Color = Color.White;
}

/// <summary>
/// A purchasable node in a psionic skill tree. The actual gameplay effect is supplied by an
/// existing <see cref="PsionicPowerPrototype"/> so the tree stays data-driven.
/// </summary>
[Prototype("psionicSkill")]
public sealed partial class PsionicSkillPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    [DataField(required: true)]
    public LocId Description = string.Empty;

    [DataField(required: true)]
    public SpriteSpecifier Icon = default!;

    [DataField(required: true)]
    public ProtoId<PsionicSkillBranchPrototype> Branch;

    [DataField(required: true)]
    public ProtoId<PsionicPowerPrototype> Power;

    /// <summary>
    /// Controls the vertical order of nodes inside a branch.
    /// </summary>
    [DataField]
    public int Tier;

    [DataField]
    public int Cost = 1;

    [DataField]
    public int MinimumLevel = 1;

    [DataField]
    public List<ProtoId<PsionicSkillPrototype>> Prerequisites = new();

    /// <summary>
    /// Only one purchased node with a given non-empty group may exist. This is used for class
    /// roots and can also be used for mutually-exclusive upgrades inside a class.
    /// </summary>
    [DataField]
    public string? ExclusiveGroup;
}

/// <summary>
/// Selects which nodes are shown to a psion. Specialist caster traits use smaller trees while
/// the generic latent psychic tree exposes all class roots.
/// </summary>
[Prototype("psionicSkillTree")]
public sealed partial class PsionicSkillTreePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    [DataField]
    public List<ProtoId<PsionicSkillBranchPrototype>> Branches = new();

    [DataField]
    public List<ProtoId<PsionicSkillPrototype>> Skills = new();
}
