namespace Content.Shared.Interaction.Events;

/// <summary>
/// Raised directed on the target entity when being attacked.
/// </summary>
/// <param name="User">
/// Eclipsion - who is swinging. A target that is only off-limits to *some* attackers cannot answer without it,
/// and the attacker-side <see cref="AttackAttemptEvent"/> is no help when the attacker is an ordinary player
/// carrying no component to hang a subscription on.
/// </param>
[ByRefEvent]
public record struct GettingAttackedAttemptEvent(EntityUid User, bool Cancelled = false);
