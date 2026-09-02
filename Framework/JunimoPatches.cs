using System;
using HarmonyLib;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class JunimoCollisionPatch
    {
        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(HoeDirt), "isPassable", new[] { typeof(Character) }),
                prefix: new HarmonyMethod(typeof(JunimoCollisionPatch), nameof(HoeDirtIsPassable_Prefix)));
        }

        private static bool HoeDirtIsPassable_Prefix(Character c, ref bool __result)
        {
            try
            {
                if (c is GreenhouseJunimo)
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModEntry.LogError("Failed in JunimoCollisionPatch.HoeDirtIsPassable_Prefix: " + ex);
            }

            return true;
        }
    }

    internal static class PathFindPatch
    {
        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(PathFindController), "isPlayerPresent"),
                postfix: new HarmonyMethod(typeof(PathFindPatch), nameof(PathFindControllerIsPlayerPresent_Postfix)));
        }

        private static void PathFindControllerIsPlayerPresent_Postfix(PathFindController __instance, ref bool __result)
        {
            try
            {
                var character = AccessTools.Field(typeof(PathFindController), "character")?.GetValue(__instance) as Character;
                if (character is GreenhouseJunimo)
                    __result = true;
            }
            catch (Exception ex)
            {
                ModEntry.LogError("Failed in PathFindPatch.PathFindControllerIsPlayerPresent_Postfix: " + ex);
            }
        }
    }
}
