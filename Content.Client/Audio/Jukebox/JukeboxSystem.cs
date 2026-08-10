using Content.Shared.Audio.Jukebox;
using Content.Shared.CCVar;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayer = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>
    /// The listener's boombox volume slider, converted from gain to dB so it can be added on top
    /// of the jukebox's own base volume.
    /// </summary>
    private float _volumeSlider;

    // Audio streams are network entities owned by the server. Track only the streams belonging to
    // jukeboxes so a local slider can be re-applied when either side receives a relevant state update,
    // without scanning every jukebox every frame.
    private readonly Dictionary<EntityUid, (EntityUid Jukebox, float BaseVolume)> _jukeboxStreams = new();
    private readonly Dictionary<EntityUid, EntityUid> _ownerStreams = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<JukeboxComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<JukeboxComponent, AfterAutoHandleStateEvent>(OnJukeboxAfterState);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnJukeboxShutdown);
        SubscribeLocalEvent<AudioComponent, AfterAutoHandleStateEvent>(OnAudioAfterState);
        SubscribeLocalEvent<AudioComponent, ComponentShutdown>(OnAudioShutdown);

        Subs.CVar(_cfg, CCVars.BoomboxVolume, OnVolumeCVarChanged, true);

        _protoManager.PrototypesReloaded += OnProtoReload;
    }

    private void OnVolumeCVarChanged(float gain)
    {
        _volumeSlider = SharedAudioSystem.GainToVolume(gain);

        foreach (var (stream, data) in _jukeboxStreams)
        {
            if (!TryComp(stream, out AudioComponent? audio))
                continue;

            ApplyListenerVolume(stream, data.BaseVolume, audio);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _protoManager.PrototypesReloaded -= OnProtoReload;
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<JukeboxPrototype>())
            return;

        var query = AllEntityQuery<JukeboxComponent, UserInterfaceComponent>();

        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>((uid, ui), JukeboxUiKey.Key, out var bui))
                continue;

            bui.PopulateMusic();
        }
    }

    private void OnJukeboxAfterState(Entity<JukeboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        TrackStream(ent);

        if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>(ent.Owner, JukeboxUiKey.Key, out var bui))
            return;

        bui.Reload();
    }

    private void TrackStream(Entity<JukeboxComponent> ent)
    {
        if (_ownerStreams.Remove(ent.Owner, out var previous))
            _jukeboxStreams.Remove(previous);

        if (ent.Comp.AudioStream is not { } stream)
            return;

        _ownerStreams[ent.Owner] = stream;
        _jukeboxStreams[stream] = (ent.Owner, ent.Comp.Volume);

        if (TryComp(stream, out AudioComponent? audio))
            ApplyListenerVolume(stream, ent.Comp.Volume, audio);
    }

    private void OnAudioAfterState(Entity<AudioComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_jukeboxStreams.TryGetValue(ent.Owner, out var data))
            ApplyListenerVolume(ent.Owner, data.BaseVolume, ent.Comp);
    }

    private void OnJukeboxShutdown(Entity<JukeboxComponent> ent, ref ComponentShutdown args)
    {
        if (_ownerStreams.Remove(ent.Owner, out var stream))
            _jukeboxStreams.Remove(stream);
    }

    private void OnAudioShutdown(Entity<AudioComponent> ent, ref ComponentShutdown args)
    {
        if (!_jukeboxStreams.Remove(ent.Owner, out var data))
            return;

        if (_ownerStreams.TryGetValue(data.Jukebox, out var stream) && stream == ent.Owner)
            _ownerStreams.Remove(data.Jukebox);
    }

    private void ApplyListenerVolume(EntityUid stream, float baseVolume, AudioComponent audio)
    {
        var target = baseVolume + _volumeSlider;

        if (audio.Params.Volume == target)
            return;

        Audio.SetVolume(stream, target, audio);
    }

    private void OnAnimationCompleted(EntityUid uid, JukeboxComponent component, AnimationCompletedEvent args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (!TryComp<AppearanceComponent>(uid, out var appearance) ||
            !_appearanceSystem.TryGetData<JukeboxVisualState>(uid, JukeboxVisuals.VisualState, out var visualState, appearance))
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance(uid, visualState, component, sprite);
    }

    private void OnAppearanceChange(EntityUid uid, JukeboxComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.AppearanceData.TryGetValue(JukeboxVisuals.VisualState, out var visualStateObject) ||
            visualStateObject is not JukeboxVisualState visualState)
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance(uid, visualState, component, args.Sprite);
    }

    private void UpdateAppearance(EntityUid uid, JukeboxVisualState visualState, JukeboxComponent component, SpriteComponent sprite)
    {
        SetLayerState(JukeboxVisualLayers.Base, component.OffState, sprite);

        switch (visualState)
        {
            case JukeboxVisualState.On:
                SetLayerState(JukeboxVisualLayers.Base, component.OnState, sprite);
                break;

            case JukeboxVisualState.Off:
                SetLayerState(JukeboxVisualLayers.Base, component.OffState, sprite);
                break;

            case JukeboxVisualState.Select:
                PlayAnimation(uid, JukeboxVisualLayers.Base, component.SelectState, 1.0f, sprite);
                break;
        }
    }

    private void PlayAnimation(EntityUid uid, JukeboxVisualLayers layer, string? state, float animationTime, SpriteComponent sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        if (!_animationPlayer.HasRunningAnimation(uid, state))
        {
            var animation = GetAnimation(layer, state, animationTime);
            sprite.LayerSetVisible(layer, true);
            _animationPlayer.Play(uid, animation, state);
        }
    }

    private static Animation GetAnimation(JukeboxVisualLayers layer, string state, float animationTime)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(animationTime),
            AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = layer,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(state, 0f)
                        }
                    }
                }
        };
    }

    private void SetLayerState(JukeboxVisualLayers layer, string? state, SpriteComponent sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        sprite.LayerSetVisible(layer, true);
        sprite.LayerSetAutoAnimated(layer, true);
        sprite.LayerSetState(layer, state);
    }
}
