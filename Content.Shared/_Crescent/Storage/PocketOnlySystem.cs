using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._Crescent.Storage;

public sealed class PocketOnlySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PocketOnlyComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<PocketOnlyComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            args.Cancel();
    }
}
