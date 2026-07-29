using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;

namespace Content.Server._RMC14.Atmos;

[RegisterComponent, Access(typeof(RMCFirePropSystem))]
public sealed partial class RMCFirePropComponent : Component;

/// <summary>
/// Makes the legacy RMC animated fire prop participate in the normal extinguisher flow.
/// </summary>
public sealed class RMCFirePropSystem : EntitySystem
{
    [Dependency] private readonly FlammableSystem _flammable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RMCFirePropComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<RMCFirePropComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<FlammableComponent>(ent, out var flammable))
            return;

        _flammable.SetFireStacks(ent, flammable.MaximumFireStacks, flammable);
        _flammable.Ignite(ent, ent, flammable);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RMCFirePropComponent, FlammableComponent>();
        while (query.MoveNext(out var uid, out _, out var flammable))
        {
            if (!flammable.OnFire)
                QueueDel(uid);
        }
    }
}
