using Content.Shared._Crescent.Overwatch;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using System.Numerics;

namespace Content.Client._Crescent.Overwatch;

/// <summary>
/// Client system that relays ambient sounds to the local player while spectating via the Overwatch camera.
/// </summary>
public sealed class OverwatchConsoleSystem : EntitySystem
{
    // Performance guard: sounds beyond this range are ignored entirely.
    private const float MaxSoundRelayDistance = 50f;

    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // Maps original sound entities to their relayed client-side counterparts.
    private readonly Dictionary<EntityUid, EntityUid> _relayedSounds = new();

    // Scratch list built each frame before spawning relayed audio entities.
    private readonly List<(EntityUid Uid, AudioComponent Audio, RatOverwatchRelayedSoundComponent? Relay, EntityCoordinates Position)> _toRelay = new();

    // Reusable buffers, so the per-frame spectating path allocates nothing.
    private readonly HashSet<EntityUid> _activeSounds = new();
    private readonly List<EntityUid> _staleScratch = new();

    /// <summary>
    /// Whether any source has ever been tagged with <see cref="RatOverwatchRelayedSoundComponent"/>
    /// since the last full cleanup. Lets the idle path skip the entity sweep entirely.
    /// </summary>
    private bool _anyRelayTagged;

    private OverwatchAnnouncementOverlay _announcementOverlay = default!;

    /// <summary>
    /// Raised when the local player starts (<c>true</c>) or stops (<c>false</c>) spectating through an
    /// Overwatch camera. The console BUI uses this to hide its window while spectating so the camera
    /// feed isn't covered, then restore it — driven from here (an always-running system) rather than
    /// from the UI, which stops updating once hidden.
    /// </summary>
    public event Action<bool>? LocalWatchStateChanged;

    private bool _localWatching;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<OverwatchAnnouncementEvent>(OnAnnouncement);

        SubscribeLocalEvent<RatOverwatchRelayedSoundComponent, ComponentRemove>(OnRelayedRemove);
        SubscribeLocalEvent<RatOverwatchRelayedSoundComponent, EntityTerminatingEvent>(OnRelayedRemove);
        SubscribeLocalEvent<RatOverwatchWatchingComponent, ComponentInit>(OnLocalWatchingInit);
        SubscribeLocalEvent<RatOverwatchWatchingComponent, ComponentRemove>(OnLocalWatchingRemoved);

        _announcementOverlay = new(_cache, _timing);
        _overlay.AddOverlay(_announcementOverlay);
    }

    /// <summary>
    /// Recomputes whether the local player is currently spectating and fires
    /// <see cref="LocalWatchStateChanged"/> on transitions only. Cheap and idempotent, so it can be
    /// called from any component hook or every tick as a safety net.
    /// </summary>
    private void UpdateLocalWatchState()
    {
        var watching = _player.LocalEntity is { } player
            && TryComp<RatOverwatchWatchingComponent>(player, out var comp)
            && comp.Watching.HasValue;

        if (watching == _localWatching)
            return;

        _localWatching = watching;
        LocalWatchStateChanged?.Invoke(watching);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_announcementOverlay);
    }

    private void OnAnnouncement(OverwatchAnnouncementEvent ev)
    {
        var title = Loc.GetString("overwatch-announcement-title",
            ("overwatchTitle", ev.OverwatchTitle),
            ("targetName", ev.TargetName));
        _announcementOverlay.SetText(title, ev.Message, ev.Color);

        _audio.PlayGlobal("/Audio/_Crescent/Announcements/radio_announcement.ogg", Filter.Local(), false);
    }

    private void OnLocalWatchingInit(Entity<RatOverwatchWatchingComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        _announcementOverlay?.Reset();
        UpdateLocalWatchState();
    }

    private void OnLocalWatchingRemoved(Entity<RatOverwatchWatchingComponent> ent, ref ComponentRemove args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        _announcementOverlay.Reset();
        CleanupAllRelayedSounds();
        UpdateLocalWatchState();
    }

    private void OnRelayedRemove<T>(Entity<RatOverwatchRelayedSoundComponent> ent, ref T args)
    {
        TryDeleteRelayed(ent.Comp.Relay);
    }

    private void TryDeleteRelayed(EntityUid? relay)
    {
        if (relay == null)
            return;

        if (IsClientSide(relay.Value))
            QueueDel(relay);
    }

    private void RemoveRelayedSound(EntityUid soundUid)
    {
        if (_relayedSounds.Remove(soundUid, out var relayedUid) && relayedUid.Valid)
        {
            if (IsClientSide(relayedUid))
                QueueDel(relayedUid);
        }
    }

    /// <summary>
    /// Tears down every relayed sound. Cheap to call when nothing is relayed — <see cref="Update"/>
    /// runs this on the not-spectating path, i.e. every frame for every client in the game, so the
    /// entity sweep has to stay behind the early-out rather than running unconditionally.
    /// </summary>
    private void CleanupAllRelayedSounds()
    {
        if (_relayedSounds.Count == 0 && !_anyRelayTagged)
            return;

        foreach (var relayedUid in _relayedSounds.Values)
        {
            if (relayedUid.Valid && IsClientSide(relayedUid))
                QueueDel(relayedUid);
        }
        _relayedSounds.Clear();

        var relayQuery = AllEntityQuery<RatOverwatchRelayedSoundComponent>();
        while (relayQuery.MoveNext(out var uid, out var relay))
        {
            TryDeleteRelayed(relay.Relay);
            RemCompDeferred<RatOverwatchRelayedSoundComponent>(uid);
        }

        _anyRelayTagged = false;
    }

    private void CleanupStaleRelayedSounds(HashSet<EntityUid> activeSounds)
    {
        _staleScratch.Clear();
        foreach (var soundUid in _relayedSounds.Keys)
        {
            if (!activeSounds.Contains(soundUid))
                _staleScratch.Add(soundUid);
        }

        foreach (var soundUid in _staleScratch)
        {
            RemoveRelayedSound(soundUid);
        }
    }

    private void UpdateOrCreateRelayedSound(
        EntityUid uid,
        AudioComponent audio,
        MapCoordinates eyePosition,
        Vector2 delta,
        EntityUid player)
    {
        var position = eyePosition.Offset(delta);

        if (_relayedSounds.TryGetValue(uid, out var relayedUid) && relayedUid.Valid)
        {
            _transform.SetMapCoordinates(relayedUid, position);
            return;
        }

        var entityPosition = _transform.ToCoordinates(position);
        _toRelay.Add((uid, audio, null, entityPosition));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Safety net: guarantees window restore even if a component hook is ever missed.
        UpdateLocalWatchState();

        if (_player.LocalEntity is not { } player ||
            !TryComp(player, out RatOverwatchWatchingComponent? watching) ||
            !watching.Watching.HasValue ||
            !TryComp(player, out TransformComponent? playerTransform))
        {
            CleanupAllRelayedSounds();
            return;
        }

        _toRelay.Clear();

        var eyePosition = _eye.CurrentEye.Position;
        var listenerCoords = _transform.ToCoordinates(eyePosition);
        var maxDistanceSquared = MaxSoundRelayDistance * MaxSoundRelayDistance;

        _activeSounds.Clear();

        var query = AllEntityQuery<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var audio, out var xform))
        {
            if (IsClientSide(uid))
                continue;

            if (eyePosition.MapId != xform.MapID)
                continue;

            var audioCoords = xform.Coordinates;
            if (!audioCoords.TryDelta(EntityManager, _transform, listenerCoords, out var delta))
                continue;

            var distanceSquared = delta.LengthSquared();

            if (distanceSquared <= audio.MaxDistance * audio.MaxDistance)
            {
                RemoveRelayedSound(uid);
                continue;
            }

            if (distanceSquared > maxDistanceSquared)
                continue;

            _activeSounds.Add(uid);
            UpdateOrCreateRelayedSound(uid, audio, eyePosition, delta, player);
        }

        foreach (var (uid, audio, _, coordinates) in _toRelay)
        {
            var relayedAudio = _audio.PlayStatic(
                new SoundPathSpecifier(audio.FileName),
                player,
                coordinates,
                audio.Params
            );

            if (relayedAudio is not { Entity: var relayedAudioEnt })
                continue;

            _audio.SetPlaybackPosition(relayedAudioEnt, audio.PlaybackPosition);

            _relayedSounds[uid] = relayedAudioEnt;

            if (TryComp<RatOverwatchRelayedSoundComponent>(uid, out var relayedComp))
            {
                relayedComp.Relay = relayedAudioEnt;
                _anyRelayTagged = true;
            }
        }

        CleanupStaleRelayedSounds(_activeSounds);
    }
}
