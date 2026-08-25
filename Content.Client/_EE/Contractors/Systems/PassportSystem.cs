using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared._EE.Contractors.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._EE.Contractors.Systems;

public sealed class PassportSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const int CoverLayer = 0;
    private const int PortraitLayer = 1;
    private const string PortraitPrefix = "passport_species_";
    private const string FallbackPortrait = PortraitPrefix + "human";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PassportComponent, SharedPassportSystem.PassportToggleEvent>(OnPassportToggled);
        SubscribeLocalEvent<PassportComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnStartup(Entity<PassportComponent> passport, ref ComponentStartup args) =>
        UpdateSprite(passport);

    private void OnAfterState(Entity<PassportComponent> passport, ref AfterAutoHandleStateEvent args) =>
        UpdateSprite(passport);

    private void OnPassportToggled(Entity<PassportComponent> passport, ref SharedPassportSystem.PassportToggleEvent evt) =>
        UpdateSprite(passport);

    /// <summary>
    /// Rebuilds both layers from the component outright. The old code mutated the current state
    /// name in place - swapping "open" for "closed" and back - which only stayed correct as long
    /// as every update arrived exactly once and in order; a prediction reset landing between a
    /// toggle and its acknowledgement left the sprite describing a state the document was not in.
    /// Deriving the names from the component instead makes every path idempotent.
    /// </summary>
    private void UpdateSprite(Entity<PassportComponent> passport)
    {
        if (!TryComp<SpriteComponent>(passport, out var sprite))
            return;

        var ent = new Entity<SpriteComponent?>(passport.Owner, sprite);

        if (_sprite.LayerExists(ent, CoverLayer)
            && _prototype.TryIndex(passport.Comp.Cover, out var cover))
        {
            var coverState = cover.State + (passport.Comp.IsClosed ? "_closed" : "_open");
            if (HasState(sprite, coverState))
                _sprite.LayerSetRsiState(ent, CoverLayer, coverState);
        }

        if (!_sprite.LayerExists(ent, PortraitLayer))
            return;

        // A closed document shows no portrait, and species without artwork borrow the human one
        // rather than asking the RSI for a state that is not there.
        _sprite.LayerSetVisible(ent, PortraitLayer, !passport.Comp.IsClosed);

        var portrait = PortraitPrefix + passport.Comp.PortraitSpecies.ToLowerInvariant();
        _sprite.LayerSetRsiState(ent, PortraitLayer, HasState(sprite, portrait) ? portrait : FallbackPortrait);
    }

    private static bool HasState(SpriteComponent sprite, string state) =>
        sprite.BaseRSI?.TryGetState(state, out _) == true;
}
