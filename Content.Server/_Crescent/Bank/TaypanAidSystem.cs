using System.Text.Json;
using Content.Server.Bank;
using Content.Server.CartridgeLoader;
using Content.Server.Preferences.Managers;
using Content.Shared.Bank.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server._Crescent.Bank;

/// <summary>
/// Grants a struggling player aid on their first spawn each round, at most three times per account.
/// </summary>
public sealed class TaypanAidSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    private const long BalanceThreshold = 10_000;
    private const int AidAmount = 25_000;
    private const int MaximumGrants = 3;
    private static readonly ResPath SavePath = new("/taypan_aid.json");

    private readonly HashSet<NetUserId> _checkedThisRound = new();
    private Dictionary<Guid, int> _grants = new();
    private bool _loaded;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        Load();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _checkedThisRound.Clear();
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        // Record even ineligible spawns so spending money and respawning cannot trigger aid mid-round.
        if (!_checkedThisRound.Add(args.Player.UserId) || !_loaded)
            return;

        if (!TryComp<BankAccountComponent>(args.Mob, out var account) || account.Balance >= BalanceThreshold)
            return;

        var userId = args.Player.UserId.UserId;
        var grants = _grants.GetValueOrDefault(userId);
        if (grants >= MaximumGrants)
            return;

        var preferences = _preferences.GetPreferences(args.Player.UserId);
        if (preferences.SelectedCharacter is not HumanoidCharacterProfile profile)
            return;

        // Persist the allowance before paying; a failed write must not enable unlimited grants after restart.
        _grants[userId] = grants + 1;
        if (!Save())
        {
            _grants[userId] = grants;
            return;
        }

        if (!_bank.TryBankDeposit(args.Mob, AidAmount))
        {
            _grants[userId] = grants;
            Save();
            return;
        }

        // Update the profile immediately, including for a player who disconnects before a state send.
        _ = _preferences.SetProfileNoChecks(args.Player.UserId,
            preferences.SelectedCharacterIndex, profile.WithBank(account.Balance));

        if (_inventory.TryGetSlotEntity(args.Mob, "id", out var pda)
            && HasComp<PdaComponent>(pda)
            && TryComp<CartridgeLoaderComponent>(pda, out var loader))
        {
            _cartridgeLoader.SendNotification(pda.Value,
                Loc.GetString("taypan-aid-notification-title"),
                Loc.GetString("taypan-aid-notification-message"), loader);
        }

        Log.Info($"Taypan aid paid {AidAmount} to {args.Player.UserId} ({grants + 1}/{MaximumGrants}).");
    }

    private void Load()
    {
        try
        {
            if (_resources.UserData.TryReadAllText(SavePath, out var json))
            {
                _grants = JsonSerializer.Deserialize<Dictionary<Guid, int>>(json)
                    ?? throw new JsonException("Missing Taypan aid grant counts.");
                if (_grants.Values.Any(count => count < 0 || count > MaximumGrants))
                    throw new JsonException("Invalid Taypan aid grant count.");
            }

            _loaded = true;
        }
        catch (Exception e)
        {
            // Keep aid disabled rather than resetting existing players' allowances on a corrupt file.
            Log.Error($"Failed to load Taypan aid grants: {e}");
        }
    }

    private bool Save()
    {
        try
        {
            _resources.UserData.WriteAllText(SavePath, JsonSerializer.Serialize(_grants));
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save Taypan aid grants: {e}");
            return false;
        }
    }
}
