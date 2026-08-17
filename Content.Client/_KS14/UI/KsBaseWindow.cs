using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.UI;

/// <remarks>KS14: ported from Klovnstation14 (AGPL-3.0-or-later).</remarks>
/// <summary>
///     XAML-root shim for chrome-less KS windows: XamlIL insists on an
///         instantiable public root type and <see cref="BaseWindow"/> is abstract,
///         so a fully custom-chromed window (the shuttle console)
///         roots its XAML here instead. Deliberately behaviour-free.
/// </summary>
[Virtual]
public class KsBaseWindow : BaseWindow;
