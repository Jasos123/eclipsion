using System.Text.RegularExpressions;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared.Administration.Logs;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Shared;
using Content.Shared.CCVar;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Shared._EE.Contractors.Systems;

public class SharedPassportSystem : EntitySystem
{
    public const int CurrentYear = 2450;
    public const int PassportLifetimeYears = 5;
    private const int MaxTextFieldLength = 64;
    private const string PIDChars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
    private static readonly Regex PassportIdRegex = new("^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$");

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PlayerLoadoutAppliedEvent>(OnPlayerLoadoutApplied);
        SubscribeLocalEvent<PassportComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PassportComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<PassportComponent, PassportSaveMessage>(OnSave);
    }

    private void OnExamined(EntityUid uid, PassportComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || component.IsClosed)
            return;

        var religion = DisplayOrUnspecified(component.Religion);

        args.PushMarkup(Loc.GetString("passport-registered-to", ("name", DisplayOrUnspecified(component.FullName))), 60);
        args.PushMarkup(Loc.GetString("passport-age", ("age", component.Age)), 59);
        args.PushMarkup(Loc.GetString("passport-species", ("species", DisplayOrUnspecified(component.Species))), 58);
        args.PushMarkup(Loc.GetString("passport-gender", ("gender", DisplayOrUnspecified(component.Sex))), 57);
        args.PushMarkup(Loc.GetString("passport-height", ("height", component.HeightCm)), 56);
        args.PushMarkup(Loc.GetString("passport-skin-color", ("color", DisplayOrUnspecified(component.SkinColor))), 55);
        args.PushMarkup(Loc.GetString("passport-eye-color", ("color", DisplayOrUnspecified(component.EyeColor))), 54);
        args.PushMarkup(Loc.GetString("passport-nationality", ("nationality", DisplayOrUnspecified(component.Nationality))), 53);
        args.PushMarkup(Loc.GetString("passport-religion", ("religion", religion)), 52);
        args.PushMarkup(Loc.GetString("passport-year-of-birth", ("year", CurrentYear - component.Age)), 51);
        args.PushMarkup(Loc.GetString("passport-issued", ("year", component.IssueYear)), 50);
        args.PushMarkup(Loc.GetString("passport-expires", ("year", component.ExpirationYear)), 49);
        args.PushMarkup(Loc.GetString("passport-pid", ("pid", DisplayOrUnspecified(component.PassportId))), 48);
        var status = component.Tampered
            ? "passport-status-tampered"
            : IsPassportValid(component)
                ? "passport-status-valid"
                : "passport-status-invalid";
        args.PushMarkup(Loc.GetString(status), 47);
    }

    private void OnPlayerLoadoutApplied(PlayerLoadoutAppliedEvent ev) =>
        SpawnPassportForPlayer(ev.Mob, ev.Profile, ev.JobId);

    public void SpawnPassportForPlayer(EntityUid mob, HumanoidCharacterProfile profile, string? jobId)
    {
        if (jobId == null || !_prototypeManager.TryIndex(
                jobId,
                out JobPrototype? jobPrototype)
            || !jobPrototype.CanHavePassport
            || Deleted(mob)
            || !Exists(mob)
            || !ShouldSpawnPassports)
            return;

        if (!_prototypeManager.TryIndex(
            profile.Nationality,
            out NationalityPrototype? nationalityPrototype) || !_prototypeManager.TryIndex(nationalityPrototype.PassportPrototype, out EntityPrototype? entityPrototype))
            return;

        var passportEntity = _entityManager.SpawnEntity(entityPrototype.ID, _sharedTransformSystem.GetMapCoordinates(mob));
        var passportComponent = _entityManager.GetComponent<PassportComponent>(passportEntity);

        UpdatePassportProfile(new(passportEntity, passportComponent), profile);

        // Try to find back-mounted storage apparatus
        if (_inventory.TryGetSlotEntity(mob, "back", out var item) &&
                EntityManager.TryGetComponent<StorageComponent>(item, out var inventory))
            // Try inserting the entity into the storage, if it can't, it leaves the loadout item on the ground
        {
            if (!EntityManager.TryGetComponent<ItemComponent>(passportEntity, out var itemComp)
                || !_storage.CanInsert(item.Value, passportEntity, out _, inventory, itemComp)
                || !_storage.Insert(item.Value, passportEntity, out _, playSound: false))
            {
                _adminLogManager.Add(
                    LogType.EntitySpawn,
                    LogImpact.Low,
                    $"Passport for {profile.Name} was spawned on the floor due to missing bag space");
            }
        }
    }

    private bool ShouldSpawnPassports =>
        _configManager.GetCVar(CCVar.CCVars.ContractorsEnabled) &&
        _configManager.GetCVar(CCVar.CCVars.ContractorsPassportEnabled);

    public void UpdatePassportProfile(Entity<PassportComponent> passport, HumanoidCharacterProfile profile)
    {
        var species = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var nationality = _prototypeManager.TryIndex(profile.Nationality, out NationalityPrototype? nationalityPrototype)
            ? Loc.GetString(nationalityPrototype.NameKey)
            : profile.Nationality;

        passport.Comp.FullName = profile.Name;
        passport.Comp.Age = profile.Age;
        passport.Comp.Species = string.IsNullOrWhiteSpace(profile.Customspeciename)
            ? Loc.GetString(species.Name)
            : profile.Customspeciename;
        passport.Comp.PortraitSpecies = profile.Species;
        passport.Comp.Sex = profile.Sex.ToString();
        passport.Comp.HeightCm = (int) MathF.Round(profile.Height * species.AverageHeight);
        passport.Comp.SkinColor = profile.Appearance.SkinColor.ToHexNoAlpha();
        passport.Comp.EyeColor = profile.Appearance.EyeColor.ToHexNoAlpha();
        passport.Comp.Nationality = nationality;
        passport.Comp.Religion = string.Empty;
        passport.Comp.IssueYear = CurrentYear;
        passport.Comp.ExpirationYear = CurrentYear + PassportLifetimeYears;
        passport.Comp.PassportId = GenerateIdentityString(profile.Name
            + profile.Species
            + profile.Height
            + profile.Age
            + profile.Nationality
            + profile.FlavorText);
        passport.Comp.Authentic = true;
        passport.Comp.Tampered = false;
        Dirty(passport);
    }

    private void OnUiOpened(Entity<PassportComponent> passport, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is PassportUiKey.Key)
            UpdateUiState(passport);
    }

    private void OnSave(Entity<PassportComponent> passport, ref PassportSaveMessage args)
    {
        var fullName = Clean(args.FullName);
        var age = Math.Clamp(args.Age, 0, 1000);
        var species = Clean(args.Species);
        var sex = Clean(args.Sex, 32);
        var heightCm = Math.Clamp(args.HeightCm, 0, 1000);
        var skinColor = CleanColor(args.SkinColor);
        var eyeColor = CleanColor(args.EyeColor);
        var nationality = Clean(args.Nationality);
        var religion = Clean(args.Religion);
        var passportId = Clean(args.PassportId, 32).ToUpperInvariant();
        var issueYear = Math.Clamp(args.IssueYear, 0, 9999);
        var expirationYear = Math.Clamp(args.ExpirationYear, 0, 9999);

        var changed = passport.Comp.FullName != fullName
            || passport.Comp.Age != age
            || passport.Comp.Species != species
            || passport.Comp.Sex != sex
            || passport.Comp.HeightCm != heightCm
            || passport.Comp.SkinColor != skinColor
            || passport.Comp.EyeColor != eyeColor
            || passport.Comp.Nationality != nationality
            || passport.Comp.Religion != religion
            || passport.Comp.PassportId != passportId
            || passport.Comp.IssueYear != issueYear
            || passport.Comp.ExpirationYear != expirationYear;

        if (changed)
        {
            passport.Comp.FullName = fullName;
            passport.Comp.Age = age;
            passport.Comp.Species = species;
            passport.Comp.Sex = sex;
            passport.Comp.HeightCm = heightCm;
            passport.Comp.SkinColor = skinColor;
            passport.Comp.EyeColor = eyeColor;
            passport.Comp.Nationality = nationality;
            passport.Comp.Religion = religion;
            passport.Comp.PassportId = passportId;
            passport.Comp.IssueYear = issueYear;
            passport.Comp.ExpirationYear = expirationYear;
            passport.Comp.Tampered = true;
            Dirty(passport);
        }

        UpdateUiState(passport);
        _popup.PopupPredicted(Loc.GetString("passport-edit-saved"), passport, args.Actor);
    }

    private void UpdateUiState(Entity<PassportComponent> passport)
    {
        var component = passport.Comp;
        _ui.SetUiState(passport.Owner, PassportUiKey.Key, new PassportBoundUserInterfaceState(
            component.FullName,
            component.Age,
            component.Species,
            component.Sex,
            component.HeightCm,
            component.SkinColor,
            component.EyeColor,
            component.Nationality,
            component.Religion,
            component.PassportId,
            component.IssueYear,
            component.ExpirationYear,
            IsPassportValid(component),
            component.Tampered));
    }

    private void OnUseInHand(Entity<PassportComponent> passport, ref UseInHandEvent evt)
    {
        if (evt.Handled || !_timing.IsFirstTimePredicted)
            return;

        evt.Handled = true;
        passport.Comp.IsClosed = !passport.Comp.IsClosed;
        Dirty(passport);

        var passportEvent = new PassportToggleEvent();
        RaiseLocalEvent(passport, ref passportEvent);
    }

    private static string GenerateIdentityString(string seed)
    {
        var hashCode = seed.GetHashCode();
        System.Random random = new System.Random(hashCode);

        char[] result = new char[17]; // 15 characters + 2 dashes

        int j = 0;
        for (int i = 0; i < 15; i++)
        {
            if (i == 5 || i == 10)
            {
                result[j++] = '-';
            }
            result[j++] = PIDChars[random.Next(PIDChars.Length)];
        }

        return new string(result);
    }

    public static bool IsPassportValid(PassportComponent component)
    {
        return component.Authentic
            && !component.Tampered
            && component.IssueYear is > 0 and <= CurrentYear
            && component.ExpirationYear >= CurrentYear
            && component.Age is > 0 and <= 1000
            && component.HeightCm is > 0 and <= 1000
            && !string.IsNullOrWhiteSpace(component.FullName)
            && !string.IsNullOrWhiteSpace(component.Species)
            && !string.IsNullOrWhiteSpace(component.Sex)
            && !string.IsNullOrWhiteSpace(component.Nationality)
            && PassportIdRegex.IsMatch(component.PassportId)
            && Color.TryFromHex(component.SkinColor, out _)
            && Color.TryFromHex(component.EyeColor, out _);
    }

    private static string Clean(string value, int maxLength = MaxTextFieldLength)
    {
        var cleaned = value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static string CleanColor(string value)
    {
        var cleaned = Clean(value, 16);
        return Color.TryFromHex(cleaned, out var color)
            ? color.ToHexNoAlpha()
            : cleaned;
    }

    private string DisplayOrUnspecified(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Loc.GetString("passport-unspecified")
            : FormattedMessage.EscapeText(value);
    }

    [ByRefEvent]
    public sealed class PassportToggleEvent : HandledEntityEventArgs {}

}
