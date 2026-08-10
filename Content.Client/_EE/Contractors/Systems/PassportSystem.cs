using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Timing;
using Robust.Shared.Timing;


namespace Content.Client._EE.Contractors.Systems;

public sealed class PassportSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IClientGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, SharedPassportSystem.PassportToggleEvent>(OnPassportToggled);
        SubscribeLocalEvent<PassportComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnAfterState(Entity<PassportComponent> passport, ref AfterAutoHandleStateEvent args)
    {
        if (!_entityManager.TryGetComponent<SpriteComponent>(passport, out var sprite))
            return;

        UpdateOpenState(passport.Comp, sprite);

        var currentState = sprite.LayerGetState(1);
        if (currentState.Name == null)
            return;

        const string portraitPrefix = "passport_species_";
        if (!currentState.Name.StartsWith(portraitPrefix, StringComparison.Ordinal))
            return;

        sprite.LayerSetState(1, portraitPrefix + passport.Comp.PortraitSpecies.ToLowerInvariant());
    }

    private void OnPassportToggled(Entity<PassportComponent> passport, ref SharedPassportSystem.PassportToggleEvent evt)
    {
        if (!_timing.IsFirstTimePredicted || evt.Handled || !_entityManager.TryGetComponent<SpriteComponent>(passport, out var sprite))
            return;

        evt.Handled = true;
        UpdateOpenState(passport.Comp, sprite);
    }

    private static void UpdateOpenState(PassportComponent passport, SpriteComponent sprite)
    {
        sprite.LayerSetVisible(1, !passport.IsClosed);

        var currentState = sprite.LayerGetState(0);
        if (currentState.Name == null)
            return;

        var oldState = passport.IsClosed ? "open" : "closed";
        var newState = passport.IsClosed ? "closed" : "open";
        var newStateName = currentState.Name.Replace(oldState, newState, StringComparison.Ordinal);

        sprite.LayerSetState(0, newStateName);
    }
}
