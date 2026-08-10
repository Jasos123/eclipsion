using Content.Server._Crescent.UnionfallCapturePoint;
using Robust.Shared.Player;
using Content.Server.Announcements.Systems;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.DoAfter;
using Content.Shared.DoAfter;
using Content.Shared._Crescent.UnionfallCapturePoint;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;


namespace Content.Server._Crescent.UnionfallCapturePoint;

public sealed class UnionfallAnnouncerSystem : EntitySystem
{
    [Dependency] private readonly AnnouncerSystem _announcer = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;

    private ISawmill _sawmill = default!;

    private int _currentGeneration = 0;

    public override void Initialize()
    {
        SubscribeLocalEvent<UnionfallAnnouncerComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _currentGeneration++;
    }

    public void SkipGracePeriod()
    {
        _currentGeneration++;

        _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallGraceOver"), Filter.Broadcast(),
            "TURNING FRONT HAS PASSED. Emergency chronal field has been disabled. Long-range radar confirms a hostile fleet.");

        var query = EntityQueryEnumerator<UnionfallGraceBarrierComponent>();
        while (query.MoveNext(out var barrierUid, out var _))
        {
            EntityManager.DeleteEntity(barrierUid);
        }
    }

    private void OnComponentInit(EntityUid uid, UnionfallAnnouncerComponent component, ComponentInit args)
    {
        int myGeneration = _currentGeneration;

        TimeSpan graceTime = TimeSpan.FromSeconds(component.GracePeriod);

        Timer.Spawn(TimeSpan.FromMinutes(1), () =>
        {
            if (_currentGeneration != myGeneration) return;
            AnnouncementWarStart(graceTime);
        });

        Timer.Spawn(graceTime * 0.50, () =>
        {
            if (_currentGeneration != myGeneration) return;
            AnnouncementWarPeriodic(graceTime - graceTime * 0.50);
        });

        Timer.Spawn(graceTime - TimeSpan.FromSeconds(10), () =>
        {
            if (_currentGeneration != myGeneration) return;
            AnnouncementWarGraceOver(myGeneration);
        });

        Timer.Spawn(graceTime, () =>
        {
            if (_currentGeneration != myGeneration) return;
            var query = EntityQueryEnumerator<UnionfallGraceBarrierComponent>();
            while (query.MoveNext(out var barrierUid, out var _))
            {
                EntityManager.DeleteEntity(barrierUid);
            }
        });
    }

    private void AnnouncementWarStart(TimeSpan time)
    {
        _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallBegin"), Filter.Broadcast(),
                "SEVERE TURNING FRONT DETECTED - Emergency chronal field deployed, estimated front passage time: <" + time.ToString(@"hh\:mm\:ss") + ">... Recurrence telemetry confirms a hostile fleet in the operating area.");
    }

    private void AnnouncementWarPeriodic(TimeSpan time)
    {
        _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallPeriodic"), Filter.Broadcast(),
                "<" + time.ToString(@"hh\:mm\:ss") + "> until the Turning front passes.");
    }

    private void AnnouncementWarAlmost()
    {
        _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallAlmost"), Filter.Broadcast(),
                "<00:01:00> LEFT UNTIL THE TURNING FRONT PASSES.");
    }

    private void AnnouncementWarGraceOver(int myGeneration)
    {
        for (int i = 10; i > 0; i--)
        {
            int countdown = i;
            Timer.Spawn(TimeSpan.FromSeconds(10 - countdown), () =>
            {
                if (_currentGeneration != myGeneration) return;
                _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallCountdown"), Filter.Broadcast(),
                    "TURNING FRONT PASSING IN <00:00:" + countdown.ToString("D2") + ">...");
            });
        }

        Timer.Spawn(TimeSpan.FromSeconds(10), () =>
        {
            if (_currentGeneration != myGeneration) return;
            _announcer.SendAnnouncement(_announcer.GetAnnouncementId("unionfallGraceOver"), Filter.Broadcast(),
                "TURNING FRONT HAS PASSED. Emergency chronal field has been disabled. Long-range radar confirms a hostile fleet.");
        });
    }
}
