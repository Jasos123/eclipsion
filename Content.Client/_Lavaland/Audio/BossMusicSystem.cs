using Content.Client.Audio;
using Content.Shared._Lavaland.Audio;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Lavaland.Audio;

public sealed class BossMusicSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly ContentAudioSystem _audioContent = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private static float _volumeSlider;
    private Entity<AudioComponent?>? _bossMusicStream;
    private BossMusicPrototype? _musicProto;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configManager, CCVars.LobbyMusicVolume, BossVolumeCVarChanged, true);

        SubscribeNetworkEvent<BossMusicStartupEvent>(OnBossInit);
        SubscribeNetworkEvent<BossMusicStopEvent>(OnBossDefeated);

        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnMindRemoved);
        SubscribeLocalEvent<ActorComponent, MobStateChangedEvent>(OnPlayerDeath);
        SubscribeLocalEvent<ActorComponent, EntParentChangedMessage>(OnPlayerParentChange);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
    }

    public override void Shutdown()
    {
        _bossMusicStream = _audio.Stop(_bossMusicStream);
        _musicProto = null;
        base.Shutdown();
    }

    private void BossVolumeCVarChanged(float obj)
    {
        _volumeSlider = SharedAudioSystem.GainToVolume(obj);

        if (_bossMusicStream != null && _musicProto != null)
        {
            _audio.SetVolume(_bossMusicStream, _musicProto.Sound.Params.Volume + _volumeSlider);
        }
    }

    private void OnBossInit(BossMusicStartupEvent args)
    {
        if (_musicProto != null || _bossMusicStream != null)
            return;

        _audioContent.DisableAmbientMusic();

        var sound = _proto.Index(args.MusicId);
        _musicProto = sound;

        var stream = _audio.PlayGlobal(
            sound.Sound,
            Filter.Local(),
            false,
            AudioParams.Default.WithVolume(sound.Sound.Params.Volume + _volumeSlider).WithLoop(true));

        if (stream == null)
        {
            _musicProto = null;
            return;
        }

        _bossMusicStream = (stream.Value.Entity, stream.Value.Component);

        if (sound.FadeIn)
            _audioContent.FadeIn(_bossMusicStream, stream.Value.Component, sound.FadeInTime);
    }

    private void OnBossDefeated(BossMusicStopEvent args)
    {
        EndAllMusic();
    }

    private void OnMindRemoved(LocalPlayerDetachedEvent args)
    {
        EndAllMusic();
    }

    private void OnPlayerDeath(Entity<ActorComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.PlayerSession == _player.LocalSession &&
            args.NewMobState == MobState.Dead)
            EndAllMusic();
    }

    /// <summary>
    /// Raised when salvager escapes from lavaland (ohio reference)
    /// </summary>
    private void OnPlayerParentChange(Entity<ActorComponent> ent, ref EntParentChangedMessage args)
    {
        if (ent.Comp.PlayerSession == _player.LocalSession &&
            args.OldMapId != null)
            EndAllMusic();
    }

    private void OnRoundEnd(RoundEndMessageEvent args)
    {
        _bossMusicStream = _audio.Stop(_bossMusicStream);
        _musicProto = null;
    }

    private void EndAllMusic()
    {
        var musicProto = _musicProto;
        var stream = _bossMusicStream;

        // Clear first so a failed/missing stream cannot poison the next startup attempt.
        _musicProto = null;
        _bossMusicStream = null;

        if (stream == null)
            return;

        if (musicProto?.FadeIn == true)
            _audioContent.FadeOut(stream, duration: musicProto.FadeOutTime);
        else
            _audio.Stop(stream);
    }
}
