using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseSoil
    {
        private const int WateredState = 1;

        private readonly GameLocation location;
        private readonly Point tile;
        public Point Tile => this.tile;
        public Point AccessPoint { get; private set; }
        public IReadOnlyList<Point> AdjacentAccessPoints { get; private set; } = new List<Point>();

        public GreenhouseSoil(HoeDirt dirt, GameLocation location, Point accessPoint)
        {
            this.location = location;
            this.tile = new Point((int)dirt.Tile.X, (int)dirt.Tile.Y);
            this.AccessPoint = accessPoint;
            this.AdjacentAccessPoints = new List<Point> { accessPoint };
        }

        public void SetAdjacentAccessPoints(IReadOnlyList<Point> points)
        {
            if (points is not null && points.Count > 0)
            {
                this.AdjacentAccessPoints = points;
                this.AccessPoint = points[0];
            }
        }

        public Point GetNearestAccessPoint(Point from)
        {
            if (this.AdjacentAccessPoints is null || this.AdjacentAccessPoints.Count == 0)
                return this.AccessPoint;
            Point best = this.AdjacentAccessPoints[0];
            int bestDist = Math.Max(Math.Abs(best.X - from.X), Math.Abs(best.Y - from.Y));
            bool bestCard = best.X == from.X || best.Y == from.Y;
            for (int i = 1; i < this.AdjacentAccessPoints.Count; i++)
            {
                var p = this.AdjacentAccessPoints[i];
                int d = Math.Max(Math.Abs(p.X - from.X), Math.Abs(p.Y - from.Y));
                bool card = p.X == from.X || p.Y == from.Y;
                if (d < bestDist || d == bestDist && card && !bestCard)
                {
                    bestDist = d;
                    best = p;
                    bestCard = card;
                }
            }
            return best;
        }

        private GameLocation GetLocation()
        {
            return this.location;
        }

        private HoeDirt GetDirt()
        {
            var location = this.GetLocation();
            if (location?.terrainFeatures.TryGetValue(new Vector2(this.tile.X, this.tile.Y), out TerrainFeature feature) == true
                && feature is HoeDirt hd)
            {
                return hd;
            }

            return null;
        }

        public bool IsEmpty => this.GetDirt()?.crop is null;

        private static string UnqualifySeedId(string id)
        {
            int close = id.IndexOf(')');
            return id.StartsWith("(") && close >= 0 ? id.Substring(close + 1) : id;
        }

        public string ProduceItemId
        {
            get
            {
                var d = this.GetDirt();
                if (d?.crop is null)
                    return null;
                return d.crop.indexOfHarvest.Value;
            }
        }

        public bool NeedsWater
        {
            get
            {
                var d = this.GetDirt();
                return d is not null && d.needsWatering() && !d.isWatered();
            }
        }

        public bool Exists => this.GetDirt() is not null;

        public bool ReadyToHarvest => this.GetDirt()?.readyForHarvest() == true;

        public bool CanAcceptFertilizer(string fertilizerItemId)
        {
            var d = this.GetDirt();
            return d is not null && d.CanApplyFertilizer(fertilizerItemId);
        }

        public bool Fertilize(string fertilizerItemId, Farmer who)
        {
            var d = this.GetDirt();
            if (d is null || !d.CanApplyFertilizer(fertilizerItemId))
                return false;
            return d.plant(fertilizerItemId, who, isFertilizer: true);
        }

        public void Water()
        {
            var d = this.GetDirt();
            if (d is null)
                return;

            var location = this.GetLocation();
            d.state.Value = WateredState;
            location.playSound("wateringCan");
            Game1.Multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(13, new Vector2(this.tile.X * 64f, this.tile.Y * 64f - 20f), Color.White, 10, Game1.random.NextDouble() < 0.5, 70f, 0, 64, (this.tile.Y * 64f + 32f) / 10000f + 0.0012f));
        }

        public bool Plant(string seedItemId, Farmer who)
        {
            var d = this.GetDirt();
            if (d is null || d.crop is not null)
                return false;

            var loc = d.Location ?? this.GetLocation();
            string unqualified = UnqualifySeedId(seedItemId);
            string resolved = Crop.ResolveSeedId(unqualified, loc);
            if (!Crop.TryGetData(resolved, out var data) || data.Seasons.Count == 0)
            {
                ModEntry.LogJunimo($"{nameof(GreenhouseSoil)} Plant failed: seed '{seedItemId}' resolved to '{resolved}' has no crop data.");
                return false;
            }

            d.crop = new Crop(resolved, this.tile.X, this.tile.Y, loc);
            d.applySpeedIncreases(who);
            loc.playSound("dirtyHit");
            Game1.stats.SeedsSown++;
            if (d.hasPaddyCrop() && d.paddyWaterCheck())
            {
                d.state.Value = 1;
                d.updateNeighbors();
            }

            return true;
        }

        public List<Item> Harvest()
        {
            var d = this.GetDirt();
            if (d is null || d.crop is null)
                return new List<Item>();

            Crop crop = d.crop;
            List<Item> result = HarvestInterceptor.InterceptHarvest(() => crop.harvest(this.tile.X, this.tile.Y, d), this.GetLocation());
            if (!d.crop.RegrowsAfterHarvest())
            {
                d.destroyCrop(this.GetLocation().IsActiveLocation());
            }

            return result;
        }
    }
}
