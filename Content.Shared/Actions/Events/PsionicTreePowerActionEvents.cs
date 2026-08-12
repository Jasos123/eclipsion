namespace Content.Shared.Actions.Events;

public sealed partial class PsionicFlameBreathActionEvent : WorldTargetActionEvent;

public sealed partial class SelectTelekineticObjectActionEvent : EntityTargetActionEvent;

public sealed partial class MoveTelekineticObjectActionEvent : WorldTargetActionEvent;

public sealed partial class CommandPsionicFamiliarMoveActionEvent : WorldTargetActionEvent;

public sealed partial class CommandPsionicFamiliarAttackActionEvent : EntityTargetActionEvent;

public sealed partial class PsionicSelfShieldActionEvent : InstantActionEvent;

public sealed partial class PsionicAllyShieldActionEvent : EntityTargetActionEvent;

public sealed partial class KineticSlamActionEvent : EntityTargetActionEvent;
