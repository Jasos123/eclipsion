using System.Numerics;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Throwing;

namespace Content.Server.Abilities.Psionics;

/// <summary>
/// A focused kinetic strike that exhausts and throws one target without dealing direct lethal damage.
/// </summary>
public sealed class KineticSlamPowerSystem : EntitySystem
{
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KineticSlamActionEvent>(OnSlam);
    }

    private void OnSlam(KineticSlamActionEvent args)
    {
        if (args.Handled
            || !_psionics.OnAttemptPowerUse(args.Performer, args.Target, "kinetic slam", true))
            return;

        var source = _transform.GetMapCoordinates(args.Performer);
        var target = _transform.GetMapCoordinates(args.Target);
        if (source.MapId != target.MapId)
            return;

        var direction = target.Position - source.Position;
        if (direction.LengthSquared() > 0.01f)
        {
            direction = Vector2.Normalize(direction) * 4f;
            _throwing.TryThrow(
                args.Target,
                direction,
                baseThrowSpeed: 7f,
                user: args.Performer,
                pushbackRatio: 0f,
                recoil: false);
        }

        if (TryComp<StaminaComponent>(args.Target, out var stamina))
            _stamina.TakeStaminaDamage(args.Target, 25f, stamina, args.Performer);

        _psionics.LogPowerUsed(args.Performer, "kinetic slam", 4, 7);
        args.Handled = true;
    }
}
