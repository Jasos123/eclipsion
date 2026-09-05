using Content.Shared._Crescent.ShipShields;

namespace Content.Client._Crescent.ShipShields;

/// <summary>
///     Shared readout maths for the ship shield consoles. Every field it reads is already
///         networked on <see cref="ShipShieldEmitterComponent"/>, so the shuttle console, its
///         status strip and the targeting console can all report the same number without any
///         extra server state - and without three copies of the same arithmetic drifting apart.
/// </summary>
public static class ShipShieldReadout
{
    /// <summary>The emitter sitting on <paramref name="grid"/>, or null if that hull has none.</summary>
    public static ShipShieldEmitterComponent? Find(IEntityManager entManager, EntityUid grid)
    {
        var query = entManager.EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out _, out var emitter, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            return emitter;
        }

        return null;
    }

    /// <summary>True while the shield is collapsed - either overloaded or damaged out.</summary>
    public static bool IsDown(ShipShieldEmitterComponent shield)
    {
        return shield.OverloadAccumulator > 0 || shield.Damage >= shield.DamageLimit;
    }

    /// <summary>Seconds left on the collapse, for the recharging readouts.</summary>
    public static float DownSeconds(ShipShieldEmitterComponent shield)
    {
        return shield.OverloadAccumulator > 0
            ? shield.OverloadAccumulator
            : shield.DamageOverloadTimePunishment;
    }

    /// <summary>
    ///     Shield strength left, 0-100. A shield that is still up never reads 0: rounding a
    ///         sliver of health down to zero would say "collapsed" while the emitter is still
    ///         holding, so the last sliver reads 1%.
    /// </summary>
    public static int Percent(ShipShieldEmitterComponent shield)
    {
        if (shield.DamageLimit <= 0f)
            return 0;

        var remaining = Math.Clamp(shield.DamageLimit - shield.Damage, 0f, shield.DamageLimit);
        var percent = (int) MathF.Round(remaining / shield.DamageLimit * 100f);

        if (percent == 0 && remaining > 0f)
            percent = 1;

        return percent;
    }
}
