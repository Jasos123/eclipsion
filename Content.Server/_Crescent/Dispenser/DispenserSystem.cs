using Content.Server.Body.Components;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Content.Shared._Crescent.Mind;
using Content.Shared.Body.Part;
using Content.Shared.Crescent.Dispenser;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Popups;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Cargo.Prototypes;
using Content.Server.Cargo.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Crescent.Dispenser;

public sealed class DispenserSystem : SharedDispenserSystem
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItemSystem = default!;
    [Dependency] private readonly StationTradeMarketSystem _marketSystem = default!;
    [Dependency] private readonly Stack.StackSystem _stackSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly DynamicPricingSystem _dynamicPricing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    /// <summary>
    /// Cheapest cargo purchase price per product entity-prototype id, built once from every
    /// <see cref="CargoProductPrototype"/>. Used to cap trade-market payouts so an item bought
    /// from cargo can never be sold back for more than it cost — closing the arbitrage loop.
    /// </summary>
    private Dictionary<string, int>? _cargoBuyPrices;

    public override void Initialize()  
    {  
        base.Initialize();  
        SubscribeLocalEvent<DispenserComponent, ActivateInWorldEvent>(OnActivateInWorld);  
        SubscribeLocalEvent<DispenserComponent, InteractUsingEvent>(OnInteractUsing);  
    }  
  
    private void OnActivateInWorld(EntityUid uid, DispenserComponent component, ActivateInWorldEvent args)  
    {  
        if (args.Handled || component.Dispensing)
            return;

        // Cargo chutes used to spit out a paper trade-deed stub when bumped with an empty hand.
        // Instead of handing out free paper, mock the player for trying to sell their bare hand.
        if (component.DefaultItem == "TradeDeedStub")
        {
            args.Handled = true;
            _popup.PopupEntity(
                Loc.GetString("dispenser-empty-hand-deny"),
                uid, args.User, PopupType.MediumCaution);
            _audioSystem.PlayPvs(component.DenySound, uid);
            return;
        }

        if (!string.IsNullOrEmpty(component.DefaultItem))
        {
            args.Handled = true;
            TryDispenseItem(uid, component, component.DefaultItem);
        }
        else
        {
            _audioSystem.PlayPvs(component.DenySound, uid);
        }
    }
  
    private void OnInteractUsing(EntityUid uid, DispenserComponent component, InteractUsingEvent args)  
    {  
        if (args.Handled || component.Dispensing)  
            return;  
  
        EntityUid used;
        if (TryComp<VirtualItemComponent>(args.Used, out var virtualItem))
            used = virtualItem.BlockingEntity;
        else
            used = args.Used;

        // Modern passports carry structured identity and issuer data. A checker reads that data
        // in place so checking a document no longer destroys it. The legacy prototype mappings
        // below are intentionally left intact for old bare legit/fake passports.
        if (HasComp<PassportCheckerComponent>(uid)
            && TryComp<PassportComponent>(used, out var passport))
        {
            args.Handled = true;
            var valid = SharedPassportSystem.IsPassportValid(passport);
            var popup = passport.Tampered
                ? Loc.GetString("passport-checker-tampered")
                : valid
                    ? Loc.GetString("passport-checker-valid",
                    ("name", FormattedMessage.EscapeText(passport.FullName)),
                    ("id", FormattedMessage.EscapeText(passport.PassportId)),
                    ("year", passport.ExpirationYear))
                    : Loc.GetString("passport-checker-invalid");

            _popup.PopupEntity(popup, uid, args.User,
                valid ? PopupType.Medium : PopupType.MediumCaution);
            _audioSystem.PlayPvs(valid ? component.DispenseSound : component.DenySound, uid);
            return;
        }

        // Check if the dispenser is HuntersBounty and validate the head
        if (TryComp<MetaDataComponent>(uid, out var meta) &&
            meta.EntityPrototype?.ID == "HuntersBounty")
        {
            if (HasComp<BodyPartComponent>(used) && !IsValidBountyHead(used))
            {
                _popup.PopupEntity(
                    Loc.GetString("hunters-bounty-invalid-head"),
                    uid, args.User, PopupType.MediumCaution);
                _audioSystem.PlayPvs(component.DenySound, uid);
                return;
            }
        }

        if (TryComp<DispenserRejectedComponentsComponent>(uid, out var rejector))
        {
            foreach (var name in rejector.RejectedComponents)
            {
                if (!_componentFactory.TryGetRegistration(name, out var registration))
                    continue;

                if (!HasComp(used, registration.Type))
                    continue;

                args.Handled = true;
                _popup.PopupEntity(
                    Loc.GetString(rejector.DenyPopup),
                    uid, args.User, PopupType.MediumCaution);
                _audioSystem.PlayPvs(component.DenySound, uid);
                return;
            }
        }

        if (!TryPrototype(used, out var prototype))
        {
            _audioSystem.PlayPvs(component.DenySound, uid);
            return;
        }

        if (component.DynamicInventory.TryGetValue(prototype.ID, out var baseAmount))  
        {  
            args.Handled = true;  
 
            var stationUid = _marketSystem.TryGetOwningStation(uid);  

            // Get base multiplier from station trade market
            float marketMultiplier = stationUid.HasValue  
                ? _marketSystem.GetPriceMultiplier(stationUid.Value, prototype.ID)  
                : 1.0f;  

            // Apply dynamic pricing multiplier
            float dynamicMultiplier = _dynamicPricing.GetPriceMultiplier(prototype.ID);
            
            // Combine both multipliers
            float finalMultiplier = marketMultiplier * dynamicMultiplier;
  
            int finalAmount = (int)MathF.Round(baseAmount * finalMultiplier);

            // Anti-arbitrage: an item that can be bought from cargo must never sell back for more
            // than its cargo purchase price, no matter how high the dynamic multiplier climbs.
            // Only the sale value is capped here; the station tax below still applies on top.
            if (GetCargoBuyPrice(prototype.ID) is { } buyPrice)
                finalAmount = Math.Min(finalAmount, buyPrice);

            if (stationUid.HasValue)
                _marketSystem.RecordSale(stationUid.Value, prototype.ID);

            // Record transaction in dynamic pricing system
            _dynamicPricing.RecordTransaction(prototype.ID, 1, isBuy: false);

            // Apply the station/faction tax. The base sale value stays fixed; the faction
            // simply takes a percentage cut of the payout into its treasury.
            float taxRate = stationUid.HasValue
                ? _marketSystem.GetTaxRate(stationUid.Value, prototype.ID)
                : 0f;
            int taxAmount = (int)MathF.Round(finalAmount * taxRate);
            int payout = Math.Max(0, finalAmount - taxAmount);

            if (stationUid.HasValue && taxAmount > 0)
                _marketSystem.AddTreasury(stationUid.Value, taxAmount);

            int pct = (int)MathF.Round(finalMultiplier * 100f);
            int taxPct = (int)MathF.Round(taxRate * 100f);
            _popup.PopupEntity(
                Loc.GetString("rat-station-trade-market",
                    ("finalAmount", payout),
                    ("pct", pct),
                    ("baseAmount", baseAmount),
                    ("taxAmount", taxAmount),
                    ("taxPct", taxPct)),
                uid, args.User, PopupType.Medium);

            component.PendingDynamicAmount = payout;
            TryDispenseItem(uid, component, string.Empty);
  
            if (virtualItem != null)  
                _virtualItemSystem.DeleteVirtualItem((args.Used, virtualItem), args.User);  
            QueueDel(used);  
            return;  
        } 
        if (TryGetDispenseItem(component, prototype.ID, out string itemId))  
        {  
            args.Handled = true;  
            TryDispenseItem(uid, component, itemId);  
  
            if (virtualItem != null)  
                _virtualItemSystem.DeleteVirtualItem((args.Used, virtualItem), args.User);  
            QueueDel(used);  
        }  
        else  
        {  
            _audioSystem.PlayPvs(component.DenySound, uid);  
        }  
    }  
  
    /// <summary>
    /// Returns the cheapest cargo purchase price for the given product entity-prototype id, or
    /// <c>null</c> if the item is not sold by cargo. The lookup is built lazily on first use and
    /// cached, since cargo product prototypes are static for the lifetime of the process.
    /// </summary>
    private int? GetCargoBuyPrice(string prototypeId)
    {
        if (_cargoBuyPrices == null)
        {
            _cargoBuyPrices = new Dictionary<string, int>();
            foreach (var product in _prototypeManager.EnumeratePrototypes<CargoProductPrototype>())
            {
                if (product.Abstract || product.Cost <= 0 || string.IsNullOrEmpty(product.Product.Id))
                    continue;

                var id = product.Product.Id;
                if (!_cargoBuyPrices.TryGetValue(id, out var existing) || product.Cost < existing)
                    _cargoBuyPrices[id] = product.Cost;
            }
        }

        return _cargoBuyPrices.TryGetValue(prototypeId, out var cost) ? cost : null;
    }

    public bool TryGetDispenseItem(DispenserComponent component, string itemId, out string dispenseItemId)
    {  
        if (string.IsNullOrEmpty(itemId))  
        {  
            dispenseItemId = string.Empty;  
            return false;  
        }  
  
        foreach (var kvp in component.Inventory)  
        {  
            if (kvp.Key == itemId)  
            {  
                dispenseItemId = kvp.Value;  
                return !string.IsNullOrEmpty(dispenseItemId);  
            }  
        }  
  
        dispenseItemId = string.Empty;  
        return false;  
    }  
  
    public void TryDispenseItem(EntityUid uid, DispenserComponent component, string itemId)  
    {  
        component.Dispensing = true;  
        component.DispensingItemId = itemId;  
        component.DispenseTimer = 0f;  
  
        _audioSystem.PlayPvs(component.DispenseSound, uid);  
    }  
  
    public void Dispense(EntityUid uid, DispenserComponent component, string itemId)  
    { 
        if (component.PendingDynamicAmount > 0)  
        {  
            _stackSystem.SpawnMultiple("SpaceCash", component.PendingDynamicAmount, Transform(uid).Coordinates);  
            component.PendingDynamicAmount = 0;  
            return;  
        }  

        if (!string.IsNullOrEmpty(itemId))  
            Spawn(itemId, Transform(uid).Coordinates);  
    }  
  
    /// <summary>
    ///     Checks if the given entity is a valid severed head with HadMindComponent.
    ///     A valid head must:
    ///     1. Have a BodyPartComponent with PartType = Head
    ///     2. Not be attached to a body (Body is null)
    ///     3. Have HadMindComponent (was once player-controlled)
    /// </summary>
    private bool IsValidBountyHead(EntityUid entity)
    {
        // Must have BodyPartComponent and be a Head
        if (!TryComp<BodyPartComponent>(entity, out var bodyPart) ||
            bodyPart.PartType != BodyPartType.Head)
        {
            return false;
        }

        // Must be detached from a body (severed)
        if (bodyPart.Body != null)
        {
            return false;
        }

        // Must have had a mind at some point (was player-controlled)
        if (!HasComp<HadMindComponent>(entity))
        {
            return false;
        }

        return true;
    }

    public override void Update(float frameTime)  
    {  
        base.Update(frameTime);  
  
        var query = EntityQueryEnumerator<DispenserComponent>();  
        while (query.MoveNext(out var uid, out var component))  
        {  
            if (!component.Dispensing)  
                continue;  
  
            component.DispenseTimer += frameTime;  
            if (component.DispenseTimer >= component.DispenseTime)  
            {  
                component.DispenseTimer = 0f;  
                component.Dispensing = false;  
  
                Dispense(uid, component, component.DispensingItemId);  
            }  
        }  
    }  
}
