using System;
using StardewModdingAPI;

namespace Yousv.JunimaticGreenhouseWorkers
{
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
    }

    internal class ModConfigMenu
    {
        public void Entry(ModEntry mod)
        {
            var api = mod.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (api is null)
                return;

            api.Register(mod.ModManifest, () => Reset(mod), () => mod.Helper.WriteConfig(mod.Config), false);

            AddSection(api, mod, "General");
            AddBool(api, mod,
                () => mod.Config.Enabled,
                v => mod.Config.Enabled = v,
                "Enable workers",
                "Allow greenhouse workers to emerge from Junimatic portals.");
            api.AddTextOption(mod: mod.ModManifest,
                getValue: () => mod.Config.Unlock,
                setValue: v => mod.Config.Unlock = v,
                name: () => "Unlock requirement",
                tooltip: () => "Junimatic quest required before workers appear: Crops, Animals, Mining, Fishing, Forestry, IndoorPots, or Always.",
                allowedValues: ModConfig.UnlockOptions);
            api.AddNumberOption(mod: mod.ModManifest,
                getValue: () => mod.Config.JunimoSpeed,
                setValue: v => mod.Config.JunimoSpeed = v,
                name: () => "Junimo speed",
                tooltip: () => "Movement speed of greenhouse junimos (default is 3).",
                min: 1, max: 10, interval: 1);
            AddBool(api, mod,
                () => mod.Config.AllowAllLocations,
                v => mod.Config.AllowAllLocations = v,
                "Junimos work any indoors",
                "Normally Junimos only work in the greenhouse. Turning this on allows Junimo portals to work in any indoor location with tilled soil. Does not work outdoors.");

            AddSection(api, mod, "Tasks");
            AddBool(api, mod,
                () => mod.Config.PlantSeeds,
                v => mod.Config.PlantSeeds = v,
                "Plant seeds",
                "Plant the most expensive seed from a chest into empty tilled soil.");
            AddBool(api, mod,
                () => mod.Config.WaterCrops,
                v => mod.Config.WaterCrops = v,
                "Water crops",
                "Water planted soil that still needs water.");
            AddBool(api, mod,
                () => mod.Config.FertilizeCrops,
                v => mod.Config.FertilizeCrops = v,
                "Fertilize soil",
                "Apply the most expensive fertilizer from a chest to tilled soil that can accept it, including right after planting.");
            AddBool(api, mod,
                () => mod.Config.HarvestCrops,
                v => mod.Config.HarvestCrops = v,
                "Harvest crops",
                "Harvest ready crops back into a chest.");
            AddBool(api, mod,
                () => mod.Config.WaterAfterPlanting,
                v => mod.Config.WaterAfterPlanting = v,
                "Water after planting",
                "Water a seedling immediately instead of on a later trip.");
            AddBool(api, mod,
                () => mod.Config.BulkCarry,
                v => mod.Config.BulkCarry = v,
                "Bulk carry",
                "When enabled, junimo takes as many seeds/fertilizers as needed for pending work in one chest visit. (enabled by default)");
            AddBool(api, mod,
                () => mod.Config.AllowShipping,
                v => mod.Config.AllowShipping = v,
                "Allow shipping",
                "When enabled, harvested goods are shipped to a mini shipping bin in the network instead of deposited in chests.");

            AddSection(api, mod, "Debug");
            AddBool(api, mod,
                () => mod.Config.VerboseLogging,
                v => mod.Config.VerboseLogging = v,
                "Verbose logging",
                "Log detailed worker activity to the SMAPI console.");
            AddBool(api, mod,
                () => mod.Config.ShowPathDebug,
                v => mod.Config.ShowPathDebug = v,
                "Show path debug",
                "Draw walkable tiles, soil access points, chests, and the junimo's planned route when in the greenhouse.");
            AddBool(api, mod,
                () => mod.Config.ShowTaskBubbles,
                v => mod.Config.ShowTaskBubbles = v,
                "Show task bubbles",
                "Draw a speech bubble above each worker with its current task.");
            AddBool(api, mod,
                () => mod.Config.NeverDropItems,
                v => mod.Config.NeverDropItems = v,
                "Never drop items",
                "When a tile is already planted or destroyed, skip it and keep working instead of dropping carried items. Items are returned to the chest if no more work remains.");
        }

        private static void Reset(ModEntry mod)
        {
            var defaults = new ModConfig();
            mod.Config.Enabled = defaults.Enabled;
            mod.Config.ShowTaskBubbles = defaults.ShowTaskBubbles;
            mod.Config.Unlock = defaults.Unlock;
            mod.Config.PlantSeeds = defaults.PlantSeeds;
            mod.Config.WaterCrops = defaults.WaterCrops;
            mod.Config.FertilizeCrops = defaults.FertilizeCrops;
            mod.Config.HarvestCrops = defaults.HarvestCrops;
            mod.Config.WaterAfterPlanting = defaults.WaterAfterPlanting;
            mod.Config.JunimoSpeed = defaults.JunimoSpeed;
            mod.Config.BulkCarry = defaults.BulkCarry;
            mod.Config.AllowAllLocations = defaults.AllowAllLocations;
            mod.Config.WorkAroundVillagers = defaults.WorkAroundVillagers;
            mod.Config.AllowShipping = defaults.AllowShipping;
            mod.Config.VerboseLogging = defaults.VerboseLogging;
            mod.Config.ShowPathDebug = defaults.ShowPathDebug;
            mod.Config.NeverDropItems = defaults.NeverDropItems;
        }

        private static void AddSection(IGenericModConfigMenuApi api, ModEntry mod, string title)
            => api.AddSectionTitle(mod.ModManifest, () => title);

        private static void AddBool(IGenericModConfigMenuApi api, ModEntry mod, Func<bool> getValue, Action<bool> setValue, string name, string tooltip)
            => api.AddBoolOption(mod.ModManifest, getValue, setValue, () => name, () => tooltip);
    }
}
