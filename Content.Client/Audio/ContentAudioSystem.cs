using Content.Shared.Audio;
using Content.Shared.GameTicking;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client.Audio;

public sealed partial class ContentAudioSystem : SharedContentAudioSystem
{
    // Need how much volume to change per tick and just remove it when it drops below "0"
    private readonly Dictionary<EntityUid, float> _fadingOut = new();

    // Need volume change per tick + target volume.
    private readonly Dictionary<EntityUid, (float VolumeChange, float TargetVolume)> _fadingIn = new();

    private readonly List<EntityUid> _fadeToRemove = new();

    private const float MinVolume = -32f;
    private const float DefaultDuration = 2f;

    /*
     * Gain multipliers for specific audio sliders.
     * The float value will get multiplied by this when setting
     * i.e. a gain of 0.5f x 3 will equal 1.5f which is supported in OpenAL.
     */

    public const float MasterVolumeMultiplier = 3f;
    public const float MidiVolumeMultiplier = 0.25f;
    public const float AmbienceMultiplier = 3f;
    public const float CombatMusicMultiplier = 3f;
    public const float AmbientMusicMultiplier = 3f;
    public const float LobbyMultiplier = 3f;
    public const float InterfaceMultiplier = 2f;
    public const float AnnouncerMultiplier = 3f;
    public const float CommunicationsMultiplier = 3f;
    // Multiplier for boombox / jukebox individual volume cvar.
    public const float BoomboxMultiplier = 3f;

    // Duck amount, in dB, that a fully cranked boombox ducking slider corresponds to.
    public const float BoomboxDuckMaxDb = 20f;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;
        InitializeAmbientMusic();
        InitializeLobbyMusic();
        InitializeDucking();
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _fadingOut.Clear();

        // Preserve lobby music but everything else should get dumped.
        var lobbyMusic = _lobbySoundtrackInfo?.MusicStreamEntityUid;
        TryComp(lobbyMusic, out AudioComponent? lobbyMusicComp);
        var oldMusicGain = lobbyMusicComp?.Gain;

        var restartAudio = _lobbyRoundRestartAudioStream;
        TryComp(restartAudio, out AudioComponent? restartComp);
        var oldAudioGain = restartComp?.Gain;

        SilenceAudio();

        if (oldMusicGain != null)
        {
            Audio.SetGain(lobbyMusic, oldMusicGain.Value, lobbyMusicComp);
        }

        if (oldAudioGain != null)
        {
            Audio.SetGain(restartAudio, oldAudioGain.Value, restartComp);
        }
        PlayRestartSound(ev);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ShutdownAmbientMusic();
        ShutdownLobbyMusic();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        //UpdateAmbientMusic();
        UpdateLobbyMusic();
        UpdateFades(frameTime);
        UpdateDucking(frameTime);
    }

    #region Fades

    /// <summary>
    /// Gets the volume a fade should start from. AudioComponent.Volume is a straight passthrough for the
    /// source's current gain, so it reads back as -infinity whenever the stream happens to be silent: either
    /// a volume slider sits at 0, or the engine muted the stream for being on another map. Dividing that by
    /// the duration gives an infinite per-tick change, and the first frame the volume is finite again the
    /// fade lands on +infinity, which sticks in the component's AudioParams and makes OpenAL reject every
    /// gain set from then on. So fall back to the intended volume, which stays finite in those cases.
    /// </summary>
    private static bool TryGetFadeVolume(AudioComponent component, out float volume)
    {
        volume = component.Volume;

        if (!float.IsFinite(volume))
            volume = component.Params.Volume;

        return float.IsFinite(volume);
    }

    public void FadeOut(EntityUid? stream, AudioComponent? component = null, float duration = DefaultDuration)
    {
        if (stream == null || duration <= 0f || !Resolve(stream.Value, ref component))
            return;

        // Just in case
        // TODO: Maybe handle the removals by making it seamless?
        _fadingIn.Remove(stream.Value);

        if (!TryGetFadeVolume(component, out var curVolume) || curVolume <= MinVolume)
        {
            // Nothing audible left to fade, so go straight to what the fade would have ended on.
            _audio.Stop(stream);
            _fadingOut.Remove(stream.Value);
            return;
        }

        _fadingOut[stream.Value] = (curVolume - MinVolume) / duration;
    }

    public void FadeIn(EntityUid? stream, AudioComponent? component = null, float duration = DefaultDuration)
    {
        if (stream == null || duration <= 0f || !Resolve(stream.Value, ref component))
            return;

        if (!TryGetFadeVolume(component, out var curVolume) || curVolume < MinVolume)
            return;

        _fadingOut.Remove(stream.Value);
        var change = (MinVolume - curVolume) / duration;
        _fadingIn[stream.Value] = (change, curVolume);
        component.Volume = MinVolume;
    }

    private void UpdateFades(float frameTime)
    {
        _fadeToRemove.Clear();

        foreach (var (stream, change) in _fadingOut)
        {
            if (!TryComp(stream, out AudioComponent? component))
            {
                _fadeToRemove.Add(stream);
                continue;
            }

            var volume = component.Volume - change * frameTime;

            // The stream was silenced from under us mid-fade (map change, slider at 0), so the step above
            // is infinite. MathF.Max won't clamp +infinity, and letting it through would burn a permanent
            // infinite volume into the params. It's inaudible either way, so just end the fade here.
            if (!float.IsFinite(volume))
                volume = MinVolume;

            volume = MathF.Max(MinVolume, volume);
            _audio.SetVolume(stream, volume, component);

            if (volume.Equals(MinVolume))
            {
                _audio.Stop(stream);
                _fadeToRemove.Add(stream);
            }
        }

        foreach (var stream in _fadeToRemove)
        {
            _fadingOut.Remove(stream);
        }

        _fadeToRemove.Clear();

        foreach (var (stream, (change, target)) in _fadingIn)
        {
            // Cancelled elsewhere
            if (!TryComp(stream, out AudioComponent? component))
            {
                _fadeToRemove.Add(stream);
                continue;
            }

            var volume = component.Volume - change * frameTime;

            // Same as above: silenced mid-fade. Restart the ramp from the floor rather than letting an
            // infinite value through, so the fade picks back up once the stream is audible again.
            if (!float.IsFinite(volume))
                volume = MinVolume;

            volume = MathF.Min(target, volume);
            _audio.SetVolume(stream, volume, component);

            if (volume.Equals(target))
            {
                _fadeToRemove.Add(stream);
            }
        }

        foreach (var stream in _fadeToRemove)
        {
            _fadingIn.Remove(stream);
        }
    }

    #endregion
}

/// <summary>
/// Raised whenever ambient music tries to play.
/// </summary>
[ByRefEvent]
public record struct PlayAmbientMusicEvent(bool Cancelled = false);
