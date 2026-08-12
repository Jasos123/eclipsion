using System.Linq;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Customization.Systems;

public sealed partial class FactionRequirement : CharacterRequirement
{
    [DataField("factionID")] public string FactionID = "";

    public override bool IsValid(
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        out string? reason,
        int depth = 0,
        MindComponent? mind = null
    )
    {
        // The reason is always populated: CharacterRequirementsSystem applies Inverted itself, so
        // returning null here left inverted requirements failing with no explanation in the UI.
        reason = Inverted
            ? $"Your faction must NOT be {FactionID}."
            : $"Your faction must be {FactionID}.";

        return profile.Faction == FactionID;
    }

}

[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class WealthRequirement : CharacterRequirement
{
    [DataField("below")] public int Below = int.MaxValue;
    [DataField("above")] public int Above = 0;

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        out string? reason,
        int depth = 0,
        MindComponent? mind = null)
    {
        if (profile.BankBalance > Above || profile.BankBalance < Below)
        {
            reason = $"Your bank balance must be between {Below} and {Above} !";
            return false;
        }

        reason = null;
        return true;
    }
}
