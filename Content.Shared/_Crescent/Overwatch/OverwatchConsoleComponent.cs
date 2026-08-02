using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Overwatch;

/// <summary>
/// Overwatch console component, for tracking faction members.
/// </summary>
/// <remarks>
/// The status/squad/search filters used to live here and be networked. They were never set — the panel
/// filters its own copy of the roster client-side and never sent the messages — so the fields, their
/// handlers and their message types were dead weight that also made two viewers of one console fight
/// over a single shared filter. Filtering stays client-local.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OverwatchConsoleComponent : Component
{
    /// <summary>
    /// The faction this console tracks.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Faction = string.Empty;

    /// <summary>
    /// Minimum time between two announcements sent from this console. Stops one operator from
    /// carpeting their whole faction in full-screen alerts.
    /// </summary>
    [DataField]
    public TimeSpan AnnounceCooldown = TimeSpan.FromSeconds(15);

    /// <summary>Server time an announcement was last sent from this console, for throttling.</summary>
    [ViewVariables]
    public TimeSpan? LastAnnounce;
}
