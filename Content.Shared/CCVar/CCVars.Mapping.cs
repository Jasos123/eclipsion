using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Will mapping mode enable autosaves when it's activated?
    /// </summary>
    public static readonly CVarDef<bool>
        AutosaveEnabled = CVarDef.Create("mapping.autosave", true, CVar.SERVERONLY);

    /// <summary>
    ///     Autosave interval in seconds.
    /// </summary>
    public static readonly CVarDef<float>
        AutosaveInterval = CVarDef.Create("mapping.autosave_interval", 3600f, CVar.SERVERONLY);

    /// <summary>
    ///     How many autosaves to keep per map. Older ones get deleted as new ones are made.
    ///     Zero or less keeps every autosave forever.
    /// </summary>
    public static readonly CVarDef<int>
        AutosaveRotation = CVarDef.Create("mapping.autosave_rotation", 3, CVar.SERVERONLY);

    /// <summary>
    ///     Directory in server user data to save to. Saves will be inside folders in this directory.
    /// </summary>
    public static readonly CVarDef<string>
        AutosaveDirectory = CVarDef.Create("mapping.autosave_dir", "Autosaves", CVar.SERVERONLY);
}
