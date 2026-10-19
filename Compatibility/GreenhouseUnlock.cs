using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class GreenhouseUnlock
    {
        public static bool IsActive(ModConfig config)
        {
            if (!config.ModEnabled || !config.Enabled)
                return false;
            return JunimaticCompat.IsModeUnlocked(config.Unlock);
        }
    }

    internal class CutscenePatcher
    {
        private sealed record ModePatch(string Mode, string Asset, string EventKey, string Original, string Patched);

        private static readonly ModePatch[] Patches =
        {
            new(
                "Crops",
                "Data/Events/Farm",
                "Junimatic.CropMachineHelper.GiantCropCelebration/H/sawEvent Junimatic.JunimoPortalDiscoveryEvent/Junimatic.GiantCropIsGrowingOnFarm",
                "One of us will come and help with your kegs, casks and preserves jars if you connect them to a portal!",
                "One of us will come and help with your kegs, casks and preserves jars^and your greenhouse soil if you place a portal inside!"),
            new(
                "Animals",
                "Data/Events/FarmHouse",
                "Junimatic.AnimalJunimoDreamEvent/H/sawEvent Junimatic.GivePortalForJunimoEvent/time 600 620",
                "I like animals and the wonderful things you can make with their help...",
                "I like animals and the wonderful things you can make with their help...^The animal junimo will also tend your greenhouse soil^if you place a portal inside!"),
            new(
                "Mining",
                "Data/Events/FarmHouse",
                "Junimatic.MiningJunimoDreamEvent/H/sawEvent Junimatic.ReturnJunimoOrbEvent/time 600 620",
                "I am so proud of you...",
                "I am so proud of you...#$b#The mining junimo will also tend your greenhouse soil if you place a portal inside!"),
            new(
                "Forestry",
                "Data/Events/Farm",
                "Junimatic.MysticTreeCelebration/H/sawEvent Junimatic.LinusCamping/Junimatic.IsMysticTreeGrownOnFarm",
                "You will be wiser.  We will help.",
                "You will be wiser.  We will help.^The forestry junimo will also tend your greenhouse soil^if you place a portal inside!"),
            new(
                "IndoorPots",
                "Data/Events/FarmHouse",
                "Junimatic.PotJunimoThankYou/H/t 600 620/ActiveDialogueEvent Junimatic.LewisGotPlant",
                "Since you helped Lewis, I will help with your indoor plants...",
                "Since you helped Lewis, I will help with your indoor plants...^The indoor pot junimo will also tend your greenhouse soil^if you place a portal inside!"),
        };

        private readonly ModEntry mod;

        public CutscenePatcher(ModEntry mod)
        {
            this.mod = mod;
        }

        public void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (!this.mod.Config.ModEnabled || !this.mod.Config.Enabled)
                return;

            foreach (var patch in Patches)
            {
                if (!e.NameWithoutLocale.IsEquivalentTo(patch.Asset) || !this.IsSelected(patch.Mode))
                    continue;

                e.Edit(editor =>
                {
                    var data = editor.AsDictionary<string, string>().Data;
                    if (!data.TryGetValue(patch.EventKey, out string script) || script.Contains(patch.Patched))
                        return;

                    data[patch.EventKey] = script.Replace(patch.Original, patch.Patched);
                }, AssetEditPriority.Late);
            }
        }

        public void InvalidateCache()
        {
            this.mod.Helper.GameContent.InvalidateCache("Data/Events/Farm");
            this.mod.Helper.GameContent.InvalidateCache("Data/Events/FarmHouse");
        }

        private bool IsSelected(string mode)
        {
            return string.Equals(this.mod.Config.Unlock, mode, StringComparison.OrdinalIgnoreCase);
        }
    }
}
