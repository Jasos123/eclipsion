using Content.Shared.Drugs;
using Content.Shared.StatusEffect;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Drugs;

public sealed class RainbowOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;
    private readonly ShaderInstance _rainbowShader;

    public float Intoxication = 0.0f;

    private const float VisualThreshold = 10.0f;
    private const float PowerDivisor = 250.0f;

    /// <summary>How long, in seconds, the effect takes to work its way up to its target strength.</summary>
    private const float RampUpTime = 16.0f;

    /// <summary>How long, in seconds, it takes to fade back out once the effect starts running out.</summary>
    private const float RampDownTime = 6.0f;

    private float EffectScale => Math.Clamp((Intoxication - VisualThreshold) / PowerDivisor, 0.0f, 1.0f);

    public RainbowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _rainbowShader = _prototypeManager.Index<ShaderPrototype>("Rainbow").InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var playerEntity = _playerManager.LocalEntity;

        if (playerEntity == null)
            return;

        if (!_entityManager.HasComponent<SeeingRainbowsComponent>(playerEntity)
            || !_entityManager.TryGetComponent<StatusEffectsComponent>(playerEntity, out var status))
            return;

        var statusSys = _sysMan.GetEntitySystem<StatusEffectsSystem>();
        if (!statusSys.TryGetTime(playerEntity.Value, DrugOverlaySystem.RainbowKey, out var time, status))
            return;

        var timeLeft = (float) (time.Value.Item2 - _timing.CurTime).TotalSeconds;

        // Strength simply follows the time left on the status effect, easing in as it builds and
        // out as it runs down. This used to be driven by a separate ticker that was only ever
        // reset when the overlay was removed, so any drug that tops its own status effect up -
        // which is all of them - drove the divisor negative and slammed the shader to full
        // strength for the rest of the trip.
        var rate = args.DeltaSeconds / (timeLeft > Intoxication ? RampUpTime : RampDownTime);
        Intoxication += (timeLeft - Intoxication) * Math.Clamp(rate, 0.0f, 1.0f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return EffectScale > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _rainbowShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _rainbowShader.SetParameter("effectScale", EffectScale);
        handle.UseShader(_rainbowShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
