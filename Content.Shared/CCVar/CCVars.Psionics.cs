using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///    Whether glimmer is enabled.
    /// </summary>
    public static readonly CVarDef<bool> GlimmerEnabled =
        CVarDef.Create("glimmer.enabled", true, CVar.REPLICATED);

    /// <summary>
    ///     The rate at which glimmer linearly decays. Since glimmer increases (usually) follow a logistic curve, this means glimmer
    ///     becomes increasingly harder to raise after ~502 points.
    /// </summary>
    public static readonly CVarDef<float> GlimmerLinearDecayPerSecond =
        CVarDef.Create("glimmer.linear_decay_per_second", 1f, CVar.SERVERONLY);

    /// <summary>
    ///     How many seconds between updates to passive glimmer decay.
    /// </summary>
    public static readonly CVarDef<float> GlimmerDecayUpdateInterval =
        CVarDef.Create("glimmer.decay_update_interval", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Whether random rolls for psionics are allowed.
    ///     Guaranteed psionics will still go through.
    /// </summary>
    public static readonly CVarDef<bool> PsionicRollsEnabled =
        CVarDef.Create("psionics.rolls_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     How much Potentia a Psion earns per point of glimmer the power they just used produced.
    ///     Set to 0 to switch off progression from using powers.
    /// </summary>
    public static readonly CVarDef<float> PsionicPotentiaPerGlimmer =
        CVarDef.Create("psionics.potentia_per_glimmer", 1f, CVar.SERVERONLY);

    /// <summary>
    ///     How fast the diminishing-returns counter on power use decays, in stacks per second. One stack
    ///     halves the Potentia from the next cast, two thirds it, and so on. At the default a Psion is back
    ///     to full value roughly 45 seconds after a cast.
    /// </summary>
    public static readonly CVarDef<float> PsionicCastFatigueDecay =
        CVarDef.Create("psionics.cast_fatigue_decay", 1f / 45f, CVar.SERVERONLY);

    /// <summary>
    ///     Ceiling on the diminishing-returns counter, so a long spam session cannot lock a Psion out of
    ///     progression for the rest of the round.
    /// </summary>
    public static readonly CVarDef<float> PsionicCastFatigueMax =
        CVarDef.Create("psionics.cast_fatigue_max", 8f, CVar.SERVERONLY);

    /// <summary>
    ///     Potentia per minute earned by an awake Psion simply existing in a noosphere sitting at equilibrium
    ///     glimmer (~502). This scales linearly with glimmer, so a quiet station barely progresses anyone and
    ///     a screaming one progresses everyone. Set to 0 to switch off ambient progression.
    /// </summary>
    public static readonly CVarDef<float> PsionicGlimmerDripPerMinute =
        CVarDef.Create("psionics.glimmer_drip_per_minute", 2f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between ambient glimmer progression ticks.
    /// </summary>
    public static readonly CVarDef<float> PsionicGlimmerDripInterval =
        CVarDef.Create("psionics.glimmer_drip_interval", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     When mindbroken, permanently eject the player from their own body, and turn their character into an NPC.
    ///     Congratulations, now they *actually* aren't a person anymore.
    ///     For people who complained that it wasn't obvious enough from the text that Mindbreaking is a form of Murder.
    /// </summary>
    public static readonly CVarDef<bool> ScarierMindbreaking =
        CVarDef.Create("psionics.scarier_mindbreaking", false, CVar.SERVERONLY);

    /// <summary>
    /// Allow Ethereal Ent to PassThrough Walls/Objects while in Ethereal.
    /// </summary>
    public static readonly CVarDef<bool> EtherealPassThrough =
        CVarDef.Create("ic.EtherealPassThrough", false, CVar.SERVER);
}
