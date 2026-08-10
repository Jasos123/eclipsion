using Content.Shared._Crescent.Overlays;
using Content.Shared._Crescent.SpaceBiomes;
using Robust.Shared.Network;
using Content.Server.Polymorph;
using Content.Shared._Crescent.TemporalDesync;
using Content.Server.Polymorph.Systems;
using Robust.Shared.Collections;

namespace Content.Server._Crescent.TemporalDesync;

public sealed class TemporalDesyncSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var query = EntityManager.EntityQueryEnumerator<TemporalDesyncComponent, SpaceBiomeTrackerComponent>();
        var entList = new ValueList<(EntityUid, TemporalDesyncComponent, SpaceBiomeTrackerComponent)>();
        while (query.MoveNext(out var ent, out var desync, out var biomeTracker))
        {
            entList.Add((ent, desync, biomeTracker));
        }
        foreach (var (ent, desync, biomeTracker) in entList)
        {
            var inRecurrenceExposure = biomeTracker.Biome == "default";

            // Read the multiplier straight out, rather than standing up a throwaway component per
            // entity per tick just to hold the default.
            var resistanceMultiplier = TryComp<DesyncResistanceComponent>(ent, out var resistance)
                ? resistance.ResistanceMultiplier
                : 1f;

            var desyncRate = resistanceMultiplier * 0.0002f;
            if (!inRecurrenceExposure)
                desyncRate *= -5f;

            if (EntityManager.TryGetComponent<MetaDataComponent>(ent, out var meta) && meta.EntityPrototype?.ID == "MobFleshGolemCorroded")
                desyncRate *= -10f;

            var previousLevel = desync.DesyncLevel;
            var newLevel = Math.Clamp(previousLevel + desyncRate * frameTime, 0f, 1f);
            if (newLevel != previousLevel)
            {
                desync.DesyncLevel = newLevel;
                Dirty(ent, desync);
            }

            var staticComp = EnsureComp<StaticOverlayComponent>(ent);
            if (staticComp.AdditionLevel != newLevel)
            {
                staticComp.AdditionLevel = newLevel;
                Dirty(ent, staticComp);
            }

            // Trigger only when crossing the threshold. A component clamped at one must not retry the
            // polymorph every server tick (including while its original entity is parked in nullspace).
            if (previousLevel < 1f && newLevel >= 1f && desyncRate > 0f)
                _polymorph.PolymorphEntity(ent, "TemporalDesync");
        }
    }
}
