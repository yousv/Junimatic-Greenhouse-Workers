using StardewValley;
using StardewValley.Objects;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal static class ItemHelper
    {
        internal static bool IsColorMatch(Item a, Item b)
        {
            if (a is ColoredObject ca)
                return b is ColoredObject cb && ca.color.Value == cb.color.Value;
            return b is not ColoredObject;
        }

        internal static Item ConsumeOne(this Item item)
        {
            if (item is null || item.Stack <= 0)
                return null;
            var drop = item.getOne();
            if (drop is null)
                return null;
            drop.Stack = 1;
            item.Stack--;
            return drop;
        }
    }
}
