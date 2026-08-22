using Content.Shared.Eye;
using Content.Shared.Voidborn;
using Robust.Server.GameObjects;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using System.Linq;
using Content.Shared.Abilities.Psionics;
using Robust.Shared.Random;
using Content.Server.Light.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Damage.Systems;
using Content.Server.Flash;
using Content.Shared.Stunnable;
using Robust.Shared.Timing;


namespace Content.Server.Voidborn;

public sealed class EtherealSystem : SharedEtherealSystem
{
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly EyeSystem _eye = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StaminaSystem _staminaSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EtherealComponent, FlashAttemptEvent>(OnFlashed);
        SubscribeLocalEvent<EtherealComponent, StunnedEvent>(OnStunned);
    }

    private void OnFlashed(EntityUid uid, EtherealComponent comp, FlashAttemptEvent args)
    {
        _staminaSystem.TakeStaminaDamage(uid, comp.StaminaDamageOnFlash);
        RemComp(uid, comp);
    }

    private void OnStunned(EntityUid uid, EtherealComponent component, StunnedEvent args) =>
        RemComp(uid, component);

    /// <summary>
    /// Drops an entity out of the shadow state with the same flash and shadow the power itself uses,
    /// so a phase that runs out of time looks like a phase that was ended on purpose.
    /// </summary>
    public void EndEthereal(EntityUid uid, EtherealComponent component)
    {
        SpawnAtPosition("VoidbornShadow", Transform(uid).Coordinates);
        SpawnAtPosition("EffectFlashVoidbornDarkSwapOff", Transform(uid).Coordinates);
        RemComp(uid, component);
    }

    public override void OnStartup(EntityUid uid, EtherealComponent component, MapInitEvent args)
    {
        base.OnStartup(uid, component, args);

        component.ExpiresAt = _timing.CurTime + component.Duration;

        // Eclipsion - the shadow state carries no visibility layer, so a DarkSwapped psion is a shimmer everyone
        // can spot if they look rather than an entity the client never hears about. PVS only sends an entity when
        // the viewer's mask holds every bit the entity carries, and the engine forces bit 1 on regardless, so
        // adding the Ethereal layer alone was enough to hide it from everybody without the ShowEthereal bit -
        // dropping the Normal layer next to it never mattered. Hiding is StealthComponent's job below.
        if (TryComp<EyeComponent>(uid, out var eye))
            _eye.SetVisibilityMask(uid, eye.VisibilityMask | (int) (VisibilityFlags.Ethereal), eye);

        var stealth = EnsureComp<StealthComponent>(uid);
        _stealth.SetVisibility(uid, SharedPsionicAbilitiesSystem.ConcealmentVisibility, stealth); // Eclipsion
        _stealth.SetColorTint(uid, false, stealth); // Eclipsion - a shimmer, not a blue character.

        SuppressFactions(uid, component, true);

        if (HasComp<MindbrokenComponent>(uid))
            RemComp(uid, component);
    }

    public override void OnShutdown(EntityUid uid, EtherealComponent component, ComponentShutdown args)
    {
        base.OnShutdown(uid, component, args);

        // Eclipsion - nothing to undo on the visibility layer; the shadow state never puts one on.
        if (TryComp<EyeComponent>(uid, out var eye))
            _eye.SetVisibilityMask(uid, (int) VisibilityFlags.Normal, eye);

        SuppressFactions(uid, component, false);

        RemComp<StealthComponent>(uid);

        foreach (var light in component.DarkenedLights.ToArray())
        {
            if (!TryComp<PointLightComponent>(light, out var pointLight)
                || !TryComp<EtherealLightComponent>(light, out var etherealLight))
                continue;

            ResetLight(light, pointLight, etherealLight);
        }
    }

    public void SuppressFactions(EntityUid uid, EtherealComponent component, bool set)
    {
        if (set)
        {
            if (!TryComp<NpcFactionMemberComponent>(uid, out var factions))
                return;

            component.SuppressedFactions = factions.Factions.ToList();

            foreach (var faction in factions.Factions)
                _factions.RemoveFaction(uid, faction);
        }
        else
        {
            foreach (var faction in component.SuppressedFactions)
                _factions.AddFaction(uid, faction);

            component.SuppressedFactions.Clear();
        }
    }

    public void ResetLight(EntityUid uid, PointLightComponent light, EtherealLightComponent etherealLight)
    {
        etherealLight.AttachedEntity = EntityUid.Invalid;

        if (etherealLight.OldRadiusEdited)
            _light.SetRadius(uid, etherealLight.OldRadius);
        etherealLight.OldRadiusEdited = false;

        if (etherealLight.OldEnergyEdited)
            _light.SetEnergy(uid, etherealLight.OldEnergy);
        etherealLight.OldEnergyEdited = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<EtherealComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            // A round restart rewinds CurTime, which would otherwise strand an expiry in the future.
            if (component.ExpiresAt > now + component.Duration)
                component.ExpiresAt = now;

            if (now >= component.ExpiresAt)
            {
                EndEthereal(uid, component);
                continue;
            }

            if (!component.Darken)
                continue;

            component.DarkenAccumulator += frameTime;

            if (component.DarkenAccumulator <= 1)
                continue;

            component.DarkenAccumulator -= component.DarkenRate;
            _staminaSystem.TakeStaminaDamage(uid, component.StaminaPerSecond * component.DarkenRate);

            var darkened = new List<EntityUid>();
            var lightQuery = _lookup.GetEntitiesInRange(uid, component.DarkenRange, flags: LookupFlags.StaticSundries)
                .Where(x => HasComp<EtherealLightComponent>(x) && HasComp<PointLightComponent>(x));

            foreach (var entity in lightQuery)
                if (!darkened.Contains(entity))
                    darkened.Add(entity);

            _random.Shuffle(darkened);
            component.DarkenedLights = darkened;

            var playerPos = _transform.GetWorldPosition(uid);

            foreach (var light in component.DarkenedLights.ToArray())
            {
                var lightPos = _transform.GetWorldPosition(light);
                if (!TryComp<PointLightComponent>(light, out var pointLight)
                    || !TryComp<EtherealLightComponent>(light, out var etherealLight))
                    continue;

                if (TryComp<PoweredLightComponent>(light, out var powered) && !powered.On)
                {
                    ResetLight(light, pointLight, etherealLight);
                    continue;
                }

                if (etherealLight.AttachedEntity == EntityUid.Invalid)
                    etherealLight.AttachedEntity = uid;

                if (etherealLight.AttachedEntity != EntityUid.Invalid
                && etherealLight.AttachedEntity != uid)
                {
                    component.DarkenedLights.Remove(light);
                    continue;
                }

                if (etherealLight.AttachedEntity == uid
                    && _random.Prob(0.03f))
                    etherealLight.AttachedEntity = EntityUid.Invalid;

                if (!etherealLight.OldRadiusEdited)
                {
                    etherealLight.OldRadius = pointLight.Radius;
                    etherealLight.OldRadiusEdited = true;
                }
                if (!etherealLight.OldEnergyEdited)
                {
                    etherealLight.OldEnergy = pointLight.Energy;
                    etherealLight.OldEnergyEdited = true;
                }

                var distance = (lightPos - playerPos).Length();
                var radius = distance * 2f;
                var energy = distance * 0.8f;

                if (etherealLight.OldRadiusEdited && radius > etherealLight.OldRadius)
                    radius = etherealLight.OldRadius;
                if (etherealLight.OldRadiusEdited && radius < etherealLight.OldRadius * 0.20f)
                    radius = etherealLight.OldRadius * 0.20f;

                if (etherealLight.OldEnergyEdited && energy > etherealLight.OldEnergy)
                    energy = etherealLight.OldEnergy;
                if (etherealLight.OldEnergyEdited && energy < etherealLight.OldEnergy * 0.20f)
                    energy = etherealLight.OldEnergy * 0.20f;

                _light.SetRadius(light, radius);
                _light.SetEnergy(light, energy);
            }
        }
    }
}
