using System;
using HarmonyLib;
using StardewModdingAPI;

namespace Yousv.JunimaticGreenhouseWorkers
{
    public class ModEntry : Mod
    {
        public static ModEntry Instance { get; private set; }

        public ModConfig Config { get; private set; }

        private Harmony harmony;
        private GreenhouseWorkFinder workFinder;

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            this.Config = helper.ReadConfig<ModConfig>();
            this.harmony = new Harmony(this.ModManifest.UniqueID);

            if (!JunimaticCompat.IsLoaded(helper))
            {
                this.Monitor.Log("Junimatic is not loaded. Junimatic Greenhouse Workers requires Junimatic to be installed.", LogLevel.Error);
                return;
            }

            TryApply(this.harmony, HarvestInterceptor.Apply, nameof(HarvestInterceptor));
            TryApply(this.harmony, JunimoCollisionPatch.Apply, nameof(JunimoCollisionPatch));
            TryApply(this.harmony, PathFindPatch.Apply, nameof(PathFindPatch));

            this.workFinder = new GreenhouseWorkFinder(this);
            this.workFinder.Entry();

            var cutscene = new CutscenePatcher();
            helper.Events.Content.AssetRequested += cutscene.OnAssetRequested;

            helper.ConsoleCommands.Add("ghw_clear", "Remove all greenhouse worker junimos and reset pending tasks.", (_, _) =>
            {
                this.workFinder?.ClearAll();
                this.Monitor.Log("Cleared greenhouse worker junimos.", LogLevel.Info);
            });

            helper.ConsoleCommands.Add("ghw_debug", "Toggle intensive junimo console logging.", (_, _) =>
            {
                this.Config.VerboseLogging = !this.Config.VerboseLogging;
                this.Helper.WriteConfig(this.Config);
                this.Monitor.Log($"Junimo verbose logging {(this.Config.VerboseLogging ? "on" : "off")}.", LogLevel.Info);
            });

            helper.Events.GameLoop.GameLaunched += (_, _) =>
            {
                var configMenu = new ModConfigMenu();
                configMenu.Entry(this);
            };
        }

        private static void TryApply(Harmony harmony, Action<Harmony> apply, string name)
        {
            try
            {
                apply(harmony);
            }
            catch (Exception ex)
            {
                LogError($"Failed to apply patch {name}: {ex}");
            }
        }

        public static void LogError(string message) => Instance?.Monitor.Log(message, LogLevel.Error);
        public static void LogWarning(string message) => Instance?.Monitor.Log(message, LogLevel.Warn);
        public static void LogJunimo(string message)
        {
            if (Instance?.Config?.VerboseLogging == true)
                Instance?.Monitor.Log("[Junimo] " + message, LogLevel.Info);
        }
    }
}
