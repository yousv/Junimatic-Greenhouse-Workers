using System;
using StardewModdingAPI;

namespace Yousv.JunimaticGreenhouseWorkers
{
    public interface IQuickSaveAPI
    {
        event SavingDelegate SavingEvent;
        bool IsSaving { get; }
        delegate void SavingDelegate(object sender, ISavingEventArgs e);
    }

    public interface ISavingEventArgs
    {
    }

    internal static class QuickSaveCompat
    {
        public const string UniqueId = "DLX.QuickSave";

        private static IQuickSaveAPI api;

        public static bool IsSaving => api?.IsSaving == true;

        public static void Entry(ModEntry mod)
        {
            if (!mod.Helper.ModRegistry.IsLoaded(UniqueId))
                return;
            api = mod.Helper.ModRegistry.GetApi<IQuickSaveAPI>(UniqueId);
            if (api is null)
                return;
            api.SavingEvent += (_, _) => mod.ClearWorkers();
        }
    }
}
