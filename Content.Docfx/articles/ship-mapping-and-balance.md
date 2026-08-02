# Ship Mapping and Balance

Use power, fuel, hardpoints, armour, mobility and role together when balancing a hull.

## Faction doctrine

- DSM: AME power, fewer energy weapons, high burst damage and high fuel cost.
- NCWL: boriatic power, more ballistic or missile hardpoints and ammunition logistics.

## Engine selection

### Boriatic generators

| Hull class | Prototype | Output |
| --- | --- | ---: |
| Small | `BoriaticGeneratorHerculesShuttle` | 50 kW |
| Medium | `BoriaticGeneratorEinsteinShuttle` | 95 kW |
| Large | `BoriaticGeneratorTitanShuttle` | 200 kW |
| Capital | Multiple generators | - |

### Antimatter engine

An AME is an `AmeController` surrounded by `AmeShielding`. A solid `r x c` shielding block yields
`(r - 2) x (c - 2)` cores.

```text
power = cores^2 * (injection + 1) * 5 kW
        * ((injection - cores) / 3 + 1) when injection > cores
        + 120 kW baseline
safe injection = floor(cores * 2.5)
fuel burn = injection units per 10 seconds
```

| Cores | Inj 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 10 | 12 | 15 | Safe max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 130 | 135 | - | - | - | - | - | - | - | - | - | **135** |
| 2 | 160 | 180 | 200 | 220 | 360 | - | - | - | - | - | - | **360** |
| 3 | 210 | 255 | 300 | 345 | 390 | 750 | 840 | - | - | - | - | **840** |
| 4 | 280 | 360 | 440 | 520 | 600 | 680 | 1,400 | 1,560 | 2,760 | - | - | **2,760** |
| 5 | 370 | 495 | 620 | 745 | 870 | 995 | 1,120 | 2,370 | 2,870 | 4,995 | - | **4,995** |
| 6 | 480 | 660 | 840 | 1,020 | 1,200 | 1,380 | 1,560 | 1,740 | 4,080 | 7,140 | 11,640 | **11,640** |

Reserve four or more cores for stations.

### Fuel endurance

A standard AME jar holds 1,000 units. There is no separate AME canister.

| Injection | 2 | 3 | 4 | 5 | 6 | 7 | 10 | 15 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Jar life | 83 min | 56 min | 42 min | 33 min | 28 min | 24 min | 17 min | 11 min |

## AME sizing

| Class | Cores | Shielding | Cruise injection | Cruise power | Safe maximum | LPC band |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Corvette, frigate | 1 | 3x3 | 1-2 | 130-135 kW | 135 kW | 18-60 kW |
| Destroyer | 2 | 3x4 | 2-3 | 180-200 kW | 360 kW | 58-100 kW |
| Cruiser, battlecruiser | 3 | 3x5 | 3-4 | 300-345 kW | 840 kW | 85-230 kW |
| Battleship, capital | 3 + Titan | 3x5 + Titan | 4 | 545 kW | 1,040 kW | 125-350 kW |

Three AME cores is the maximum for a ship. Use boriatic auxiliary power for larger hulls.

## Weapon budget

Weapons may use at most 40 percent of cruise power.

| Class | Cruise | Weapon budget | DSM example | NCWL example |
| --- | ---: | ---: | --- | --- |
| Corvette | 135 kW | 54 kW | 2 small energy | 4 small ballistic + 2 missile |
| Frigate | 200 kW | 80 kW | 2 medium energy | 3 medium ballistic + 2 missile |
| Destroyer | 345 kW | 138 kW | 2 large energy + 1 small | 4 medium ballistic + 1 large |
| Cruiser | 600 kW | 240 kW | 4 large energy | 6 large ballistic |
| Battlecruiser | 995 kW | 398 kW | 6 large energy + 2 small | 8 large ballistic + 3 missile |
| Battleship | 1,560 kW | 624 kW | 8 large energy + 4 medium | 8 large ballistic + 6 medium |

| Weapon class | Sustained draw | Minimum hull |
| --- | ---: | --- |
| Very light point defence | 3-6 kW | Corvette |
| Small or medium missile | 3-5 kW | Corvette |
| Small ballistic | 6 kW | Corvette |
| Medium ballistic | 10 kW | Frigate |
| Small energy | 20 kW | Corvette with AME |
| Large ballistic | 16 kW | Destroyer |
| Medium energy | 35 kW | Frigate with 2 cores |
| Large missile | 8 kW | Destroyer |
| Large energy | 55 kW | Destroyer with 3 cores |

One large energy weapon requires one AME core.

## Fuel storage

| Hull class | Boriatic containers | Antimatter |
| --- | --- | --- |
| Small | Tier 1 | One jar |
| Medium | Mostly tier 2 | One jar; spare for long patrols |
| Large | Tier 2 and tier 3 | Two jars and a refill tank |
| Capital | Tier 3 and refill tanks | Three or more jars and tanks |

## Hardpoints

| Hull class | Main | Point defence | Missile |
| --- | ---: | ---: | ---: |
| Corvette | 1-2 | 1 | Optional |
| Frigate | 2-3 | 2 | 1 |
| Destroyer | 3-4 | 2-3 | 1-2 |
| Cruiser | 4-6 | 4 | 2 |
| Battlecruiser | 5-7 | 4-6 | 2-3 |
| Battleship | 6-8 | 6+ | 3-4 |

## Mapping checks

- Keep weapon draw within the cruise-power budget.
- Give NCWL hulls ammunition storage and DSM hulls enough AME fuel.
- Keep small hulls agile and large hulls slower to accelerate and turn.
- Include engineering access, power distribution and damage-control routes.
- Do not give a hull top-tier firepower, endurance, mobility, armour and cargo at the same time.
