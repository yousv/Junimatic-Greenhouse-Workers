using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseNetworks
    {
        private readonly GameLocation location;
        private readonly Dictionary<Point, GreenhouseNetwork> networks = new Dictionary<Point, GreenhouseNetwork>();
        private bool dirty = true;

        public GreenhouseNetworks(GameLocation location)
        {
            this.location = location;
        }

        public bool IsDirty => this.dirty;

        public void MarkDirty() => this.dirty = true;

        public IEnumerable<GreenhouseNetwork> GetNetworks()
        {
            if (this.dirty)
                this.Rebuild();
            return this.networks.Values;
        }

        private void Rebuild()
        {
            this.networks.Clear();
            var map = new GreenhouseGameMap(this.location);
            foreach (var portal in map.GetPortals())
            {
                var net = map.TryBuildNetwork(portal);
                if (net is not null)
                    this.networks[portal.TileLocation.ToPoint()] = net;
            }

            this.dirty = false;
            ModEntry.LogJunimo($"GreenhouseNetworks rebuilt for {this.location.Name}: {this.networks.Count} network(s).");
        }
    }
}
