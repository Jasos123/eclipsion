using Robust.Shared.Containers;
using Content.Shared.Destructible;
using Content.Shared.Laundry;
using Content.Shared.Storage;

namespace Content.Server.Laundry;

// I just wanted the sprite to change states when it broke.

public sealed class LaundrySystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WashingMachineComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WashingMachineComponent, BreakageEventArgs>(OnBreak);
        SubscribeLocalEvent<WashingMachineComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<WashingMachineComponent, EntRemovedFromContainerMessage>(OnContainerModified);

    }

    private void OnMapInit(EntityUid uid, WashingMachineComponent component, MapInitEvent args)
    {
        if (!_containerSystem.TryGetContainer(uid, "storagebase", out var container))
            return;

        _appearanceSystem.SetData(uid, StorageVisuals.HasContents, container.ContainedEntities.Count > 0);
    }

    private void OnBreak(EntityUid uid, WashingMachineComponent component, BreakageEventArgs args)
    {
        _appearanceSystem.SetData(uid, WashingMachineVisualState.Broken, true);
    }

    private void OnContainerModified(EntityUid uid, WashingMachineComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID == "storagebase")
            _appearanceSystem.SetData(uid, StorageVisuals.HasContents, args.Container.ContainedEntities.Count > 0);
    }
}

