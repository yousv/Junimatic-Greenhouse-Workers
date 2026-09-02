using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewValley;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class HarvestInterceptor
    {
        private const int MaxMergeStack = 1000;

        private static List<Item> collectedItems = null;
        private static Farmer fakeFarmer = new FakeFarmer();

        private class FakeFarmer : Farmer
        {
            public FakeFarmer()
            {
                this.mostRecentlyGrabbedItem = new StardewValley.Object();
            }

            public override void gainExperience(int which, int howMuch) { }
        }

        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.addItemToInventoryBool)),
                prefix: new HarmonyMethod(typeof(HarvestInterceptor), nameof(Farmer_addItemToInventoryBool_Prefix)));
            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.createItemDebris)),
                prefix: new HarmonyMethod(typeof(HarvestInterceptor), nameof(Game1_createItemDebris_Prefix)));
            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.createObjectDebris), new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(GameLocation) }),
                prefix: new HarmonyMethod(typeof(HarvestInterceptor), nameof(Game1_createObjectDebris_Prefix)));
        }

        internal static List<Item> InterceptHarvest(Action harvestAction, GameLocation machineLocation)
        {
            var playerField = typeof(Game1).GetField("_player", BindingFlags.NonPublic | BindingFlags.Static);
            var player = Game1.player;
            try
            {
                collectedItems = new List<Item>();
                playerField.SetValue(null, fakeFarmer);
                fakeFarmer.currentLocation = machineLocation;

                harvestAction();
                return collectedItems;
            }
            finally
            {
                playerField.SetValue(null, player);
                collectedItems = null;
            }
        }

        private static bool TryCollect(Item item)
        {
            if (collectedItems is null)
                return false;
            AddToItemList(collectedItems, item);
            return true;
        }

        private static bool Farmer_addItemToInventoryBool_Prefix(ref bool __result, Item item)
        {
            try
            {
                if (TryCollect(item))
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModEntry.LogError("Failed in HarvestInterceptor.Farmer_addItemToInventoryBool_Prefix: " + ex);
            }

            return true;
        }

        private static bool Game1_createItemDebris_Prefix(ref Debris __result, Item item)
        {
            try
            {
                if (TryCollect(item))
                {
                    __result = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModEntry.LogError("Failed in HarvestInterceptor.Game1_createItemDebris_Prefix: " + ex);
            }

            return true;
        }

        private static bool Game1_createObjectDebris_Prefix(string id)
        {
            try
            {
                if (TryCollect(ItemRegistry.Create(id)))
                    return false;
            }
            catch (Exception ex)
            {
                ModEntry.LogError("Failed in HarvestInterceptor.Game1_createObjectDebris_Prefix: " + ex);
            }

            return true;
        }

        private static void AddToItemList(List<Item> list, Item item)
        {
            var existing = list.FirstOrDefault(x => x.ItemId == item.ItemId && x.Quality == item.Quality && ItemHelper.IsColorMatch(x, item) && x.Stack + item.Stack < MaxMergeStack);
            if (existing is not null)
                existing.Stack += item.Stack;
            else
                list.Add(item);
        }
    }
}
