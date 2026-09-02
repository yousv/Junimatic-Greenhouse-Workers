using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class GreenhouseUnlock
    {
        public static bool IsActive(ModConfig config)
        {
            if (!config.Enabled)
                return false;
            return JunimaticCompat.IsModeUnlocked(config.Unlock);
        }
    }

    internal class CutscenePatcher
    {
        private const string CropsOriginalLine = "spriteText 4 \"One of us will come and help with your kegs, casks and preserves jars if you connect them to a portal!\"";

        private sealed record ModePatch(string Asset, string EventKey, string Line, string ReplaceLine = null);

        private static readonly List<ModePatch> Patches = new List<ModePatch>
        {
            new ModePatch(
                "Data/Events/Farm",
                "Junimatic.CropMachineHelper.GiantCropCelebration/H/sawEvent Junimatic.JunimoPortalDiscoveryEvent/Junimatic.GiantCropIsGrowingOnFarm",
                "spriteText 4 \"One of us will come and help with your kegs, casks and preserves jars^and your greenhouse soil if you place a portal inside!\"",
                CropsOriginalLine),
            new ModePatch(
                "Data/Events/FarmHouse",
                "Junimatic.MiningJunimoDreamEvent/H/sawEvent Junimatic.ReturnJunimoOrbEvent/time 600 620",
                "spriteText 4 \"The mining junimo will also tend your greenhouse soil^if you place a portal inside!\""),
            new ModePatch(
                "Data/Events/FarmHouse",
                "Junimatic.AnimalJunimoDreamEvent/H/sawEvent Junimatic.GivePortalForJunimoEvent/time 600 620",
                "spriteText 4 \"The animal junimo will also tend your greenhouse soil^if you place a portal inside!\""),
            new ModePatch(
                "Data/Events/Farm",
                "Junimatic.MysticTreeCelebration/H/sawEvent Junimatic.LinusCamping/Junimatic.IsMysticTreeGrownOnFarm",
                "spriteText 4 \"The forestry junimo will also tend your greenhouse soil^if you place a portal inside!\""),
            new ModePatch(
                "Data/Events/Farmhouse",
                "Junimatic.PotJunimoThankYou/H/t 600 620/ActiveDialogueEvent Junimatic.LewisGotPlant",
                "spriteText 4 \"The indoor pot junimo will also tend your greenhouse soil^if you place a portal inside!\""),
        };

        public void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            foreach (var patch in Patches)
            {
                if (!e.NameWithoutLocale.IsEquivalentTo(patch.Asset))
                    continue;

                e.Edit(editor =>
                {
                    var data = editor.AsDictionary<string, string>().Data;
                    if (!data.TryGetValue(patch.EventKey, out string script))
                        return;
                    if (script.Contains(patch.Line))
                        return;

                    if (patch.ReplaceLine is not null && script.Contains(patch.ReplaceLine))
                        data[patch.EventKey] = script.Replace(patch.ReplaceLine, patch.Line);
                    else
                        data[patch.EventKey] = AppendBeforeEnd(script, patch.Line);
                });
            }
        }

        private static string AppendBeforeEnd(string script, string line)
        {
            int idx = script.LastIndexOf("\nend");
            if (idx < 0)
                idx = script.LastIndexOf(" end");
            if (idx < 0)
                return script + "\n" + line;

            return script.Insert(idx + 1, line + "\n");
        }
    }
}
