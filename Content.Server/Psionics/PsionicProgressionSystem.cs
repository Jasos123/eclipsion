using Content.Shared.Abilities.Psionics;
using Content.Shared.Bed.Sleep;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Systems;
using Content.Shared.Psionics;
using Content.Shared.Psionics.Glimmer;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server.Psionics;

/// <summary>
/// Owns the in-round sources of Potentia.
///
/// Before this existed the only way to earn a psionic level was a single one-shot chemical reroll, which
/// left every skill-tree node above <c>minimumLevel: 1</c> unreachable in normal play. Progression now comes
/// from two places that pull in opposite directions:
///
/// <list type="bullet">
/// <item>Using powers, with diminishing returns, so the Psion who actually plays their role advances.</item>
/// <item>Ambient glimmer, so a station whose Psions are pushing the noosphere advances all of them - and
/// pays for it with the glimmer events that come with a loud noosphere.</item>
/// </list>
/// </summary>
public sealed class PsionicProgressionSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GlimmerSystem _glimmer = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PsionicsSystem _psionics = default!;

    private TimeSpan _nextDrip;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsionicComponent, PsionicPowerCastEvent>(OnPowerCast);
    }

    /// <summary>
    ///     Potentia from using powers. The award is the glimmer the cast produced, so the heavier powers are
    ///     worth more, divided by a fatigue counter that punishes casting the same cheap power on a loop.
    /// </summary>
    private void OnPowerCast(Entity<PsionicComponent> ent, ref PsionicPowerCastEvent args)
    {
        var perGlimmer = _cfg.GetCVar(CCVars.PsionicPotentiaPerGlimmer);
        if (perGlimmer <= 0 || args.Glimmer <= 0 || !ent.Comp.Roller)
            return;

        var fatigue = DecayFatigue(ent.Comp);
        var amount = args.Glimmer * perGlimmer * ent.Comp.PotentiaGainMultiplier / (1 + fatigue);

        ent.Comp.CastFatigue = Math.Min(fatigue + 1, _cfg.GetCVar(CCVars.PsionicCastFatigueMax));

        _psionics.AddPotentia(ent.Owner, ent.Comp, amount);
    }

    /// <summary>
    ///     Brings a Psion's cast fatigue up to date. Decay is lazy rather than ticked so the common case -
    ///     a handful of Psions who cast every few minutes - costs nothing between casts.
    /// </summary>
    private float DecayFatigue(PsionicComponent component)
    {
        var now = _timing.CurTime;

        // Round restarts rewind CurTime, which would otherwise leave a Psion carrying fatigue from a
        // timestamp in the future forever.
        if (component.CastFatigueUpdated > now)
        {
            component.CastFatigue = 0;
            component.CastFatigueUpdated = now;
            return 0;
        }

        var elapsed = (float) (now - component.CastFatigueUpdated).TotalSeconds;
        component.CastFatigue = Math.Max(0, component.CastFatigue - elapsed * _cfg.GetCVar(CCVars.PsionicCastFatigueDecay));
        component.CastFatigueUpdated = now;

        return component.CastFatigue;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextDrip)
            return;

        var interval = Math.Max(1f, _cfg.GetCVar(CCVars.PsionicGlimmerDripInterval));
        _nextDrip = _timing.CurTime + TimeSpan.FromSeconds(interval);

        var perMinute = _cfg.GetCVar(CCVars.PsionicGlimmerDripPerMinute);
        if (perMinute <= 0 || !_glimmer.GetGlimmerEnabled())
            return;

        // Scales with how far the noosphere sits above equilibrium: ~0 when glimmer is quiet, 1x at ~502,
        // and close to 2x when it is screaming.
        var drip = perMinute * (float) _glimmer.GetGlimmerEquilibriumRatio() * interval / 60f;
        if (drip <= 0)
            return;

        var query = EntityQueryEnumerator<PsionicComponent>();
        while (query.MoveNext(out var uid, out var psionic))
        {
            if (!psionic.Roller
                || !_mobState.IsAlive(uid)
                || HasComp<SleepingComponent>(uid)
                || HasComp<MindbrokenComponent>(uid))
                continue;

            // Insulation cuts you off from the noosphere, so it cuts you off from what the noosphere feeds you.
            if (TryComp<PsionicInsulationComponent>(uid, out var insulation) && !insulation.Passthrough)
                continue;

            _psionics.AddPotentia(uid, psionic, drip * psionic.PotentiaGainMultiplier);
        }
    }
}
