using StardewModdingAPI;
using StardewValley;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class JunimaticCompat
    {
        public const string JunimoPortalQiid = "(BC)Junimatic.JunimoPortal";
        public const string UniqueId = "NermNermNerm.Junimatic";

        public const string GiantCropCelebrationEventId = "Junimatic.CropMachineHelper.GiantCropCelebration";
        public const string MiningJunimoDreamEvent = "Junimatic.MiningJunimoDreamEvent";
        public const string AnimalJunimoDreamEvent = "Junimatic.AnimalJunimoDreamEvent";
        public const string MysticTreeCelebrationEvent = "Junimatic.MysticTreeCelebration";
        public const string PotJunimoThankYouEvent = "Junimatic.PotJunimoThankYou";
        public const string FishingIcePipsModData = "Junimatic.HasDoneIcePipsQuestModDataKey";

        public static bool IsLoaded(IModHelper helper) => helper.ModRegistry.IsLoaded(UniqueId);

        public static bool IsModeUnlocked(string mode)
        {
            var player = Game1.MasterPlayer;
            bool seen(string eventId) => player is not null && player.eventsSeen.Contains(eventId);

            return mode?.Trim().ToLowerInvariant() switch
            {
                "always" => true,
                "crops" => seen(GiantCropCelebrationEventId),
                "mining" => seen(MiningJunimoDreamEvent),
                "animals" => seen(AnimalJunimoDreamEvent),
                "forestry" => seen(MysticTreeCelebrationEvent),
                "indoorpots" => seen(PotJunimoThankYouEvent),
                "fishing" => player is not null && player.modData.ContainsKey(FishingIcePipsModData),
                _ => seen(GiantCropCelebrationEventId)
            };
        }
    }
}
