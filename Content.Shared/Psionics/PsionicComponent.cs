using Content.Shared.Abilities.Psionics;
using Content.Shared.DoAfter;
using Content.Shared.Psionics;
using Content.Shared.Random;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Abilities.Psionics
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class PsionicComponent : Component
    {
        [DataField]
        public bool BypassManaCheck;

        /// <summary>
        ///     Progress toward the next psionic level. Reaching <see cref="NextPowerCost"/> spends
        ///     that Potentia and awards a level plus a development point.
        ///     TODO: Psi-Potentiometry should be able to read how much Potentia a person has.
        /// </summary>
        [DataField]
        public float Potentia;

        /// <summary>
        ///     The base Potentia cost used for level progression.
        /// </summary>
        [DataField]
        public float BaselinePowerCost = 100;

        /// <summary>
        ///     Potentia required for the next level. Normal progression calculates this as
        ///     BaselinePowerCost * 2^(PsionicLevel - 1).
        /// </summary>
        [DataField]
        public float NextPowerCost = 100;

        /// <summary>
        ///     Baseline modifier added when rolling for Potentia.
        /// </summary>
        [DataField]
        public float Chance = 0.04f;

        /// <summary>
        ///     Whether this Psion has an additional Potentia roll available.
        /// </summary>
        [DataField]
        public bool CanReroll = true;

        /// <summary>
        ///     Diminishing-returns counter for Potentia earned by using powers. Each cast adds a stack and
        ///     stacks decay over time, so a Psion who spams their cheapest power on a wall earns a fraction
        ///     of what one who uses their powers when the round calls for them does.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public float CastFatigue;

        /// <summary>
        ///     When <see cref="CastFatigue"/> was last brought up to date. Decay is applied lazily on use so
        ///     progression does not need a per-tick pass over every Psion.
        /// </summary>
        [ViewVariables]
        public TimeSpan CastFatigueUpdated;

        /// <summary>
        ///     Multiplier applied to all Potentia this Psion earns from powers and from ambient glimmer.
        ///     Traits and species can push this around without touching the roll maths.
        /// </summary>
        [DataField]
        public float PotentiaGainMultiplier = 1f;

        /// <summary>
        ///     The Base amount of time (in minutes) this Psion is given the stutter effect if they become mindbroken.
        /// </summary>
        [DataField]
        public float MindbreakingStutterTime = 5;

        public string MindbreakingStutterCondition = "Stutter";

        public string MindbreakingStutterAccent = "StutteringAccent";

        /// <summary>
        ///     The message feedback given on mindbreak.
        /// </summary>
        [DataField]
        public string MindbreakingFeedback = "mindbreaking-feedback";

        /// <summary>
        /// </summary>
        [DataField]
        public string HardMindbreakingFeedback = "hard-mindbreaking-feedback";

        /// <summary>
        ///     Multiplier applied to this Psion's Potentia roll.
        /// </summary>
        [DataField]
        public float PowerRollMultiplier = 1f;

        /// <summary>
        ///     Flat bonus applied to this Psion's Potentia roll.
        /// </summary>
        [DataField]
        public float PowerRollFlatBonus = 0;

        private (float, float) _baselineAmplification = (0.4f, 1.2f);

        /// <summary>
        ///     Use this datafield to change the range of Baseline Amplification.
        /// </summary>
        [DataField]
        private (float, float) _baselineAmplificationFactors = (0.4f, 1.2f);

        /// <summary>
        ///     All Psionics automatically possess a random amount of initial starting Amplification, regardless of if they have any powers or not.
        ///     The game will crash if Robust.Random is handed a (bigger number, smaller number), so the logic here prevents any funny business.
        /// </summary>
        public (float, float) BaselineAmplification
        {
            get { return _baselineAmplification; }
            private set
            {
                _baselineAmplification = (Math.Min(
                _baselineAmplificationFactors.Item1, _baselineAmplificationFactors.Item2),
                Math.Max(_baselineAmplificationFactors.Item1, _baselineAmplificationFactors.Item2));
            }
        }
        private (float, float) _baselineDampening = (0.4f, 1.2f);

        /// <summary>
        ///     Use this datafield to change the range of Baseline Amplification.
        /// </summary>
        [DataField]
        private (float, float) _baselineDampeningFactors = (0.4f, 1.2f);

        /// <summary>
        ///     All Psionics automatically possess a random amount of initial starting Dampening, regardless of if they have any powers or not.
        ///     The game will crash if Robust.Random is handed a (bigger number, smaller number), so the logic here prevents any funny business.
        /// </summary>
        public (float, float) BaselineDampening
        {
            get { return _baselineDampening; }
            private set
            {
                _baselineDampening = (Math.Min(
                _baselineDampeningFactors.Item1, _baselineDampeningFactors.Item2),
                Math.Max(_baselineDampeningFactors.Item1, _baselineDampeningFactors.Item2));
            }
        }

        /// <summary>
        ///     Whether this entity is capable of rolling for Potentia-based progression.
        /// </summary>
        [DataField]
        public bool Roller = true;

        /// <summary>
        ///     Ifrits, revenants, etc are explicitly magical beings that shouldn't get mindbroken
        /// </summary>
        [DataField]
        public bool Removable = true;

        /// <summary>
        ///     The list of all powers currently active on a Psionic, by power Prototype.
        ///     TODO: Not in this PR due to scope, but this needs to go to Server and not Shared.
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public HashSet<PsionicPowerPrototype> ActivePowers = new();

        /// <summary>
        ///     Action entities granted by each psionic power. A power can expose multiple related
        ///     actions (for example, select/move or command/attack).
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public Dictionary<string, List<EntityUid?>> Actions = new();

        /// <summary>
        ///     What sources of Amplification does this Psion have?
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public readonly Dictionary<string, float> AmplificationSources = new();

        /// <summary>
        ///     A measure of how "Powerful" a Psion is.
        ///     TODO: Implement this in a separate PR.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
        public float CurrentAmplification;

        /// <summary>
        ///     What sources of Dampening does this Psion have?
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public readonly Dictionary<string, float> DampeningSources = new();

        /// <summary>
        ///     A measure of how "Controlled" a Psion is.
        ///     TODO: Implement this in a separate PR.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
        public float CurrentDampening;

        /// <summary>
        ///     Legacy power-capacity metadata used by psionic effects and diagnostics.
        /// </summary>
        [DataField]
        public int PowerSlots = 1;

        /// <summary>
        ///     Total slot cost of the entity's active psionic powers.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public int PowerSlotsTaken;

        /// <summary>
        ///     List of descriptors this entity will bring up for psychognomy. Used to remove
        ///     unneccesary subs for unique psionic entities like e.g. Oracle.
        /// </summary>
        [DataField]
        public List<string> PsychognomicDescriptors = new();

        /// Used for tracking what spell a Psion is actively casting
        [DataField]
        public DoAfterId? DoAfter;

        /// Popup to play if a Psion attempts to start casting a power while already casting one
        [DataField]
        public string AlreadyCasting = "already-casting";

        /// <summary>
        ///     The list of Familiars currently bound to this Psion.
        /// </summary>
        [DataField]
        public List<EntityUid> Familiars = new();

        /// <summary>
        ///     The maximum number of Familiars a Psion may bind.
        /// </summary>
        [DataField]
        public int FamiliarLimit = 1;

        /// <summary>
        ///     The list of all potential Assay messages that can be obtained from this Psion.
        /// </summary>
        [DataField]
        public List<string> AssayFeedback = new();

        /// <summary>
        ///     Legacy weighted pool retained for explicit random-power admin/debug and scripted content.
        ///     Normal player progression uses <see cref="SkillTree"/> instead.
        /// </summary>
        [DataField]
        public ProtoId<WeightedRandomPrototype> PowerPool = "RandomPsionicPowerPool";

        /// <summary>
        /// Powers currently available to the legacy random-power path.
        /// </summary>
        [DataField]
        public Dictionary<string, float> AvailablePowers = new();

        /// <summary>
        /// The tree exposed through the psionic progression action.
        /// </summary>
        [DataField]
        public ProtoId<PsionicSkillTreePrototype> SkillTree = "PsionicGeneralTree";

        /// <summary>
        /// Psionic levels award points rather than immediately rolling random powers.
        /// Level one starts with one point so a new psion can choose their first class.
        /// </summary>
        [DataField]
        public int PsionicLevel = 1;

        [DataField]
        public int SkillPoints = 1;

        /// <summary>
        /// Node IDs are recorded separately from ActivePowers because innate powers can exist
        /// outside a tree and multiple trees may expose different routes to powers.
        /// </summary>
        [DataField]
        public HashSet<string> UnlockedSkills = new();

        /// <summary>
        /// Runtime action entity used to open the tree.
        /// </summary>
        [DataField]
        public EntityUid? SkillTreeAction;
    }
}

// HULLROT EDIT
[ByRefEvent]
public sealed class PsionicPowersModifiedEvent
{
    public HashSet<PsionicPowerPrototype> Powers;

    public PsionicPowersModifiedEvent(HashSet<PsionicPowerPrototype> powers)
    {
        Powers = powers;
    }
}
