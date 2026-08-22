using System.Threading;

namespace Content.Server._Crescent.PlanetfallObjectives;

[RegisterComponent]
public sealed partial class PlanetfallBarrierAnnouncerComponent : Component
{
    [DataField]
    public float ReleaseDelay = 900f;

    /// <summary>
    ///     When the silent midway report is sent. Leave unset to send it halfway through <see cref="ReleaseDelay"/>.
    /// </summary>
    [DataField]
    public float? MidwayDelay;

    public bool SchedulesCreated;

    public bool MidwaySent;

    public bool Released;

    [NonSerialized]
    public CancellationTokenSource TimerCancel = new();
}
