# Persistent capture regions

A persistent capture region turns any mapped installation into ground the four major powers can take from each
other, and keeps the result across round restarts and server restarts. Take a mining outpost as DSM and it becomes
"DSM Fenwick Extraction" on radar in DSM colours, its point-defence console answers only to DSM crew, and its
anti-boarder turrets shoot everyone DSM is hostile to. Lose it and all of that goes with it.

Ownership lives in `capture_regions.json` in the server's user data, keyed by region ID. The map file is never
rewritten.

## Mapping one

Everything below is in the mapping template list under Crescent hardpoints and consoles.

1. **Put the grid together first and name it.** The grid's name is the territory's name with nobody holding it.
   Ownership is displayed by prefixing it, so name it `Fenwick Extraction`, not `DSM Fenwick Extraction`.

2. **Place one `PersistentCaptureRegionFlag`** on that grid, somewhere a boarding party can physically stand for
   the better part of a minute. Give it a `regionId` that is unique across every map on the server and that you
   will never change:

   ```yaml
   - type: PersistentCaptureRegion
     regionId: fenwick-extraction
   ```

   The region ID is the save key. Renaming it orphans the saved owner and the territory silently reverts to
   whatever the map says. A flag left on the `REPLACE_WITH_UNIQUE_REGION_ID` placeholder, a duplicate ID, a flag
   not on a grid, or a second flag on a grid that already has one is rejected at map load with an error in the
   server log, and that flag does nothing at all for the rest of the round.

3. **Place the defences.** Both of these take their faction from the flag, on every map load and on every capture,
   so never map the faction-specific versions inside a capturable region.

   - `WeaponTurretAutoPDCapturable` for anti-boarder turrets. Unowned, it has no factions and shoots nobody.
   - `ComputerTargetingCapturable` for the point-defence console, plus ordinary hardpoints and ship guns for it to
     control. Unowned, the console is locked to everyone.

4. **Optionally give it a starting owner** by setting `ownerTeam` on the flag's `CaptureFlag` component. That
   applies at map load but is not written to the save, so the map stays the source of truth until a player takes
   the region for the first time.

A territory that spans several grids sets `regionId` explicitly on the devices on the other grids:

```yaml
- type: CaptureRegionDevice
  regionId: fenwick-extraction
```

Only the flag's own grid is renamed and recoloured on radar. Devices bound across grids still change hands.

## Who can take it

DSM, NCWL, TFSC (the TFCF's ID) and SHI, and nobody else. The restriction is not cosmetic: each of those four has
an `npcFaction` prototype for the turrets to join and a banner sprite state for the flag to fly. ATH, TAP, TSP,
SRM and IND characters are ignored by the flag entirely - they neither capture nor contest, and they cannot use a
captured console. Adding a fifth power means adding its `npcFaction` prototype, a banner state in
`medieval_pole_banners.rsi`, an entry in `PersistentTerritoryFactions`, and radar and flag colours. For its
batteries to know who to shoot it also needs a `diplomacy` prototype and a place on `RatDiplomacySystem`'s
roster, and for the territory to move its stock price, a row in `FactionStockCompanies`.

Capture needs a living body. Corpses and crit do not hold ground. Two factions standing on it at once contest it
and neither makes progress. Partial progress belongs to the faction that earned it, so a faction that walks in
after another leaves starts from nothing rather than finishing someone else's work.

Taking held ground is two stages, neutralise then capture, at the times on the flag's `CaptureFlag` component.

## The point-defence console

`ComputerTargetingCapturable` is an ordinary targeting console with two additions.

**Access.** Its `FactionMachine` is restricted, so only members of the holding faction can open it or send it a
message. An unowned territory's console is locked to everybody rather than open to everybody, and a capture closes
the window on anyone from the previous owner still standing at it.

**Automatic guard mode** (`TerritoryAutoDefense`). With nobody at the console it acquires and fires on its own,
and stands down the instant an operator opens the UI. Two consoles on one grid never both run - the lower entity
ID takes it. Defaults on the prototype:

| Field                   | Default | Meaning                                                      |
|-------------------------|---------|--------------------------------------------------------------|
| `range`                 | 512     | Acquisition range, matched to the console's own radar range   |
| `neutralEngagementRange`| 384     | Exclusion zone, only used when `warOnly` is off               |
| `scanInterval`          | 1       | Seconds between target searches and cannon relinks            |
| `warOnly`               | true    | Engage only declared wars, and ignore everything else         |

With `warOnly` on, the battery shoots hulls whose faction is at war with the holder and ignores the rest silently.
Turning it off makes the territory hostile ground for all non-allies: anything crossing `neutralEngagementRange`
is fired on, and traffic between there and `range` gets a radio warning naming the clearance it must keep.

The battery holds fire when a grid that is not at war with the holder is in the line of shot, and only fires guns
that can actually bear - a welded mount has to already be pointing at the target, a turret swings onto it.

## Admin console

```
territory list                     every region in the save or on the current map
territory set <regionId> <faction>  hand a region to DSM, NCWL, TFSC or SHI
territory clear <regionId>          return a region to nobody
territory forget <regionId>         drop the save row, so the map's own owner applies next load
```

`set` and `clear` work whether or not the region's map is loaded, and take effect immediately when it is.
