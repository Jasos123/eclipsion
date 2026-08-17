using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Audio.Jukebox;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedJukeboxSystem))]
public sealed partial class JukeboxComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<JukeboxPrototype>? SelectedSongId;

    [DataField, AutoNetworkedField]
    public EntityUid? AudioStream;

    /// <summary>
    /// Base playback volume in dB, before the listener's own boombox volume slider is applied.
    /// Jukeboxes used to play at 0 dB, which is over ten times the gain the station's own ambient
    /// music runs at (-12 dB plus the music slider), so a single boombox drowned out everything.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Volume = -8f;

    /// <summary>
    /// Audible radius in tiles. Attenuation is linear from the listener out to this range, so this
    /// also sets how steeply the track fades as you walk away: a smaller range is both quieter at a
    /// distance and gone sooner. A hand-carried boombox wants a much tighter range than a jukebox
    /// bolted to a bar wall.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 10f;

    /// <summary>
    /// Upcoming songs to play after the current one finishes, in order.
    /// The currently playing track is not included here.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<JukeboxPrototype>> Queue = new();

    /// <summary>
    /// Whether the jukebox should automatically advance to the next queued
    /// song when the current one ends. Cleared when the user presses stop.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool QueueActive;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OnState;

    /// <summary>
    /// RSI state for the jukebox being on.
    /// </summary>
    [DataField]
    public string? OffState;

    /// <summary>
    /// RSI state for the jukebox track being selected.
    /// </summary>
    [DataField]
    public string? SelectState;

    [ViewVariables]
    public bool Selecting;

    [ViewVariables]
    public float SelectAccumulator;
}

[Serializable, NetSerializable]
public sealed class JukeboxPlayingMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxPauseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxSelectedMessage(ProtoId<JukeboxPrototype> songId) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> SongId { get; } = songId;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetTimeMessage(float songTime) : BoundUserInterfaceMessage
{
    public float SongTime { get; } = songTime;
}

[Serializable, NetSerializable]
public sealed class JukeboxQueueAddMessage(ProtoId<JukeboxPrototype> songId) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> SongId { get; } = songId;
}

[Serializable, NetSerializable]
public sealed class JukeboxQueueRemoveMessage(int index) : BoundUserInterfaceMessage
{
    public int Index { get; } = index;
}

[Serializable, NetSerializable]
public sealed class JukeboxQueueClearMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class JukeboxQueueNextMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum JukeboxVisuals : byte
{
    VisualState
}

[Serializable, NetSerializable]
public enum JukeboxVisualState : byte
{
    On,
    Off,
    Select,
}

public enum JukeboxVisualLayers : byte
{
    Base
}
