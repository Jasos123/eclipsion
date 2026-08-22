using System.Threading;
using Content.Server.Announcements.Systems;
using Content.Shared.Announcements.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Crescent.PlanetfallObjectives;

public sealed class PlanetfallBarrierAnnouncerSystem : EntitySystem
{
    private const string AnnouncementId = "planetfallEnemyPresence";
    private const string AnnouncerId = "HullrotAnnouncer";
    private static readonly TimeSpan ReleaseAnnouncementDelay = TimeSpan.FromSeconds(8);
    private const string MarkerBlockerPrototypeId = "MarkerBlocker";
    private const string MidwaySender = "Castle Garrison";
    // Sent silently (no announcer audio) halfway through the barrier timer, as flavour before the release.
    private const string MidwayAnnouncement = """
        [GARRISON DISPATCH]

        Labour trouble in the outer settlements. Quotas unmet, a work gang refusing to return to the fields, a checkpoint pelted with stones. Patrols have made arrests and the magistrate reports the matter settled.

        Word of the disturbance has reached the ears of Olywier. The Charter Lord has heard it, and waved it away - peasant grumbling, nothing more. The festival will proceed as planned.

        All personnel are to keep order and avoid alarming the celebrants.
        """;
    private const string ReleaseAnnouncement = """
        [EMERGENCY BROADCAST]

        Enemy presence detected. All personnel to arms. Protect Olyw-

        Signal interrupted...

        All personnel, lay down your arms. The Dominion is not unbreakable. Its control ends today. Stand with the uprising and reclaim your freedom!

        FOR KANE!

        [The Planetfall barrier has been released!]
        """;

    [Dependency] private readonly AnnouncerSystem _announcer = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlanetfallBarrierAnnouncerComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PlanetfallBarrierAnnouncerComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnComponentInit(EntityUid uid, PlanetfallBarrierAnnouncerComponent component, ComponentInit args)
    {
        StartSchedule(uid, component);
    }

    private void OnComponentShutdown(EntityUid uid, PlanetfallBarrierAnnouncerComponent component, ComponentShutdown args)
    {
        component.TimerCancel.Cancel();
    }

    private void StartSchedule(EntityUid uid, PlanetfallBarrierAnnouncerComponent component)
    {
        if (component.SchedulesCreated)
            return;

        component.SchedulesCreated = true;
        component.TimerCancel.Cancel();
        component.TimerCancel = new CancellationTokenSource();

        var releaseDelay = TimeSpan.FromSeconds(component.ReleaseDelay);
        var midwayDelay = TimeSpan.FromSeconds(component.MidwayDelay ?? component.ReleaseDelay / 2f);

        if (midwayDelay > TimeSpan.Zero && midwayDelay < releaseDelay)
            Timer.Spawn(midwayDelay, () => SendMidwayReport(uid), component.TimerCancel.Token);

        Timer.Spawn(releaseDelay, () => TryRelease(uid), component.TimerCancel.Token);
    }

    /// <summary>
    ///     Silent flavour announcement: chat only, no announcer audio.
    /// </summary>
    private void SendMidwayReport(EntityUid uid)
    {
        if (!TryComp<PlanetfallBarrierAnnouncerComponent>(uid, out var component) || component.MidwaySent ||
            component.Released)
            return;

        component.MidwaySent = true;

        _prototype.TryIndex<AnnouncerPrototype>(AnnouncerId, out var announcer);
        var announcementId = _announcer.GetAnnouncementId(AnnouncementId);
        _announcer.SendAnnouncementMessage(announcementId, MidwayAnnouncement, MidwaySender,
            announcerOverride: announcer);
    }

    public bool TryReleaseOnMap(MapId mapId)
    {
        var query = EntityQueryEnumerator<PlanetfallBarrierAnnouncerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.MapID != mapId)
                continue;

            return TryRelease(uid);
        }

        return false;
    }

    public bool TryReleaseAny()
    {
        var query = EntityQueryEnumerator<PlanetfallBarrierAnnouncerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Released)
                continue;

            if (TryRelease(uid))
                return true;
        }

        return false;
    }

    private bool TryRelease(EntityUid uid)
    {
        if (!TryComp<PlanetfallBarrierAnnouncerComponent>(uid, out var component) || component.Released)
            return false;

        if (!TryComp<TransformComponent>(uid, out var controllerTransform))
            return false;

        var mapId = controllerTransform.MapID;
        component.Released = true;

        _prototype.TryIndex<AnnouncerPrototype>(AnnouncerId, out var announcer);
        var announcementId = _announcer.GetAnnouncementId(AnnouncementId);
        _announcer.SendAnnouncementAudio(announcementId, Filter.Broadcast(), announcer);

        Timer.Spawn(ReleaseAnnouncementDelay, () => CompleteRelease(uid, mapId, announcementId, announcer),
            component.TimerCancel.Token);

        return true;
    }

    private void CompleteRelease(EntityUid uid, MapId mapId, string announcementId, AnnouncerPrototype? announcer)
    {
        if (!TryComp<PlanetfallBarrierAnnouncerComponent>(uid, out var component) || !component.Released)
            return;

        _announcer.SendAnnouncementMessage(announcementId, ReleaseAnnouncement, announcerOverride: announcer);

        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var blockerUid, out var metadata, out var transform))
        {
            if (metadata.EntityPrototype?.ID != MarkerBlockerPrototypeId || transform.MapID != mapId)
                continue;

            QueueDel(blockerUid);
        }
    }
}
