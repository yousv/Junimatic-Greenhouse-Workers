using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseWorkFinder
    {
        private const int MaxActionsPerGameMinute = 2;
        private const int BoardStaleLimit = 20;
        private static readonly Color DebugHarvestColor = new Color(0, 100, 0);
        private static readonly Color DebugFertilizeColor = new Color(200, 150, 0);
        private static readonly Color DebugPlantColor = new Color(100, 255, 100);
        private static readonly Color DebugWaterColor = new Color(100, 180, 255);

        private readonly ModEntry mod;
        private readonly List<GreenhouseJunimo> activeWorkers = new List<GreenhouseJunimo>();
        private readonly Dictionary<GameLocation, GreenhouseNetworks> boards = new Dictionary<GameLocation, GreenhouseNetworks>();
        private int lastSeedCacheTick = -1;
        private readonly Dictionary<Point, string> cachedSeedIds = new Dictionary<Point, string>();
        private int timeOfDayAtLastCheck = -1;
        private int actionsAtThisTime;
        private int boardStaleCounter;

        public GreenhouseWorkFinder(ModEntry mod)
        {
            this.mod = mod;
        }

        public void Entry()
        {
            this.mod.Helper.Events.GameLoop.OneSecondUpdateTicked += this.OneSecondUpdateTicked;
            this.mod.Helper.Events.GameLoop.UpdateTicking += this.UpdateTicking;
            this.mod.Helper.Events.Player.Warped += this.PlayerWarped;
            this.mod.Helper.Events.GameLoop.DayEnding += this.DayEnding;
            this.mod.Helper.Events.GameLoop.Saving += this.Saving;
            this.mod.Helper.Events.GameLoop.SaveLoaded += this.SaveLoaded;
            this.mod.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            this.mod.Helper.Events.World.ObjectListChanged += this.OnWorldChanged;
            this.mod.Helper.Events.World.TerrainFeatureListChanged += this.OnTerrainChanged;
            this.mod.Helper.Events.World.BuildingListChanged += this.OnWorldChanged;
            this.mod.Helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        }

        // the worker gave up (broken soil, missing seed); drop what it carries, emote, walk home and wait
        private void QuitInDisgust(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles)
        {
            ModEntry.LogJunimo($"{junimo.Name} disgusted at {soil.Tile.X},{soil.Tile.Y}; skipping.");
            junimo.DoDisgustEmote();
            handledTiles.UnionWith(soil.AdjacentAccessPoints);
            if (!this.mod.Config.NeverDropItems)
            {
                var drops = new List<Item>();
                if (junimo.CarriedSeed is not null)
                    drops.Add(junimo.CarriedSeed.ConsumeOne());
                if (junimo.CarriedFertilizer is not null)
                    drops.Add(junimo.CarriedFertilizer.ConsumeOne());
                if (drops.Count > 0)
                    DropAsDebris(junimo, drops.Where(d => d is not null).ToList());
            }
            this.ScheduleNext(junimo, network, handledTiles);
        }

        // deposit carried items into chests, drop anything that does not fit
        private void ReturnCarried(GreenhouseJunimo junimo, GreenhouseNetwork network)
        {
            var items = CollectCarried(junimo);
            if (items.Count == 0)
                return;
            ModEntry.LogJunimo($"{junimo.Name} ReturnCarried: depositing {items.Count} item(s) [{string.Join(", ", items.Select(i => $"{i.QualifiedItemId} x{i.Stack}"))}] to chests.");
            var left = this.TryDepositAll(network, items);
            if (left.Count > 0)
                ModEntry.LogJunimo($"{junimo.Name} ReturnCarried: {left.Count} leftover(s) dropped as debris [{string.Join(", ", left.Select(i => $"{i.QualifiedItemId} x{i.Stack}"))}].");
            DropAsDebris(junimo, left);
        }

        private Point FindNearestChestAccess(GreenhouseJunimo junimo, GreenhouseNetwork network)
        {
            Point best = network.Chests[0].AccessPoint;
            int bestDist = Math.Abs(junimo.CurrentTile.X - best.X) + Math.Abs(junimo.CurrentTile.Y - best.Y);
            for (int i = 1; i < network.Chests.Count; i++)
            {
                var ap = network.Chests[i].AccessPoint;
                int d = Math.Abs(junimo.CurrentTile.X - ap.X) + Math.Abs(junimo.CurrentTile.Y - ap.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = ap;
                }
            }
            return best;
        }

        private void WalkBackToChestAndReturn(GreenhouseJunimo junimo, GreenhouseNetwork network, Action onReturn)
        {
            if (junimo.CarriedSeed is null && junimo.CarriedFertilizer is null && (junimo.CarriedHarvest is null || junimo.CarriedHarvest.Count == 0))
            {
                onReturn();
                return;
            }
            var chestAccess = this.FindNearestChestAccess(junimo, network);
            ModEntry.LogJunimo($"{junimo.Name} walking back to chest at {chestAccess.X},{chestAccess.Y} to return items.");
            junimo.Enqueue(chestAccess, () =>
            {
                this.ReturnCarried(junimo, network);
                onReturn();
            });
        }

        private void UpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.eventUp)
                return;
            var time = Game1.currentGameTime;
            if (time is null)
                return;

            foreach (var junimo in this.activeWorkers)
            {
                if (junimo.IsDestroying)
                    continue;

                var loc = junimo.currentLocation;
                if (loc is null)
                    continue;

                if (!loc.characters.Contains(junimo))
                    loc.characters.Add(junimo);

                if (loc.IsActiveLocation())
                    continue;

                junimo.update(time, loc);
            }
        }

        private void PlayerWarped(object sender, WarpedEventArgs e)
        {
            if (e.OldLocation is null || e.OldLocation.Name != "Greenhouse")
                return;

            ModEntry.LogJunimo($"PlayerWarped: left Greenhouse (new={e.NewLocation?.Name}). {this.activeWorkers.Count} worker(s) persist.");
        }

        private void Saving(object sender, SavingEventArgs e)
        {
            ModEntry.LogJunimo("Saving event: ClearAll().");
            this.ClearAll();
        }

        private void SaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            ModEntry.LogJunimo("SaveLoaded event: ClearAll().");
            this.ClearAll();
        }

        private void DayEnding(object sender, DayEndingEventArgs e)
        {
            ModEntry.LogJunimo("DayEnding event: ClearAll().");
            this.ClearAll();
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            this.MarkBoardsDirty();
        }

        private void OnWorldChanged(object sender, ObjectListChangedEventArgs e)
        {
            if (e.Location is not null && this.boards.ContainsKey(e.Location))
                this.MarkBoardsDirty();
        }

        private void OnTerrainChanged(object sender, TerrainFeatureListChangedEventArgs e)
        {
            if (e.Location is not null && this.boards.ContainsKey(e.Location))
                this.MarkBoardsDirty();
        }

        private void OnWorldChanged(object sender, BuildingListChangedEventArgs e)
        {
            if (e.Location is not null && this.boards.ContainsKey(e.Location))
                this.MarkBoardsDirty();
        }

        private const int ActionDelayMs = 160;

        private void ScheduleNext(GreenhouseJunimo junimo, GreenhouseNetwork network, HashSet<Point> handledTiles)
        {
            junimo.IsDispatching = true;
            Game1.delayedActions.Add(new DelayedAction(ActionDelayMs, () =>
            {
                if (!junimo.IsDestroying && this.activeWorkers.Contains(junimo))
                {
                    junimo.IsDispatching = false;
                    this.DoOneTask(junimo, network, handledTiles);
                }
                else
                    junimo.IsDispatching = false;
            }));
        }

        private void MarkBoardsDirty()
        {
            foreach (var board in this.boards.Values)
                board.MarkDirty();
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!this.mod.Config.ShowPathDebug || !Context.IsWorldReady)
                return;
            if (Game1.currentLocation?.Name != "Greenhouse" && !this.mod.Config.AllowAllLocations)
                return;
            var b = e.SpriteBatch;
            var currentLoc = Game1.currentLocation;
            foreach (var junimo in this.activeWorkers.Where(w => w.currentLocation == currentLoc))
            {
                string status = junimo.DebugTasks is not null && junimo.DebugTasks.Count > 0 ? $"{junimo.DebugTasks[0].Type} {junimo.DebugTasks[0].Soil.Tile.X},{junimo.DebugTasks[0].Soil.Tile.Y}" : "idle";
                var jpos = Game1.GlobalToLocal(Game1.viewport, junimo.Position + new Vector2(16, -56));
                b.DrawString(Game1.smallFont, $"{junimo.Name} {status} at {junimo.CurrentTile.X},{junimo.CurrentTile.Y} plan:{junimo.DebugPlan?.Count ?? 0}", jpos, Color.White);
                if (junimo.DebugPlan is not null && junimo.DebugPlan.Count > 0)
                {
                    for (int i = 0; i < junimo.DebugPlan.Count; i++)
                    {
                        var task = junimo.DebugPlan[i];
                        var p = task.Access;
                        Color taskColor = task.Type switch
                        {
                            GreenhouseTaskType.Harvest => DebugHarvestColor,
                            GreenhouseTaskType.Fertilize => DebugFertilizeColor,
                            GreenhouseTaskType.Plant => DebugPlantColor,
                            GreenhouseTaskType.Water => DebugWaterColor,
                            _ => Color.White
                        };
                        Color lineColor = taskColor * 0.7f;
                        if (i > 0)
                        {
                            var prev = junimo.DebugPlan[i - 1];
                            var pp = prev.Access;
                            var start = new Vector2(pp.X * 64 + 32 - Game1.viewport.X, pp.Y * 64 + 32 - Game1.viewport.Y);
                            var end = new Vector2(p.X * 64 + 32 - Game1.viewport.X, p.Y * 64 + 32 - Game1.viewport.Y);
                            Vector2 edge = end - start;
                            float len = edge.Length();
                            if (len > 0)
                            {
                                edge.Normalize();
                                float angle = (float)Math.Atan2(edge.Y, edge.X);
                                b.Draw(Game1.staminaRect, new Rectangle((int)start.X, (int)start.Y, (int)len, 5), null, lineColor, angle, Vector2.Zero, SpriteEffects.None, 0.9f);
                            }
                        }
                        string s = $"{i + 1}";
                        var sz = Game1.smallFont.MeasureString(s);
                        int boxW = Math.Max(26, (int)sz.X + 10);
                        int boxH = 26;
                        var bg = new Rectangle((int)(p.X * 64 - Game1.viewport.X + 32 - boxW / 2), (int)(p.Y * 64 - Game1.viewport.Y + 20 - boxH / 2), boxW, boxH);
                        b.Draw(Game1.staminaRect, bg, Color.Black * 0.6f);
                        var inner = new Rectangle(bg.X + 1, bg.Y + 1, boxW - 2, boxH - 2);
                        b.Draw(Game1.staminaRect, inner, taskColor * 0.9f);
                        var numPos = new Vector2(p.X * 64 - Game1.viewport.X + 32 - sz.X / 2, p.Y * 64 - Game1.viewport.Y + 20 - sz.Y / 2);
                        b.DrawString(Game1.smallFont, s, numPos, Color.Black);
                    }
                }
            }
        }

        public void ClearAll()
        {
            foreach (var junimo in this.activeWorkers)
                this.DepositCarried(junimo);

            this.activeWorkers.Clear();
            this.boards.Clear();
            foreach (var location in Game1.locations)
            {
                for (int i = location.characters.Count - 1; i >= 0; i--)
                {
                    if (location.characters[i] is GreenhouseJunimo)
                        location.characters.RemoveAt(i);
                }
            }
        }

        private void OneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.isTimePaused || !Game1.IsMasterGame)
                return;
            if (Game1.eventUp || Game1.dialogueUp)
                return;
            if (!GreenhouseUnlock.IsActive(this.mod.Config))
            {
                ModEntry.LogJunimo($"OneSecondUpdateTicked: gated off (GreenhouseUnlock.IsActive=false). Clearing {this.activeWorkers.Count} worker(s).");
                if (this.activeWorkers.Count > 0)
                    this.ClearAll();
                return;
            }

            if (!this.mod.Config.Enabled)
            {
                ModEntry.LogJunimo($"OneSecondUpdateTicked: gated off (Enabled=false). Clearing {this.activeWorkers.Count} worker(s).");
                if (this.activeWorkers.Count > 0)
                    this.ClearAll();
                return;
            }

            if (this.timeOfDayAtLastCheck != Game1.timeOfDay)
            {
                this.timeOfDayAtLastCheck = Game1.timeOfDay;
                this.actionsAtThisTime = 0;
            }
            else
            {
                this.actionsAtThisTime++;
            }

            if (this.actionsAtThisTime >= MaxActionsPerGameMinute)
                return;

            this.DoWork();
        }

        private void DoWork()
        {
            this.boardStaleCounter++;
            if (this.boardStaleCounter >= BoardStaleLimit)
            {
                this.boardStaleCounter = 0;
                this.MarkBoardsDirty();
            }

            var locations = new List<GameLocation>();
            if (this.mod.Config.AllowAllLocations)
            {
                foreach (var loc in Game1.locations)
                {
                    if (loc is not null && !loc.IsOutdoors && loc.Objects.Values.Any(o => o.QualifiedItemId == JunimaticCompat.JunimoPortalQiid))
                        locations.Add(loc);
                }
            }
            else
            {
                var greenhouse = Game1.getLocationFromName("Greenhouse");
                if (greenhouse is null)
                    return;
                locations.Add(greenhouse);
            }

            var validPortalKeys = new HashSet<(string LocationName, Vector2 PortalTile)>();
            foreach (var location in locations)
            {
                if (!this.boards.TryGetValue(location, out var board))
                {
                    board = new GreenhouseNetworks(location);
                    this.boards[location] = board;
                }

                if (board.IsDirty)
                {
                    foreach (var w in this.activeWorkers.Where(w => w.currentLocation == location))
                        w.FailedAccessPoints.Clear();
                }

                foreach (var network in board.GetNetworks())
                {
                    var key = (location.NameOrUniqueName, network.Portal.TileLocation);
                    validPortalKeys.Add(key);

                    var worker = this.activeWorkers.FirstOrDefault(w => w.PortalTile == network.Portal.TileLocation && w.currentLocation == location);
                    if (worker is null)
                    {
                        this.SpawnWorker(location, network);
                        this.actionsAtThisTime++;
                        continue;
                    }

                    if (worker.IsIdle)
                    {
                        if (this.HasWork(network, worker))
                            this.DoOneTask(worker, network, new HashSet<Point>());
                        else
                        {
                            if (worker.CarriedSeed is not null || worker.CarriedFertilizer is not null || worker.CarriedHarvest is not null)
                            {
                                ModEntry.LogJunimo($"{worker.Name} idle with no work and still carrying; walking back to chest.");
                                this.WalkBackToChestAndReturn(worker, network, () => worker.FadeOut());
                            }
                            else
                                worker.FadeOut();
                        }
                    }
                }
            }

            foreach (var worker in this.activeWorkers.Where(w => !validPortalKeys.Contains((w.currentLocation.NameOrUniqueName, w.PortalTile))).ToList())
                worker.FadeOut();
        }

        private bool HasWork(GreenhouseNetwork network, GreenhouseJunimo junimo = null)
        {
            if (this.mod.Config.HarvestCrops && network.Soils.Any(s => s.ReadyToHarvest))
                return true;
            if (network.Soils.Any(s => this.SoilNeedsFertilizing(network, s, junimo)))
                return true;
            bool hasSeed = this.ChooseSeed(network) is not null;
            if (this.mod.Config.BulkCarry && junimo?.CarriedSeed is not null && junimo.CarriedSeed.Stack > 0)
                hasSeed = true;
            if (this.mod.Config.PlantSeeds && network.Soils.Any(s => s.IsEmpty) && hasSeed)
                return true;
            if (network.Soils.Any(s => this.SoilNeedsWatering(s)))
                return true;
            return false;
        }

        private void SpawnWorker(GameLocation location, GreenhouseNetwork network)
        {
            var accessTile = network.PortalAccess;
            if (!location.isTilePassable(new Vector2(accessTile.X, accessTile.Y)))
                return;

            var junimo = new GreenhouseJunimo(
                location,
                Color.LimeGreen,
                new AnimatedSprite(@"Characters\Junimo", Game1.random.Next(6), 16, 16),
                new Vector2(accessTile.X * 64f, accessTile.Y * 64f),
                2,
                "NPC_GreenhouseWorker");

            junimo.OnDone = () => this.activeWorkers.Remove(junimo);
            junimo.currentLocation = location;
            junimo.PortalTile = network.Portal.TileLocation;
            junimo.PortalAccessTile = accessTile;
            junimo.HomeChest = network.Chests.Count > 0 ? network.Chests[0] : null;
            location.characters.Add(junimo);
            this.activeWorkers.Add(junimo);

            junimo.Start();
            var homeAccess = network.Chests.Count > 0 ? network.Chests[0].AccessPoint.ToString() : "none";
            ModEntry.LogJunimo($"SpawnWorker: spawned {junimo.Name} at portal {network.Portal.TileLocation}, home chest access={homeAccess}.");
            this.DoOneTask(junimo, network, new HashSet<Point>());
        }

        private void DoOneTask(GreenhouseJunimo junimo, GreenhouseNetwork network, HashSet<Point> handledTiles)
        {
            if (!junimo.IsIdle)
                return;
            string cachedSeedId = this.ChooseSeed(network);
            bool hasSeedForPlant = cachedSeedId is not null || (this.mod.Config.BulkCarry && junimo.CarriedSeed is not null && junimo.CarriedSeed.Stack > 0);

            var distMap = new Dictionary<Point, int>();
            var q = new Queue<Point>();
            var start = junimo.CurrentTile;
            distMap[start] = 0;
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                int d = distMap[cur];
                foreach (var dir in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1), new Point(-1, -1), new Point(-1, 1), new Point(1, -1), new Point(1, 1) })
                {
                    var nxt = new Point(cur.X + dir.X, cur.Y + dir.Y);
                    if (!network.WalkableTiles.Contains(nxt) && !nxt.Equals(start))
                        continue;
                    if (distMap.ContainsKey(nxt))
                        continue;
                    distMap[nxt] = d + 1;
                    q.Enqueue(nxt);
                }
            }

            var candidates = new List<PlannedTask>();
            foreach (var so in network.Soils)
            {
                if (!so.Exists)
                    continue;
                GreenhouseTaskType? type = null;
                if (this.mod.Config.HarvestCrops && so.ReadyToHarvest)
                    type = GreenhouseTaskType.Harvest;
                else if (this.mod.Config.FertilizeCrops && !so.IsEmpty && this.SoilNeedsFertilizing(network, so, junimo))
                    type = GreenhouseTaskType.Fertilize;
                else if (this.mod.Config.PlantSeeds && so.IsEmpty && hasSeedForPlant)
                    type = GreenhouseTaskType.Plant;
                else if (this.mod.Config.WaterCrops && so.NeedsWater && !so.IsEmpty)
                    type = GreenhouseTaskType.Water;
                else
                    continue;

                int bestDist = int.MaxValue;
                Point bestAccess = default;
                bool found = false;
                foreach (var ap in so.AdjacentAccessPoints)
                {
                    if (handledTiles.Contains(ap) || junimo.FailedAccessPoints.Contains(ap))
                        continue;
                    if (!distMap.TryGetValue(ap, out int d))
                        continue;
                    if (!found || d < bestDist)
                    {
                        bestDist = d;
                        bestAccess = ap;
                        found = true;
                    }
                }
                if (!found)
                    continue;
                candidates.Add(new PlannedTask(so, type.Value, bestAccess));
            }

            candidates.Sort((a, b) => CompareTasks(a, b, distMap, junimo));

            junimo.DebugTasks = candidates;
            junimo.DebugPlan = candidates;
            var next = candidates.FirstOrDefault();
            var soil = next?.Soil;
            if (soil is null)
            {
                bool anyWork = network.Soils.Any(s => s.Exists && (
                    (this.mod.Config.HarvestCrops && s.ReadyToHarvest) ||
                    (this.mod.Config.FertilizeCrops && !s.IsEmpty && this.SoilNeedsFertilizing(network, s, junimo)) ||
                    (this.mod.Config.PlantSeeds && s.IsEmpty && hasSeedForPlant) ||
                    (this.mod.Config.WaterCrops && s.NeedsWater && !s.IsEmpty)));
                if (anyWork && handledTiles.Count > 0)
                {
                    ModEntry.LogJunimo($"{junimo.Name} no task but work remains with handled={handledTiles.Count}, retrying with cleared handledTiles.");
                    handledTiles.Clear();
                    candidates.Clear();
                    foreach (var s in network.Soils)
                    {
                        if (!s.Exists)
                            continue;
                        GreenhouseTaskType? t = null;
                        if (this.mod.Config.HarvestCrops && s.ReadyToHarvest)
                            t = GreenhouseTaskType.Harvest;
                        else if (this.mod.Config.FertilizeCrops && !s.IsEmpty && this.SoilNeedsFertilizing(network, s, junimo))
                            t = GreenhouseTaskType.Fertilize;
                        else if (this.mod.Config.PlantSeeds && s.IsEmpty && hasSeedForPlant)
                            t = GreenhouseTaskType.Plant;
                        else if (this.mod.Config.WaterCrops && s.NeedsWater && !s.IsEmpty)
                            t = GreenhouseTaskType.Water;
                        else
                            continue;
                        int bestDist = int.MaxValue;
                        Point bestAccess = default;
                        bool found = false;
                        foreach (var ap in s.AdjacentAccessPoints)
                        {
                            if (junimo.FailedAccessPoints.Contains(ap))
                                continue;
                            if (!distMap.TryGetValue(ap, out int d))
                                continue;
                            if (!found || d < bestDist)
                            {
                                bestDist = d;
                                bestAccess = ap;
                                found = true;
                            }
                        }
                        if (!found)
                            continue;
                        candidates.Add(new PlannedTask(s, t.Value, bestAccess));
                    }
                    candidates.Sort((a, b) => CompareTasks(a, b, distMap, junimo));
                    junimo.DebugTasks = candidates;
                    junimo.DebugPlan = candidates;
                    next = candidates.FirstOrDefault();
                    soil = next?.Soil;
                    if (soil is not null)
                        ModEntry.LogJunimo($"{junimo.Name} retry found soil at {soil.Tile.X},{soil.Tile.Y}, continuing.");
                }
                if (soil is null)
                {
                    int emptyCount = network.Soils.Count(s => s.Exists && s.IsEmpty);
                    if (this.mod.Config.VerboseLogging)
                    {
                        int fertilizableCount = network.Soils.Count(s => this.SoilNeedsFertilizing(network, s, junimo));
                        int waterCount = network.Soils.Count(s => this.SoilNeedsWatering(s));
                        int harvestCount = network.Soils.Count(s => s.ReadyToHarvest);
                        ModEntry.LogJunimo($"{junimo.Name} no task at {junimo.CurrentTile.X},{junimo.CurrentTile.Y}: empty={emptyCount} fertilizable={fertilizableCount} water={waterCount} harvest={harvestCount} hasSeed={hasSeedForPlant} bulkCarry={this.mod.Config.BulkCarry} carriedSeed={(junimo.CarriedSeed is not null ? $"{junimo.CarriedSeed.QualifiedItemId} x{junimo.CarriedSeed.Stack}" : "none")} carriedFert={(junimo.CarriedFertilizer is not null ? $"{junimo.CarriedFertilizer.QualifiedItemId} x{junimo.CarriedFertilizer.Stack}" : "none")} handled={handledTiles.Count}. Going home.");
                    }
                    else if (emptyCount > 0 && hasSeedForPlant)
                    {
                        ModEntry.LogJunimo($"{junimo.Name} no task at {junimo.CurrentTile.X},{junimo.CurrentTile.Y}: empty={emptyCount} hasSeed={hasSeedForPlant} handled={handledTiles.Count}. Going home with seeds still carried.");
                    }
                    if (junimo.CarriedSeed is not null || junimo.CarriedFertilizer is not null || junimo.CarriedHarvest is not null)
                    {
                        ModEntry.LogJunimo($"{junimo.Name} no more work; walking back to chest to return items.");
                        junimo.IsDispatching = false;
                        junimo.DebugPlan = null;
                        junimo.DebugTasks = null;
                        this.WalkBackToChestAndReturn(junimo, network, () =>
                        {
                            if (junimo.CurrentTile != network.PortalAccess)
                            {
                                junimo.SetReturningToPortal();
                                junimo.Enqueue(network.PortalAccess, () => junimo.FadeOut());
                            }
                            else
                                junimo.FadeOut();
                        });
                    }
                    else
                    {
                        junimo.IsDispatching = false;
                        junimo.DebugPlan = null;
                        junimo.DebugTasks = null;
                        if (junimo.CurrentTile != network.PortalAccess)
                        {
                            junimo.SetReturningToPortal();
                            junimo.Enqueue(network.PortalAccess, () => junimo.FadeOut());
                        }
                        else
                            junimo.FadeOut();
                    }
                    return;
                }
            }

            var seedId = cachedSeedId;
            string plantSeedId = seedId;
            if (plantSeedId is null && this.mod.Config.BulkCarry && junimo.CarriedSeed is not null && junimo.CarriedSeed.Stack > 0)
                plantSeedId = junimo.CarriedSeed.QualifiedItemId;
            junimo.LastAccess = next.Access;
            handledTiles.Add(next.Access);
            junimo.IsDispatching = true;
            if (next.Type == GreenhouseTaskType.Harvest)
                this.DoHarvestTask(junimo, network, soil, handledTiles);
            else if (next.Type == GreenhouseTaskType.Fertilize)
                this.FetchAndApplyFertilizer(junimo, network, soil, handledTiles, false);
            else if (next.Type == GreenhouseTaskType.Plant && plantSeedId is not null)
                this.DoPlantTask(junimo, network, soil, plantSeedId, handledTiles);
            else if (next.Type == GreenhouseTaskType.Water)
                this.DoWaterTask(junimo, network, soil, handledTiles);
            else
            {
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                junimo.IsDispatching = false;
                this.DoOneTask(junimo, network, handledTiles);
            }
        }

        // walk to the soil, harvest, then deliver to the chest
        private void DoHarvestTask(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles)
        {
            string cropName = (soil.ProduceItemId is not null
                ? ItemRegistry.Create(ItemRegistry.QualifyItemId(soil.ProduceItemId))?.DisplayName
                : null) ?? "crop";
            var harvestAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
            ModEntry.LogJunimo($"{junimo.Name} harvesting {cropName} at {soil.Tile.X},{soil.Tile.Y} via {harvestAccess.X},{harvestAccess.Y}.");
            junimo.SetHarvestingTask(cropName);
            junimo.Enqueue(harvestAccess, () =>
            {
                if (!soil.ReadyToHarvest)
                {
                    handledTiles.UnionWith(soil.AdjacentAccessPoints);
                    this.ScheduleNext(junimo, network, handledTiles);
                    return;
                }
                junimo.CarriedHarvest ??= new List<Item>();
                foreach (var harvested in soil.Harvest())
                    this.MergeIntoHarvest(junimo.CarriedHarvest, harvested);

                if (network.Chests.Count == 0)
                {
                    DropAsDebris(junimo, junimo.CarriedHarvest);
                    junimo.CarriedHarvest = null;
                    this.ScheduleNext(junimo, network, handledTiles);
                    return;
                }

                if (this.mod.Config.BulkCarry)
                {
                    bool moreHarvest = network.Soils.Any(s => s.ReadyToHarvest && s.AdjacentAccessPoints.Any(ap => !handledTiles.Contains(ap) && !junimo.FailedAccessPoints.Contains(ap)));
                    if (moreHarvest)
                    {
                        ModEntry.LogJunimo($"{junimo.Name} bulk harvesting: {junimo.CarriedHarvest.Sum(i => i.Stack)} items, continuing.");
                        this.ScheduleNext(junimo, network, handledTiles);
                        return;
                    }
                }

                string storedName = junimo.CarriedHarvest.Count > 0 ? junimo.CarriedHarvest[0].DisplayName : "harvest";
                int totalStacks = junimo.CarriedHarvest.Sum(i => i.Stack);
                string bulkSuffix = this.mod.Config.BulkCarry && totalStacks > 1 ? $" x{totalStacks}" : "";
                ModEntry.LogJunimo($"{junimo.Name} storing {storedName}{bulkSuffix} at {network.Chests[0].AccessPoint.X},{network.Chests[0].AccessPoint.Y}.");
                junimo.SetStoringTask(storedName);
                junimo.Enqueue(network.Chests[0].AccessPoint, () =>
                {
                    this.DeliverHarvest(junimo, network);
                    this.ScheduleNext(junimo, network, handledTiles);
                });
            });
        }

        // fetch one seed (and fertilizer alongside if same chest) from a chest, walk to the soil, plant it, then fertilize and water
        private void DoPlantTask(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, string seedId, HashSet<Point> handledTiles)
        {
            string seedName = ItemRegistry.Create(seedId)?.DisplayName ?? seedId;
            if (this.mod.Config.BulkCarry && junimo.CarriedSeed is not null && junimo.CarriedSeed.QualifiedItemId == seedId && junimo.CarriedSeed.Stack > 0)
            {
                var bulkPlantAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
                ModEntry.LogJunimo($"{junimo.Name} planting {seedName}{(junimo.CarriedFertilizer is not null ? $" with {junimo.CarriedFertilizer.DisplayName}" : "")} at {soil.Tile.X},{soil.Tile.Y} via {bulkPlantAccess.X},{bulkPlantAccess.Y} (bulk).");
                junimo.SetPlantingTask(seedName, junimo.CarriedFertilizer?.DisplayName);
                junimo.Enqueue(bulkPlantAccess, () => this.OnPlantArrival(junimo, network, soil, seedName, bulkPlantAccess, handledTiles));
                return;
            }

            var chest = this.PickSeedChest(network, seedId);
            if (chest is null)
            {
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                this.ReturnCarried(junimo, network);
                this.ScheduleNext(junimo, network, handledTiles);
                return;
            }

            string fertId = null;
            GreenhouseChestStorage fertChest = null;
            if (this.mod.Config.FertilizeCrops)
            {
                fertId = this.ChooseBestFertilizerForSoil(network, soil);
                fertChest = fertId is not null ? this.PickFertilizerChest(network, fertId) : null;
            }

            bool shouldFetchAlongside = fertId is not null && fertChest is not null && fertChest == chest && soil.CanAcceptFertilizer(fertId);

            int seedToTake = 1;
            int fertToTake = 1;
            if (this.mod.Config.BulkCarry)
            {
                int neededSeeds = network.Soils.Count(s => s.Exists && s.IsEmpty);
                int availableSeeds = chest.GetAvailableCount(seedId);
                int maxSeedStack = ItemRegistry.Create(seedId).maximumStackSize();
                seedToTake = System.Math.Min(neededSeeds, System.Math.Min(availableSeeds, maxSeedStack));
                if (seedToTake <= 0) seedToTake = 1;
                if (shouldFetchAlongside)
                {
                    int neededFerts = network.Soils.Count(s => s.Exists && s.IsEmpty && s.CanAcceptFertilizer(fertId));
                    int availableFerts = fertChest.GetAvailableCount(fertId);
                    int maxFertStack = ItemRegistry.Create(fertId).maximumStackSize();
                    fertToTake = System.Math.Min(neededFerts, System.Math.Min(availableFerts, maxFertStack));
                    if (fertToTake <= 0) fertToTake = 1;
                }
            }

            string takingFertName = shouldFetchAlongside ? (ItemRegistry.Create(fertId)?.DisplayName ?? fertId) : null;
            string takingLog = this.mod.Config.BulkCarry && seedToTake > 1 ? $"{seedToTake} {seedName}" : seedName;
            string takingFertLog = shouldFetchAlongside ? (this.mod.Config.BulkCarry && fertToTake > 1 ? $"{fertToTake} {takingFertName}" : takingFertName) : null;
            ModEntry.LogJunimo($"{junimo.Name} taking {takingLog}{(shouldFetchAlongside ? $" and {takingFertLog}" : "")} from chest at {chest.AccessPoint.X},{chest.AccessPoint.Y} for soil {soil.Tile.X},{soil.Tile.Y}.");
            if (shouldFetchAlongside)
                junimo.SetTakingSeedAndFertTask(seedName, takingFertName);
            else
                junimo.SetTakingSeedTask(seedName);
            junimo.Enqueue(chest.AccessPoint, () =>
            {
                Item takenSeed = this.TakeFromChest(chest, seedId, seedToTake);
                if (takenSeed is null)
                {
                    ModEntry.LogJunimo($"{junimo.Name} could not fetch seed {seedId} from chest; returning carried and replanning.");
                    handledTiles.UnionWith(soil.AdjacentAccessPoints);
                    if (!this.mod.Config.NeverDropItems)
                        this.ReturnCarried(junimo, network);
                    this.ScheduleNext(junimo, network, handledTiles);
                    return;
                }
                junimo.CarriedSeed = takenSeed;

                if (shouldFetchAlongside)
                {
                    Item takenFert = this.TakeFromChest(fertChest, fertId, fertToTake);
                    if (takenFert is not null)
                        junimo.CarriedFertilizer = takenFert;
                }

                var plantAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
                ModEntry.LogJunimo($"{junimo.Name} planting {seedName}{(junimo.CarriedFertilizer is not null ? $" with {junimo.CarriedFertilizer.DisplayName}" : "")} at {soil.Tile.X},{soil.Tile.Y} via {plantAccess.X},{plantAccess.Y}.");
                junimo.SetPlantingTask(seedName, junimo.CarriedFertilizer?.DisplayName);
                junimo.Enqueue(plantAccess, () => this.OnPlantArrival(junimo, network, soil, seedName, plantAccess, handledTiles));
            });
        }

        // after a seed is planted: apply carried fertilizer, or fetch a fitting one, then water and continue
        private void AfterPlant(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, string seedName, HashSet<Point> handledTiles)
        {
            ModEntry.LogJunimo($"{junimo.Name} AfterPlant at {soil.Tile.X},{soil.Tile.Y}: carriedSeed={junimo.CarriedSeed?.QualifiedItemId} x{junimo.CarriedSeed?.Stack ?? 0}, carriedFert={junimo.CarriedFertilizer?.QualifiedItemId} x{junimo.CarriedFertilizer?.Stack ?? 0}.");
            if (this.mod.Config.FertilizeCrops && junimo.CarriedFertilizer is not null)
            {
                bool canAccept = soil.CanAcceptFertilizer(junimo.CarriedFertilizer.QualifiedItemId);
                ModEntry.LogJunimo($"{junimo.Name} AfterPlant: soil.CanAcceptFertilizer({junimo.CarriedFertilizer.QualifiedItemId}) = {canAccept}.");
                if (canAccept)
                {
                    string plantName = (soil.ProduceItemId is not null ? ItemRegistry.Create(ItemRegistry.QualifyItemId(soil.ProduceItemId))?.DisplayName : null) ?? seedName;
                    ModEntry.LogJunimo($"{junimo.Name} fertilizing {plantName} at {soil.Tile.X},{soil.Tile.Y}.");
                    junimo.SetFertilizingTask(plantName);
                    bool fertResult = soil.Fertilize(junimo.CarriedFertilizer.QualifiedItemId, Game1.MasterPlayer);
                    ModEntry.LogJunimo($"{junimo.Name} AfterPlant: soil.Fertilize returned {fertResult}.");
                    if (fertResult)
                    {
                        if (this.mod.Config.BulkCarry && junimo.CarriedFertilizer.Stack > 1)
                            junimo.CarriedFertilizer.Stack--;
                        else
                            junimo.CarriedFertilizer = null;
                        this.FinishPlant(junimo, network, soil, handledTiles);
                    }
                    else
                    {
                        ModEntry.LogJunimo($"{junimo.Name} could not apply fertilizer; returning to chest.");
                        handledTiles.UnionWith(soil.AdjacentAccessPoints);
                        if (!this.mod.Config.NeverDropItems)
                            this.ReturnCarried(junimo, network);
                        this.ScheduleNext(junimo, network, handledTiles);
                    }
                    return;
                }

                ModEntry.LogJunimo($"{junimo.Name} AfterPlant: fertilizer {junimo.CarriedFertilizer.QualifiedItemId} rejected by soil; returning ALL carried to chest.");
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                if (!this.mod.Config.NeverDropItems)
                    this.ReturnCarried(junimo, network);
                this.FinishPlant(junimo, network, soil, handledTiles);
                return;
            }

            if (this.mod.Config.FertilizeCrops)
                this.FetchAndApplyFertilizer(junimo, network, soil, handledTiles, true);
            else
                this.FinishPlant(junimo, network, soil, handledTiles);
        }

        private void FinishPlant(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles)
        {
            if (this.mod.Config.WaterAfterPlanting && this.mod.Config.WaterCrops)
                soil.Water();
            this.ScheduleNext(junimo, network, handledTiles);
        }

        private void OnPlantArrival(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, string seedName, Point plantAccess, HashSet<Point> handledTiles)
        {
            ModEntry.LogJunimo($"{junimo.Name} OnPlantArrival at {soil.Tile.X},{soil.Tile.Y}: exists={soil.Exists} empty={soil.IsEmpty} carriedSeed={junimo.CarriedSeed?.QualifiedItemId} x{junimo.CarriedSeed?.Stack ?? 0}.");
            if (!soil.Exists || !soil.IsEmpty)
            {
                ModEntry.LogJunimo($"{junimo.Name} OnPlantArrival: soil gone or already planted; quitting in disgust.");
                this.QuitInDisgust(junimo, network, soil, handledTiles);
                return;
            }
            if (junimo.CarriedSeed is null)
            {
                ModEntry.LogJunimo($"{junimo.Name} OnPlantArrival: no carried seed; returning carried and replanning.");
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                if (!this.mod.Config.NeverDropItems)
                    this.ReturnCarried(junimo, network);
                this.DoOneTask(junimo, network, handledTiles);
                return;
            }
            bool plantResult = soil.Plant(junimo.CarriedSeed.QualifiedItemId, Game1.MasterPlayer);
            ModEntry.LogJunimo($"{junimo.Name} OnPlantArrival: soil.Plant({junimo.CarriedSeed.QualifiedItemId}) returned {plantResult}.");
            if (!plantResult)
            {
                if (!soil.Exists)
                {
                    ModEntry.LogJunimo($"{junimo.Name} OnPlantArrival: soil destroyed during plant; quitting in disgust.");
                    this.QuitInDisgust(junimo, network, soil, handledTiles);
                }
                else
                {
                    ModEntry.LogJunimo($"{junimo.Name} could not plant {junimo.CarriedSeed.QualifiedItemId}; returning to chest.");
                    handledTiles.UnionWith(soil.AdjacentAccessPoints);
                    if (!this.mod.Config.NeverDropItems)
                        this.ReturnCarried(junimo, network);
                    this.DoOneTask(junimo, network, handledTiles);
                }
                return;
            }
            if (this.mod.Config.BulkCarry && junimo.CarriedSeed.Stack > 1)
                junimo.CarriedSeed.Stack--;
            else
                junimo.CarriedSeed = null;
            this.AfterPlant(junimo, network, soil, seedName, handledTiles);
        }

        private void OnFertilizeArrival(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles, bool water, Point fertAccess)
        {
            ModEntry.LogJunimo($"{junimo.Name} OnFertilizeArrival at {soil.Tile.X},{soil.Tile.Y}: carriedFert={junimo.CarriedFertilizer?.QualifiedItemId} x{junimo.CarriedFertilizer?.Stack ?? 0} water={water}.");
            if (junimo.CarriedFertilizer is null || !soil.CanAcceptFertilizer(junimo.CarriedFertilizer.QualifiedItemId))
            {
                ModEntry.LogJunimo($"{junimo.Name} OnFertilizeArrival: no fert or soil rejects it; returning ALL carried to chest.");
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                if (!this.mod.Config.NeverDropItems)
                    this.ReturnCarried(junimo, network);
                this.DoOneTask(junimo, network, handledTiles);
                return;
            }
            bool fertResult = soil.Fertilize(junimo.CarriedFertilizer.QualifiedItemId, Game1.MasterPlayer);
            ModEntry.LogJunimo($"{junimo.Name} OnFertilizeArrival: soil.Fertilize returned {fertResult}.");
            if (!fertResult)
            {
                ModEntry.LogJunimo($"{junimo.Name} could not apply fertilizer {junimo.CarriedFertilizer.QualifiedItemId}; returning to chest.");
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                if (!this.mod.Config.NeverDropItems)
                    this.ReturnCarried(junimo, network);
                this.DoOneTask(junimo, network, handledTiles);
                return;
            }
            if (this.mod.Config.BulkCarry && junimo.CarriedFertilizer.Stack > 1)
                junimo.CarriedFertilizer.Stack--;
            else
                junimo.CarriedFertilizer = null;
            if (water && this.mod.Config.WaterAfterPlanting && this.mod.Config.WaterCrops)
                soil.Water();
            this.DoOneTask(junimo, network, handledTiles);
        }

        // walk to the soil and water it
        private void DoWaterTask(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles)
        {
            var waterAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
            ModEntry.LogJunimo($"{junimo.Name} watering at {soil.Tile.X},{soil.Tile.Y} via {waterAccess.X},{waterAccess.Y}.");
            junimo.SetWateringTask();
            junimo.Enqueue(waterAccess, () =>
            {
                if (soil.NeedsWater)
                    soil.Water();
                this.ScheduleNext(junimo, network, handledTiles);
            });
        }

        private void DeliverHarvest(GreenhouseJunimo junimo, GreenhouseNetwork network)
        {
            if (junimo.CarriedHarvest is { Count: > 0 })
            {
                int totalCount = junimo.CarriedHarvest.Sum(i => i.Stack);
                if (this.mod.Config.AllowShipping)
                {
                    var bin = network.Chests.FirstOrDefault(c => c.IsMiniShippingBin);
                    if (bin is not null)
                    {
                        var left = bin.TryStore(junimo.CarriedHarvest);
                        int shipped = totalCount - left.Sum(i => i.Stack);
                        if (shipped > 0)
                        {
                            var farm = Game1.getFarm();
                            farm.playSound("Ship");
                            ModEntry.LogJunimo($"{junimo.Name} shipped {shipped} item(s) to mini shipping bin.");
                        }
                        if (left.Count > 0)
                        {
                            if (this.mod.Config.NeverDropItems)
                                junimo.CarriedHarvest = left;
                            else
                            {
                                DropAsDebris(junimo, left);
                                junimo.CarriedHarvest = null;
                            }
                        }
                        else
                            junimo.CarriedHarvest = null;
                        return;
                    }
                }
                var leftovers = this.TryDepositAll(network, junimo.CarriedHarvest);
                if (leftovers.Count > 0 && this.mod.Config.NeverDropItems)
                    junimo.CarriedHarvest = leftovers;
                else
                {
                    DropAsDebris(junimo, leftovers);
                    junimo.CarriedHarvest = null;
                }
            }
        }

        private void DepositCarried(GreenhouseJunimo junimo)
        {
            var items = CollectCarried(junimo);
            if (items.Count == 0)
                return;

            ModEntry.LogJunimo($"{junimo.Name} DepositCarried: {items.Count} item(s) [{string.Join(", ", items.Select(i => $"{i.QualifiedItemId} x{i.Stack}"))}] to HomeChest.");
            var left = junimo.HomeChest?.TryStore(items) ?? new List<Item>(items);
            if (left.Count > 0)
                ModEntry.LogJunimo($"{junimo.Name} DepositCarried: {left.Count} leftover(s) dropped as debris.");
            DropAsDebris(junimo, left);
        }

        private List<Item> TryDepositAll(GreenhouseNetwork network, IEnumerable<Item> items)
        {
            var leftovers = new List<Item>(items);
            foreach (var chest in network.Chests)
            {
                if (leftovers.Count == 0)
                    break;
                leftovers = chest.TryStore(leftovers);
            }
            return leftovers;
        }

        // pick the chest that actually holds the requested seed, or null if none do
        private GreenhouseChestStorage PickSeedChest(GreenhouseNetwork network, string seedId)
        {
            if (network.Chests.Count == 0 || string.IsNullOrEmpty(seedId))
                return null;
            return network.Chests.FirstOrDefault(c => c.GetSeed(seedId) is not null);
        }

        private Item TakeFromChest(GreenhouseChestStorage chest, string qualifiedItemId, int count)
        {
            if (this.mod.Config.BulkCarry)
                return chest.TakeUpTo(qualifiedItemId, count);

            var tote = new Inventory();
            if (chest.TryFulfillShoppingList(new List<Item> { ItemRegistry.Create(qualifiedItemId, 1) }, tote) && tote.Count > 0)
                return tote[0];
            return null;
        }

        private static List<Item> CollectCarried(GreenhouseJunimo junimo)
        {
            var items = new List<Item>();
            if (junimo.CarriedSeed is not null)
                items.Add(junimo.CarriedSeed);
            if (junimo.CarriedFertilizer is not null)
                items.Add(junimo.CarriedFertilizer);
            if (junimo.CarriedHarvest is { Count: > 0 })
                items.AddRange(junimo.CarriedHarvest);
            junimo.CarriedSeed = null;
            junimo.CarriedHarvest = null;
            junimo.CarriedFertilizer = null;
            return items;
        }

        private static void DropAsDebris(GreenhouseJunimo junimo, List<Item> items)
        {
            if (items is null || items.Count == 0 || junimo.currentLocation is null)
                return;
            foreach (var item in items)
                junimo.currentLocation.debris.Add(new Debris(item, junimo.Position));
        }

        private bool SoilNeedsWatering(GreenhouseSoil soil)
            => soil.NeedsWater && !soil.IsEmpty && this.mod.Config.WaterCrops;

        private string ChooseSeed(GreenhouseNetwork network)
        {
            int tick = Game1.ticks;
            var key = network.Portal.TileLocation.ToPoint();
            if (lastSeedCacheTick == tick && cachedSeedIds.TryGetValue(key, out string cached))
                return cached;
            if (lastSeedCacheTick != tick)
            {
                lastSeedCacheTick = tick;
                cachedSeedIds.Clear();
            }
            Item best = null;
            foreach (var chest in network.Chests)
            {
                var seed = chest.GetMostExpensiveSeed();
                if (seed is not null && (best is null || seed.salePrice() > best.salePrice()))
                    best = seed;
            }
            string result = best?.QualifiedItemId;
            cachedSeedIds[key] = result;
            return result;
        }

        private GreenhouseChestStorage PickFertilizerChest(GreenhouseNetwork network, string fertilizerItemId)
        {
            if (network.Chests.Count == 0 || string.IsNullOrEmpty(fertilizerItemId))
                return null;
            return network.Chests.FirstOrDefault(c => c.GetFertilizer(fertilizerItemId) is not null);
        }

        private string ChooseBestFertilizerForSoil(GreenhouseNetwork network, GreenhouseSoil soil)
        {
            Item best = null;
            foreach (var chest in network.Chests)
            {
                foreach (var item in chest.Items)
                {
                    if (item is null || item.Category != StardewValley.Object.fertilizerCategory)
                        continue;
                    if (!soil.CanAcceptFertilizer(item.QualifiedItemId))
                    {
                        if (this.mod.Config.VerboseLogging)
                            ModEntry.LogJunimo($"ChooseBestFertilizer: {item.QualifiedItemId} rejected by soil {soil.Tile.X},{soil.Tile.Y} (CanApplyFertilizer=false).");
                        continue;
                    }
                    if (best is null || item.salePrice() > best.salePrice())
                        best = item;
                }
            }
            if (best is null && this.mod.Config.VerboseLogging)
            {
                int totalFert = network.Chests.Sum(c => c.Items.Count(i => i is not null && i.Category == StardewValley.Object.fertilizerCategory));
                ModEntry.LogJunimo($"ChooseBestFertilizer: no valid fertilizer for soil {soil.Tile.X},{soil.Tile.Y}. Total fertilizer in chests: {totalFert}.");
            }
            return best?.QualifiedItemId;
        }

        private bool SoilNeedsFertilizing(GreenhouseNetwork network, GreenhouseSoil soil, GreenhouseJunimo junimo = null)
        {
            if (!this.mod.Config.FertilizeCrops)
                return false;
            if (soil.IsEmpty)
                return false;
            if (this.mod.Config.BulkCarry && junimo?.CarriedFertilizer is not null && soil.CanAcceptFertilizer(junimo.CarriedFertilizer.QualifiedItemId))
                return true;
            return this.ChooseBestFertilizerForSoil(network, soil) is not null;
        }

        private void FetchAndApplyFertilizer(GreenhouseJunimo junimo, GreenhouseNetwork network, GreenhouseSoil soil, HashSet<Point> handledTiles, bool water)
        {
            if (this.mod.Config.BulkCarry && junimo.CarriedFertilizer is not null && junimo.CarriedFertilizer.Stack > 0 && soil.CanAcceptFertilizer(junimo.CarriedFertilizer.QualifiedItemId))
            {
                var bulkFertAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
                string bulkPlantName = (soil.ProduceItemId is not null ? ItemRegistry.Create(ItemRegistry.QualifyItemId(soil.ProduceItemId))?.DisplayName : null) ?? "soil";
                ModEntry.LogJunimo($"{junimo.Name} fertilizing {bulkPlantName} at {soil.Tile.X},{soil.Tile.Y} via {bulkFertAccess.X},{bulkFertAccess.Y} (bulk).");
                junimo.SetFertilizingTask(bulkPlantName);
                junimo.Enqueue(bulkFertAccess, () => this.OnFertilizeArrival(junimo, network, soil, handledTiles, water, bulkFertAccess));
                return;
            }

            var fertId = this.ChooseBestFertilizerForSoil(network, soil);
            var fertChest = fertId is not null ? this.PickFertilizerChest(network, fertId) : null;
            if (fertId is null || fertChest is null)
            {
                handledTiles.UnionWith(soil.AdjacentAccessPoints);
                if (water && this.mod.Config.WaterAfterPlanting && this.mod.Config.WaterCrops)
                    soil.Water();
                this.ScheduleNext(junimo, network, handledTiles);
                return;
            }

            int fertToTake = 1;
            if (this.mod.Config.BulkCarry)
            {
                int neededFerts = network.Soils.Count(s => s.Exists &&
                    (this.SoilNeedsFertilizing(network, s) ||
                     (s.IsEmpty && s.CanAcceptFertilizer(fertId))));
                int availableFerts = fertChest.GetAvailableCount(fertId);
                int maxFertStack = ItemRegistry.Create(fertId).maximumStackSize();
                fertToTake = System.Math.Min(neededFerts, System.Math.Min(availableFerts, maxFertStack));
                if (fertToTake <= 0) fertToTake = 1;
            }

            string fertName = ItemRegistry.Create(fertId)?.DisplayName ?? fertId;
            string fertLogName = this.mod.Config.BulkCarry && fertToTake > 1 ? $"{fertToTake} {fertName}" : fertName;
            ModEntry.LogJunimo($"{junimo.Name} taking {fertLogName} from chest at {fertChest.AccessPoint.X},{fertChest.AccessPoint.Y} for soil {soil.Tile.X},{soil.Tile.Y}.");
            junimo.SetTakingFertTask(fertName);
            junimo.Enqueue(fertChest.AccessPoint, () =>
            {
                Item takenFert = this.TakeFromChest(fertChest, fertId, fertToTake);
                if (takenFert is null)
                {
                    ModEntry.LogJunimo($"{junimo.Name} could not fetch fertilizer {fertId}; replanning.");
                    handledTiles.UnionWith(soil.AdjacentAccessPoints);
                    this.ScheduleNext(junimo, network, handledTiles);
                    return;
                }
                junimo.CarriedFertilizer = takenFert;

                var fertAccess = soil.GetNearestAccessPoint(junimo.CurrentTile);
                string plantNameForFetch = (soil.ProduceItemId is not null ? ItemRegistry.Create(ItemRegistry.QualifyItemId(soil.ProduceItemId))?.DisplayName : null) ?? "soil";
                ModEntry.LogJunimo($"{junimo.Name} fertilizing {plantNameForFetch} at {soil.Tile.X},{soil.Tile.Y} via {fertAccess.X},{fertAccess.Y}.");
                junimo.SetFertilizingTask(plantNameForFetch);
                junimo.Enqueue(fertAccess, () => this.OnFertilizeArrival(junimo, network, soil, handledTiles, water, fertAccess));
            });
        }

        private void MergeIntoHarvest(List<Item> list, Item item)
        {
            var existing = list.FirstOrDefault(x => x.ItemId == item.ItemId && x.Quality == item.Quality && ItemHelper.IsColorMatch(x, item) && x.Stack + item.Stack < 1000);
            if (existing is not null)
                existing.Stack += item.Stack;
            else
                list.Add(item);
        }

        private static int CompareTasks(PlannedTask a, PlannedTask b, Dictionary<Point, int> distMap, GreenhouseJunimo junimo)
        {
            int aPri = a.Type switch { GreenhouseTaskType.Harvest => 0, GreenhouseTaskType.Fertilize => 1, GreenhouseTaskType.Plant => 2, _ => 3 };
            int bPri = b.Type switch { GreenhouseTaskType.Harvest => 0, GreenhouseTaskType.Fertilize => 1, GreenhouseTaskType.Plant => 2, _ => 3 };
            int c = aPri.CompareTo(bPri);
            if (c != 0) return c;
            int da = distMap.TryGetValue(a.Access, out int dav) ? dav : int.MaxValue;
            int db = distMap.TryGetValue(b.Access, out int dbv) ? dbv : int.MaxValue;
            c = da.CompareTo(db);
            if (c != 0) return c;
            bool aSameRow = a.Access.Y == junimo.LastAccess.Y;
            bool bSameRow = b.Access.Y == junimo.LastAccess.Y;
            if (aSameRow != bSameRow)
                return aSameRow ? -1 : 1;
            bool aSameCol = a.Access.X == junimo.LastAccess.X;
            bool bSameCol = b.Access.X == junimo.LastAccess.X;
            if (aSameCol != bSameCol)
                return aSameCol ? -1 : 1;
            int la = Math.Abs(a.Access.X - junimo.LastAccess.X) + Math.Abs(a.Access.Y - junimo.LastAccess.Y);
            int lb = Math.Abs(b.Access.X - junimo.LastAccess.X) + Math.Abs(b.Access.Y - junimo.LastAccess.Y);
            c = la.CompareTo(lb);
            if (c != 0) return c;
            c = a.Soil.Tile.X.CompareTo(b.Soil.Tile.X);
            if (c != 0) return c;
            return a.Soil.Tile.Y.CompareTo(b.Soil.Tile.Y);
        }
    }
}
