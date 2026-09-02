namespace Yousv.JunimaticGreenhouseWorkers
{
    public class ModConfig
    {
        public bool Enabled { get; set; } = true;
        public bool ShowTaskBubbles { get; set; } = false;
        public string Unlock { get; set; } = "Crops";

        public bool PlantSeeds { get; set; } = true;
        public bool WaterCrops { get; set; } = true;
        public bool FertilizeCrops { get; set; } = true;
        public bool HarvestCrops { get; set; } = true;
        public bool WaterAfterPlanting { get; set; } = true;

        public int JunimoSpeed { get; set; } = 3;

        public bool BulkCarry { get; set; } = true;

        public bool AllowAllLocations { get; set; } = false;
        public bool WorkAroundVillagers { get; set; } = false;
        public bool AllowShipping { get; set; } = false;

        public bool VerboseLogging { get; set; } = false;
        public bool ShowPathDebug { get; set; } = false;
        public bool NeverDropItems { get; set; } = false;

        public static readonly string[] UnlockOptions =
        {
            "Crops", "Animals", "Mining", "Fishing", "Forestry", "IndoorPots", "Always"
        };
    }
}
