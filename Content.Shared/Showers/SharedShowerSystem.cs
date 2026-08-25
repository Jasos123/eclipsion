using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared.Showers
{
    public abstract class SharedShowerSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ShowerComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<ShowerComponent, ComponentShutdown>(OnShutdown);
            SubscribeLocalEvent<ShowerComponent, GetVerbsEvent<AlternativeVerb>>(OnToggleShowerVerb);
            SubscribeLocalEvent<ShowerComponent, ActivateInWorldEvent>(OnActivateInWorld);
        }

        private void OnShutdown(EntityUid uid, ShowerComponent component, ComponentShutdown args)
        {
            StopLoop(component);
        }
        private void OnMapInit(EntityUid uid, ShowerComponent component, MapInitEvent args)
        {
            if (_random.Prob(0.5f))
                component.ToggleShower = true;
            UpdateAppearance(uid);
        }
        private void OnToggleShowerVerb(EntityUid uid, ShowerComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanInteract || !args.CanAccess || args.Hands == null)
                return;

            AlternativeVerb toggleVerb = new()
            {
                Act = () => ToggleShowerHead(uid, args.User, component)
            };

            if (component.ToggleShower)
            {
                toggleVerb.Text = Loc.GetString("shower-turn-on");
                toggleVerb.Icon =
                    new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/open.svg.192dpi.png"));
            }
            else
            {
                toggleVerb.Text = Loc.GetString("shower-turn-off");
                toggleVerb.Icon =
                    new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/close.svg.192dpi.png"));
            }
            args.Verbs.Add(toggleVerb);
        }
        private void OnActivateInWorld(EntityUid uid, ShowerComponent comp, ActivateInWorldEvent args)
        {
            if (args.Handled)
                return;

            args.Handled = true;
            ToggleShowerHead(uid, args.User, comp);

            _audio.PlayPvs(comp.EnableShowerSound, uid);
        }
        public void ToggleShowerHead(EntityUid uid, EntityUid? user = null, ShowerComponent? component = null, MetaDataComponent? meta = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.ToggleShower = !component.ToggleShower;

            UpdateAppearance(uid, component);
        }
        private void UpdateAppearance(EntityUid uid, ShowerComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            _appearance.SetData(uid, ShowerVisuals.ShowerVisualState, component.ToggleShower ? ShowerVisualState.On : ShowerVisualState.Off);

            if (component.ToggleShower)
            {
                // The stream can disappear independently during map/PVS cleanup. Treat a stale UID
                // as stopped so the shower can create a valid source the next time it is refreshed.
                if (!Exists(component.PlayingStream))
                {
                    component.PlayingStream = null;
                    var audio = _audio.PlayPvs(component.LoopingSound, uid, AudioParams.Default.WithLoop(true).WithMaxDistance(5));

                    if (audio == null)
                        return;

                    component.PlayingStream = audio!.Value.Entity;
                }
            }
            else
            {
                StopLoop(component);
            }
        }

        private void StopLoop(ShowerComponent component)
        {
            component.PlayingStream = _audio.Stop(component.PlayingStream);
        }
    }
}
