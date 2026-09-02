using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseGameMap
    {
        public GameLocation Location { get; }

        public GreenhouseGameMap(GameLocation location)
        {
            this.Location = location;
        }

        public IEnumerable<StardewValley.Object> GetPortals()
            => this.Location.objects.Values.Where(o => o.QualifiedItemId == JunimaticCompat.JunimoPortalQiid);

        public void GetStartingInfo(StardewValley.Object portal, out List<Point> adjacentTiles, out FlooringSet validFlooringTiles)
        {
            var floorIds = new HashSet<string>();
            var tilesWithFloors = new List<Point>();
            foreach (var direction in CardinalDirections)
            {
                var targetPoint = direction + portal.TileLocation.ToPoint();
                string floorIdAt = FlooringSet.GetFlooringId(this.Location, targetPoint);
                if (floorIdAt is not null)
                {
                    tilesWithFloors.Add(targetPoint);
                    floorIds.Add(floorIdAt);
                }
            }

            validFlooringTiles = new FlooringSet(floorIds);
            adjacentTiles = tilesWithFloors;
        }

        public void GetThingAt(Point tileToCheck, FlooringSet validFloors, out bool isWalkable, out HoeDirt soil, out Chest chest, out StardewValley.Object machine)
        {
            soil = null;
            chest = null;
            machine = null;

            if (validFloors.IsTileWalkable(this.Location, tileToCheck))
            {
                isWalkable = true;
                return;
            }

            isWalkable = false;

            StardewValley.Object item = this.Location.getObjectAtTile(tileToCheck.X, tileToCheck.Y);
            if (item is not null)
            {
                if (item.QualifiedItemId == JunimaticCompat.JunimoPortalQiid)
                {
                    return;
                }

                if (item is Chest c && (c.SpecialChestType == Chest.SpecialChestTypes.None || c.SpecialChestType == Chest.SpecialChestTypes.BigChest || c.SpecialChestType == Chest.SpecialChestTypes.JunimoChest || c.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin))
                {
                    chest = c;
                    return;
                }

                if (item.GetMachineData() is not null)
                {
                    machine = item;
                }

                return;
            }

            if (this.Location.terrainFeatures.TryGetValue(tileToCheck.ToVector2(), out TerrainFeature feature) && feature is HoeDirt dirt)
            {
                soil = dirt;
                isWalkable = true;
            }
        }

        private static readonly Point[] CardinalDirections =
        {
            new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1)
        };

        private static readonly Point[] All8Directions =
        {
            new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1),
            new Point(-1, -1), new Point(-1, 1), new Point(1, -1), new Point(1, 1)
        };

        public GreenhouseNetwork TryBuildNetwork(StardewValley.Object portal)
        {
            var soils = new List<GreenhouseSoil>();
            var chests = new List<GreenhouseChestStorage>();
            var checkedForWorkTiles = new HashSet<Point>();
            var walkedTiles = new HashSet<Point>();
            this.GetStartingInfo(portal, out var startingPoints, out var walkableFloorTypes);

            foreach (var startingTile in startingPoints)
            {
                var tilesToInvestigate = new Queue<Point>();
                tilesToInvestigate.Enqueue(startingTile);

                while (tilesToInvestigate.TryDequeue(out var reachableTile))
                {
                    if (walkedTiles.Contains(reachableTile))
                        continue;

                    foreach (var direction in CardinalDirections)
                    {
                        var adjacentTile = reachableTile + direction;
                        if (checkedForWorkTiles.Contains(adjacentTile))
                            continue;

                        GetThingAt(adjacentTile, walkableFloorTypes, out bool isWalkable, out HoeDirt soil, out Chest chest, out StardewValley.Object machine);
                        if (chest is not null)
                        {
                            chests.Add(new GreenhouseChestStorage(chest, reachableTile, this.Location));
                            checkedForWorkTiles.Add(adjacentTile);
                        }
                        else if (machine is not null)
                        {
                            checkedForWorkTiles.Add(adjacentTile);
                        }
                        else if (soil is not null)
                        {
                            soils.Add(new GreenhouseSoil(soil, this.Location, reachableTile));
                            checkedForWorkTiles.Add(adjacentTile);
                        }

                        if (isWalkable)
                        {
                            tilesToInvestigate.Enqueue(adjacentTile);
                        }
                        else
                        {
                            checkedForWorkTiles.Add(adjacentTile);
                        }
                    }

                    checkedForWorkTiles.Add(reachableTile);
                    walkedTiles.Add(reachableTile);
                }
            }

            foreach (var soil in soils)
            {
                var adj = new List<Point>();
                foreach (var dir in All8Directions)
                {
                    var neighbor = soil.Tile + dir;
                    if (walkedTiles.Contains(neighbor))
                        adj.Add(neighbor);
                }
                if (adj.Count > 0)
                    soil.SetAdjacentAccessPoints(adj);
            }

            if (soils.Count > 0 && chests.Count > 0)
            {
                var portalAccess = startingPoints[0];
                return new GreenhouseNetwork(portal, portalAccess, soils, chests, new List<Point>(walkedTiles));
            }

            return null;
        }
    }

    internal class GreenhouseNetwork
    {
        public StardewValley.Object Portal { get; }
        public Point PortalAccess { get; }
        public List<GreenhouseSoil> Soils { get; }
        public List<GreenhouseChestStorage> Chests { get; }
        public IReadOnlyCollection<Point> WalkableTiles { get; }

        public GreenhouseNetwork(StardewValley.Object portal, Point portalAccess, List<GreenhouseSoil> soils, List<GreenhouseChestStorage> chests, IReadOnlyCollection<Point> walkableTiles = null)
        {
            this.Portal = portal;
            this.PortalAccess = portalAccess;
            this.Soils = soils;
            this.Chests = chests;
            this.WalkableTiles = walkableTiles ?? new List<Point>();
        }
    }

    internal class FlooringSet
    {
        private const string BareFloorSentinel = "#BARE_FLOOR#";

        private readonly HashSet<string> validFloorIds;

        internal FlooringSet(IEnumerable<string> ids)
        {
            this.validFloorIds = new HashSet<string>(ids);
        }

        internal bool IsTileWalkable(GameLocation location, Point tile)
        {
            string floorId = GetFlooringId(location, tile);
            return floorId is not null && this.validFloorIds.Contains(floorId);
        }

        internal static string GetFlooringId(GameLocation location, Point point)
        {
            Vector2 tile = point.ToVector2();

            StardewValley.Object tileObject = location.getObjectAtTile(point.X, point.Y);
            if (tileObject is not null
                && !(tileObject is Furniture furniture && furniture.furniture_type.Value == Furniture.rug))
            {
                return null;
            }

            if (location.getBuildingAt(tile) is not null)
                return null;

            location.terrainFeatures.TryGetValue(tile, out TerrainFeature terrain);
            if (terrain is Flooring flooring)
                return flooring.whichFloor.Value.ToString();

            if (terrain is null && !location.IsOutdoors && location.isTilePassable(tile) && location.isTilePlaceable(tile))
                return BareFloorSentinel;

            return null;
        }
    }
}
