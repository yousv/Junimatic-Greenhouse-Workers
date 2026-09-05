# Junimatic - Greenhouse Workers

SMAPI addon for [Junimatic](https://www.nexusmods.com/stardewvalley/mods/22672) that automates crop work in the greenhouse (or any indoor location with tilled soil).

## Features

- Harvests, fertilizes, plants, and waters crops automatically.
- Bulk carry: takes all needed seeds/fertilizer in one trip.
- Mini shipping bin support.
- Works in any indoor location. (configurable)
- Multiplayer compatible.

## How It Works

### Pathfinding

8-directional BFS over walkable tiles from the junimo's current position. Each soil tile has up to 8 access points (adjacent walkable tiles). The BFS finds the nearest access point for each task.

### Task Selection

Greedy nearest-neighbor, recomputed per pick via live BFS. Tasks are sorted by type priority first (Harvest > Fertilize > Plant > Water), then BFS distance, then spatial tiebreaking.

### Task Execution

`PathFindController` handles actual movement. A Harmony patch (`PathFindController.isPlayerPresent`) lets junimos use player paths. Another patch (`HoeDirt.isPassable`) lets them walk on tilled soil.

### Harvest Interception

Three Harmony prefixes intercept `addItemToInventoryBool`, `createItemDebris`, and `createObjectDebris` to redirect harvested items to the junimo's carried inventory.

### Network Cache

Each location's soil, chest, and walkable tile layout is cached as a `GreenhouseNetwork`. Rebuilt when terrain or objects change. BFS runs fresh each task pick against this cache.

### Item Return

When no work remains, the junimo walks to the nearest chest, deposits items, then walks to the portal and fades out. Error paths during mid-task deposit instantly.

## Project Structure

```
ModEntry.cs                          Entry point, Harmony setup, console commands
ModConfig.cs                         All config properties

Framework/
  GreenhouseWorkFinder.cs            Main scheduler, BFS, task dispatch, debug overlay
  GreenhouseJunimo.cs                Custom junimo entity, draw, movement, stuck detection
  GreenhouseTaskPlan.cs              Task type enum (Harvest/Fertilize/Plant/Water), PlannedTask
  GreenhouseSoil.cs                  Soil state, access points, fertilizer acceptance
  GreenhouseGameMap.cs               Map walking, portal detection, network building
  GreenhouseNetworks.cs              Per-location network cache, dirty tracking
  GreenhouseChestStorage.cs          Chest access, seed/fertilizer retrieval, mini shipping bin
  ItemHelper.cs                      Color matching, ConsumeOne extension
  JunimoPatches.cs                   Harmony: HoeDirt.isPassable, PathFindController.isPlayerPresent

Compatibility/
  ModConfigMenu.cs                   GMCM registration for all config options
  GreenhouseUnlock.cs                Junimatic quest unlock gating
  JunimaticCompat.cs                 Junimatic event IDs, mod detection
  JunimaticPatches.cs                Harmony: harvest interception (3 prefixes)
```

## Configuration

Requires [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) for in-game config, or edit `config.json` directly.

## Requirements

- [SMAPI](https://www.nexusmods.com/stardewvalley/mods/2400) 4.0.0+
- Stardew Valley 1.6+
- [Junimatic](https://www.nexusmods.com/stardewvalley/mods/22672)

## Build

```
dotnet build JunimaticGreenhouseWorkers.csproj
```

DLL deploys to `Stardew Valley/mods-testing/Junimatic Greenhouse Workers/` via the `DeployToModsTesting` MSBuild target.
