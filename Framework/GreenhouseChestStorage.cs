using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseChestStorage
    {
        private readonly Chest chest;

        private readonly Point chestTile;

        private readonly GameLocation location;

        public Point AccessPoint { get; }

        public bool IsMiniShippingBin => this.chest.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin;

        public GreenhouseChestStorage(Chest chest, Point accessPoint, GameLocation location = null)
        {
            this.chest = chest;
            this.chestTile = new Point((int)chest.TileLocation.X, (int)chest.TileLocation.Y);
            this.AccessPoint = accessPoint;
            this.location = location;
        }

        private Chest GetChest()
        {
            var loc = this.location;
            if (loc is null)
                loc = Game1.getLocationFromName("Greenhouse");
            if (loc != null
                && loc.objects.TryGetValue(new Vector2(this.chestTile.X, this.chestTile.Y), out StardewValley.Object obj)
                && obj is Chest live)
            {
                return live;
            }

            return this.chest;
        }

        public IInventory Items => this.GetChest().GetItemsForPlayer(Game1.player.UniqueMultiplayerID);

        public List<Item> TryStore(IEnumerable<Item> items)
        {
            var chest = this.GetChest();
            var inventory = chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID);
            int capacity = chest.GetActualCapacity();
            var leftovers = new List<Item>();
            foreach (var item in items)
            {
                int remaining = item.Stack;

                var existing = inventory.FirstOrDefault(i => i is not null && i.canStackWith(item));
                if (existing is not null)
                {
                    int canTake = item.maximumStackSize() - existing.Stack;
                    if (canTake > 0)
                    {
                        int moved = System.Math.Min(canTake, remaining);
                        existing.Stack += moved;
                        remaining -= moved;
                    }
                }

                if (remaining > 0)
                {
                    if (inventory.Count() >= capacity)
                    {
                        var leftover = item.getOne();
                        leftover.Stack = remaining;
                        leftovers.Add(leftover);
                    }
                    else
                    {
                        var toAdd = item.getOne();
                        toAdd.Stack = remaining;
                        inventory.Add(toAdd);
                    }
                }
            }

            return leftovers;
        }

        public bool TryFulfillShoppingList(List<Item> shoppingList, Inventory toteBag)
        {
            var inventory = this.Items;

            bool IsSame(Item request, Item chestItem)
                => chestItem is not null && chestItem.Stack > 0 && chestItem.ItemId == request.ItemId && chestItem.Quality == request.Quality;

            foreach (var request in shoppingList)
            {
                if (inventory.Where(i => IsSame(request, i)).Sum(i => i.Stack) < request.Stack)
                    return false;
            }

            foreach (var request in shoppingList)
            {
                int remaining = request.Stack;
                while (remaining > 0)
                {
                    var chestStack = inventory.FirstOrDefault(i => IsSame(request, i));
                    if (chestStack is null)
                        return false;

                    var bagItem = chestStack.getOne();
                    bagItem.Stack = System.Math.Min(chestStack.Stack, remaining);
                    toteBag.Add(bagItem);

                    if (chestStack.Stack > bagItem.Stack)
                    {
                        chestStack.Stack -= bagItem.Stack;
                        remaining = 0;
                    }
                    else
                    {
                        remaining -= chestStack.Stack;
                        inventory.Remove(chestStack);
                    }
                }
            }

            return true;
        }

        public Item GetMostExpensiveSeed()
        {
            var loc = this.location ?? Game1.getLocationFromName("Greenhouse");
            return this.Items
                .Where(i => i is not null && IsPlantableSeed(i, loc))
                .OrderByDescending(i => i.salePrice())
                .FirstOrDefault();
        }

        private static bool IsPlantableSeed(Item item, GameLocation loc)
            => Crop.TryGetData(Crop.ResolveSeedId(item.ItemId, loc), out _);

        public Item GetSeed(string seedItemId)
            => this.Items.FirstOrDefault(i => i is not null && i.QualifiedItemId == seedItemId);

        public Item GetFertilizer(string fertilizerItemId)
            => this.Items.FirstOrDefault(i => i is not null && i.QualifiedItemId == fertilizerItemId && IsFertilizer(i));

        public int GetAvailableCount(string qualifiedItemId)
            => this.Items.Where(i => i is not null && i.QualifiedItemId == qualifiedItemId).Sum(i => i.Stack);

        public Item TakeUpTo(string qualifiedItemId, int desired)
        {
            if (string.IsNullOrEmpty(qualifiedItemId) || desired <= 0)
                return null;
            int available = this.GetAvailableCount(qualifiedItemId);
            int toTake = System.Math.Min(desired, available);
            if (toTake <= 0)
                return null;
            var request = ItemRegistry.Create(qualifiedItemId, toTake);
            var tote = new Inventory();
            if (!this.TryFulfillShoppingList(new List<Item> { request }, tote) || tote.Count == 0)
                return null;
            if (tote.Count == 1)
                return tote[0];
            int total = tote.Sum(i => i.Stack);
            var consolidated = ItemRegistry.Create(qualifiedItemId, total);
            return consolidated;
        }

        private static bool IsFertilizer(Item item)
            => item is not null && item.Category == StardewValley.Object.fertilizerCategory;
    }
}
