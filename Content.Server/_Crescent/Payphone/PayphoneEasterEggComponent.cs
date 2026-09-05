using Robust.Shared.Audio;

namespace Content.Server._Crescent.Payphone;

/// <summary>
/// A persistent caller can uncover a private, timed conversation.
/// </summary>
[RegisterComponent]
public sealed partial class PayphoneEasterEggComponent : Component
{
    [DataField]
    public int RequiredAttempts = 5;

    [DataField]
    public TimeSpan AttemptInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan LineInterval = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan StunDuration = TimeSpan.FromSeconds(4);

    /// <summary>Played once when dialing begins, never restarted by subsequent calling lines.</summary>
    [DataField]
    public SoundSpecifier? CallingSound;

    /// <summary>
    /// Optional recording played privately when the final line appears.
    /// </summary>
    [DataField]
    public SoundSpecifier? FinalLineSound;

    [ViewVariables]
    public EntityUid? Caller;

    [ViewVariables]
    public int Attempts;

    /// <summary>Next line to send, or -1 while not in a conversation.</summary>
    [ViewVariables]
    public int NextLine = -1;

    [ViewVariables]
    public TimeSpan NextAttempt;

    [ViewVariables]
    public TimeSpan NextLineTime;

    [ViewVariables]
    public EntityUid? VoiceStream;

    [ViewVariables]
    public EntityUid? CallingStream;
}
