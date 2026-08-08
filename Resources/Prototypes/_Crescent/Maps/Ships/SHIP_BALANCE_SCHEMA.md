# Crescent Ship Balance Reference

Reference values for mapping Crescent ships. The figures come from the current prototypes and power code.

## Faction limits

- NCWL ships use boriatic generators, ballistic weapons and missiles. NCWL hulls should not have AMEs.
- DSM ships use AMEs and energy weapons. Generators are allowed only on ultralights and fighters.
- Ship AMEs are limited to six cores.

## Power accounting

Ship weapons use `ApcPowerReceiverBattery`; shield emitters use `ApcPowerReceiver`.

```
weapon grid demand = idleLoad + batteryRechargeRate
shield grid demand = powerLoad
```

Components with `ApcPowerReceiverBattery` override `ApcPowerReceiver.powerLoad`. Set a weapon's idle draw with `idleLoad`. Shield emitters have no battery, so they use `powerLoad` directly.

Power checks are all or nothing. If the grid cannot meet a device's full request, a weapon stops charging and a shield drops. Build each power grid for peak demand, not average draw.

## Generation

### Boriatic generators

| Generator | Output | Prototype |
|---|---:|---|
| Hercules | 80 kW | `BoriaticGeneratorHerculesShuttle` |
| Einstein | 95 kW | `BoriaticGeneratorEinsteinShuttle` |
| Titan | 200 kW | `BoriaticGeneratorTitanShuttle` |

### Antimatter engines

An AME core is an `AmeShielding` block with all eight neighbours occupied. For an M x N rectangle:

```
cores = (M - 2) * (N - 2)
```

| Strip | Blocks | Cores |
|---|---:|---:|
| 3x4 | 12 | 2 |
| 3x5 | 15 | 3 |
| 3x6 | 18 | 4 |
| 3x7 | 21 | 5 |
| 3x8 | 24 | 6 |
| 4x5 | 20 | 6 |
| 5x5 | 25 | 9, station only |

Add one `AmeController` next to the shielding. Output is calculated as follows:

```
P = cores^2 * (fuel + 1) * 5000 * k + 120000
k = floor((fuel - cores) / 3) + 1 when fuel > cores, otherwise 1
safe fuel limit = 2 * cores + cores / 2
```

Injection runs every 10 seconds.

| Cores | Strip | Cruise, f=C | Battle, f=2C | Safe maximum | Fuel/min at maximum |
|---:|---|---:|---:|---:|---:|
| 2 | 3x4 | 180 kW | 220 kW | 360 kW | 30 |
| 3 | 3x5 | 300 kW | 750 kW | 840 kW | 42 |
| 4 | 3x6 | 520 kW | 1,560 kW | 2,760 kW | 60 |
| 5 | 3x7 | 870 kW | 2,870 kW | 4,995 kW | 72 |
| 6 | 3x8 | 1,380 kW | 7,140 kW | 11,640 kW | 90 |

Values above the safe fuel limit damage the cores.

## Device loads

### Weapons

| Class | Size | `idleLoad` | `batteryRechargeRate` | Firing demand |
|---|---|---:|---:|---:|
| Energy | Small | 10,000 | 90,000 | 100 kW |
| Energy | Medium | 30,000 | 320,000 | 350 kW |
| Energy | Large | 80,000 | 820,000 | 900 kW |
| Ballistic | Small | 1,500 | 12,000 | 13.5 kW |
| Ballistic | Medium | 2,000 | 20,000 | 22 kW |
| Ballistic | Large | 3,000 | 32,000 | 35 kW |
| Missile | Small | 1,000 | 6,000 | 7 kW |
| Missile | Medium | 1,500 | 10,000 | 11.5 kW |
| Missile | Large | 2,500 | 16,000 | 18.5 kW |

A turret only returns to its idle load after its battery is full. Ballistic and missile batteries recharge faster than they drain by design, keeping ammunition as their main limit.

### Shields

| Emitter | Tier | HP | Overload | `powerLoad` | Allowed hulls |
|---|---:|---:|---:|---:|---|
| `ShieldEmitterSmall` "Errant" | 1 | 7,500 | 30 s | 40 kW | T2-T3, both factions |
| `ShieldEmitterMedium` "Bulwark" | 2 | 15,000 | 60 s | 150 kW | T4-T5, both factions |
| `ShieldEmitter` "Goliath" | 3 | 30,000 | 120 s | 500 kW | T6-T7, DSM only |

Use `ApcPowerReceiver.powerLoad` for shield draw. `ShipShieldEmitter.powerDraw` is not currently applied.

### Hull overhead

- Thruster: 1.5 kW each
- Gyroscope: 1.5 kW each
- Lighting and basic services: about 10 kW per hull

## Energy weapon burst timing

Energy turrets fire one magazine and then wait for `burstCooldown`. The battery and magazine should both refill during that interval.

| Size | Burst | Lockout | Duty cycle | Battery |
|---|---:|---:|---:|---:|
| Small | 5 s | 5 s | 50% | 450,000 J |
| Medium | 4 s | 8 s | 33% | 2,560,000 J |
| Large | 3 s | 12 s | 20% | 9,840,000 J |

```
shotsPerBurst = burstFireRate * burstSeconds
battery = batteryRechargeRate * lockout
energyPerShot = battery / shotsPerBurst
rechargeCooldown = lockout / shotsPerBurst
```

| Weapon | Size | Burst | Lockout | Energy/shot | Ammo cooldown |
|---|---|---:|---:|---:|---:|
| Starburst | S | 50 at 10/s | 5 s | 9,000 | 0.10 s |
| Retribution Navy | S | 40 at 8/s | 5 s | 11,250 | 0.125 s |
| Torch | S | 40 at 8/s | 5 s | 11,250 | 0.125 s |
| Retribution | S | 30 at 6/s | 5 s | 15,000 | 0.167 s |
| Komodo | S | 15 at 3/s | 5 s | 30,000 | 0.333 s |
| Plasma repeater | S | 6 at 1.2/s | 5 s | 75,000 | 0.833 s |
| Absolution Navy | M | 96 at 24/s | 8 s | 26,667 | 0.083 s |
| Absolution | M | 64 at 16/s | 8 s | 40,000 | 0.125 s |
| Compakt | M | 48 at 12/s | 8 s | 53,333 | lens |
| Solaris | M | 14 at 3.5/s | 8 s | 182,857 | canister |
| Bolter | M | 10 at 2.5/s | 8 s | 256,000 | 0.8 s |
| Bizmuth | M | 10 at 2.4/s | 8 s | 256,000 | 0.8 s |
| Rimward | M | 7 at 1.7/s | 8 s | 365,714 | 1.143 s |
| Curse Navy | M | 3 at 0.5/s | 8 s | 853,333 | 2.667 s |
| Curse | M | 3 at 0.4/s | 8 s | 853,333 | 2.667 s |
| Damnation Navy | L | 9 at 3/s | 12 s | 1,093,333 | 1.333 s |
| Damnation | L | 8 at 2.5/s | 12 s | 1,230,000 | 1.5 s |
| Mauler | L | 3 at 1/s | 12 s | 3,280,000 | 4 s |
| Hardliner | L | 3 at 0.7/s | 12 s | 3,280,000 | power cage |

### Damage audit

Curse, Curse Navy and Bizmuth rely on EMP effects, while Damnation and Mauler rely partly on explosions. Direct DPS alone is not a useful comparison for those weapons.

| Weapon | Size | Damage/shot | Burst DPS | Sustained DPS | Draw |
|---|---|---:|---:|---:|---:|
| Torch | S | 403 | 3,224 | 1,612 | 100 kW |
| Komodo | S | 315 | 945 | 473 | 100 kW |
| Starburst | S | 90 | 900 | 450 | 100 kW |
| Retribution Navy | S | 100 | 800 | 400 | 100 kW |
| Retribution | S | 100 | 600 | 300 | 100 kW |
| Absolution Navy | M | 220 | 5,280 | 1,742 | 350 kW |
| Absolution | M | 220 | 3,520 | 1,162 | 350 kW |
| Bolter | M | 420 | 1,050 | 347 | 350 kW |
| Rimward | M | 435 | 740 | 244 | 350 kW |
| Mauler | L | 2,300 plus explosion | 2,300 | 460 | 900 kW |
| Damnation | L | 200 plus explosion | 500 | 100 | 900 kW |

The Torch currently outperforms medium and large direct-damage weapons despite using a small hardpoint. Damnation's direct damage is also low for a large mount. Treat both as weapon-balance issues rather than power-budget issues.

## Hull tiers

Hardpoint counts are maximums.

| Tier | Designations | Crew | Hardpoints | Thrusters | Hull overhead |
|---:|---|---:|---|---:|---:|
| 0 | ultralight, drone | 0-1 | 2 S | 6 | 21 kW |
| 1 | fighter, interceptor | 1-2 | 3 S | 8-10 | 24 kW |
| 2 | gunship, bomber, corvette | 2-5 | 4 S + 1 M | 10-14 | 31 kW |
| 3 | frigate, assault corvette | 5-8 | 4 S + 2 M | 14-16 | 36 kW |
| 4 | destroyer, artillery ship | 8-12 | 4 S + 3 M + 1 L | 18-22 | 45 kW |
| 5 | cruiser | 12-18 | 6 S + 4 M + 2 L | 20-24 | 49 kW |
| 6 | battlecruiser, artillery carrier | 18-25 | 8 S + 4 M + 3 L | 28-32 | 63 kW |
| 7 | battleship, flagship | 25+ | 10 S + 6 M + 4 L | 38-42 | 79 kW |

## NCWL build sheet

NCWL hulls use ballistic and missile weapons. Shields stop at tier 2.

| Tier | Loadout | Guns | Shield | Hull | Total | Plant |
|---:|---|---:|---:|---:|---:|---|
| 0 | 2x Ballistic S | 27 | - | 21 | 48 kW | 1x Hercules |
| 1 | 2x Ballistic S + 1x Missile S | 34 | - | 24 | 58 kW | 1x Hercules |
| 2 | 4x Ballistic S + 1x Missile M | 66 | 40 | 31 | 137 kW | 2x Hercules |
| 3 | 4x Ballistic S + 2x Ballistic M | 98 | 40 | 36 | 174 kW | 2x Einstein |
| 4 | 4x Ballistic S + 3x Ballistic M + 1x Ballistic L | 155 | 150 | 45 | 350 kW | 2x Titan |
| 5 | 6x Ballistic S + 4x Ballistic M + 2x Ballistic L | 239 | 150 | 49 | 438 kW | 3x Titan |
| 6 | 8x Ballistic S + 4x Ballistic M + 3x Ballistic L | 301 | 150 | 63 | 514 kW | 3x Titan |
| 7 | 10x Ballistic S + 6x Ballistic M + 4x Ballistic L | 407 | 150 | 79 | 636 kW | 4x Titan |

Leave room for magazines, loading access and damage control.

## DSM build sheet

At least 60 percent of DSM hardpoints should be energy weapons. Missiles are acceptable filler; avoid ballistics above tier 2.

| Tier | Cores | Strip | Loadout | Guns | Shield | Hull | Total | Required setpoint |
|---:|---:|---|---|---:|---:|---:|---:|---|
| 0 | none | - | 1x Energy S | 100 | - | 21 | 121 kW | 2x Hercules |
| 1 | none | - | 2x Energy S | 200 | - | 24 | 224 kW | Titan + Hercules |
| 2 | 3 | 3x5 | 4x Energy S | 400 | 40 | 31 | 471 kW | battle |
| 3 | 4 | 3x6 | 4x Energy S + 2x Energy M | 1,100 | 40 | 36 | 1,176 kW | battle |
| 4 | 5 | 3x7 | 4x Energy S + 2x Energy M + 1x Energy L + 1x Missile M | 2,012 | 150 | 45 | 2,207 kW | battle |
| 5 | 5-6 | 3x7 or 3x8 | 6x Energy S + 3x Energy M + 2x Energy L + 1x Missile M | 3,462 | 150 | 49 | 3,661 kW | 5-core maximum or 6-core battle |
| 6 | 6 | 3x8 | 8x Energy S + 4x Energy M + 3x Energy L | 4,900 | 500 | 63 | 5,463 kW | battle |
| 7 | 6 | 3x8 | 8x Energy S + 2x Missile S + 6x Energy M + 4x Energy L | 6,514 | 500 | 79 | 7,093 kW | battle |

### DSM idle load

| Tier | Cores | Idle draw | Cruise output | Firing draw | Battle output |
|---:|---:|---:|---:|---:|---:|
| 2 | 3 | 111 kW | 300 kW | 471 kW | 750 kW |
| 3 | 4 | 176 kW | 520 kW | 1,176 kW | 1,560 kW |
| 4 | 5 | 375 kW | 870 kW | 2,207 kW | 2,870 kW |
| 5 | 6 | 509 kW | 1,380 kW | 3,661 kW | 7,140 kW |
| 6 | 6 | 1,003 kW | 1,380 kW | 5,463 kW | 7,140 kW |
| 7 | 6 | 1,159 kW | 1,380 kW | 7,093 kW | 7,140 kW |

DSM hulls can maintain idle systems at cruise injection but need a higher setpoint to fire. A SMES bank helps cover the first broadside while the reactor ramps up.

## Mapping checklist

1. Choose the tier from `VesselDesignation` and stay within its hardpoint and thruster limits.
2. Use only boriatic generators on NCWL ships.
3. Use the listed AME core count on DSM ships. Never exceed six ship cores.
4. Use tier 3 shields only on DSM tier 6-7 hulls.
5. Add weapon firing demand, shield load and hull overhead. Compare the total against peak generation.
6. Provide ammunition access on NCWL hulls and AME fuel access on DSM hulls.
7. Leave engineering and damage-control routes around power equipment.

## Current limitations

- AME output jumps sharply between core counts, especially from three to four cores.
- A 10,000-unit AME jar lasts about 1.9 hours at a six-core safe maximum, so fuel is a weak balance constraint.
- AMEs burn fuel according to their setpoint, not actual grid load.
- Brownouts disable individual devices instead of reducing all output proportionally.
