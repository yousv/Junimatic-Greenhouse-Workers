using Microsoft.Xna.Framework;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal enum GreenhouseTaskType
    {
        Harvest = 0,
        Fertilize = 1,
        Plant = 2,
        Water = 3
    }

    internal class PlannedTask
    {
        public GreenhouseSoil Soil { get; }
        public GreenhouseTaskType Type { get; }
        public Point Access { get; }

        public PlannedTask(GreenhouseSoil soil, GreenhouseTaskType type, Point access)
        {
            this.Soil = soil;
            this.Type = type;
            this.Access = access;
        }
    }
}
