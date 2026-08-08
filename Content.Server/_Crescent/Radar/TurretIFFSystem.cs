using Content.Shared.Crescent.Radar;
using Content.Server.Shuttles.Systems;

namespace Content.Server.Crescent.Radar;

public sealed partial class TurretIFFSystem : SharedTurretIFFSystem
{
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private readonly RadarConsoleSystem _radarConsole = default!;

    // Refreshing is expensive, so batch any turret changes that occur in the same tick.
    private bool _turretsDirty;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TurretIFFComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TurretIFFComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, TurretIFFComponent component, ComponentStartup args)
    {
        if (EntityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage < EntityLifeStage.Initialized)
        {
            return;
        }

        _turretsDirty = true;
    }

    private void OnShutdown(EntityUid uid, TurretIFFComponent component, ComponentShutdown args)
    {
        if (EntityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage > EntityLifeStage.MapInitialized)
        {
            return;
        }

        _turretsDirty = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_turretsDirty)
            return;

        _turretsDirty = false;

        _shuttleConsole.RefreshIFFState();
        _radarConsole.RefreshIFFState();
    }
}
