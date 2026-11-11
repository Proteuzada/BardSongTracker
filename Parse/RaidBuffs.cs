using System.Collections.Generic;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// External raid buffs FF Logs moves between players. Potencies are the live
    /// tooltips: songs, Battle Voice, Litany, and the published 2% per Radiant
    /// Finale coda. Brotherhood, Technical Finish, Divination, and the rest use
    /// the same fractions the current raid-buff sims publish.
    /// </summary>
    internal static class RaidBuffs
    {
        internal readonly record struct Spec(double Damage, double Crit, double Direct, bool MagicOnly);

        private static readonly Dictionary<int, Spec> Known = new()
        {
            [2217] = new(0.01, 0, 0, false),
            [2218] = new(0, 0, 0.03, false),
            [2216] = new(0, 0.02, 0, false),
            [141] = new(0, 0, 0.20, false),
            [2964] = new(0.02, 0, 0, false),
            [786] = new(0, 0.10, 0, false),
            [1185] = new(0.05, 0, 0, false),
            [2599] = new(0.03, 0, 0, false),
            [2703] = new(0.05, 0, 0, false),
            [1297] = new(0.05, 0, 0, false),
            [3685] = new(0.05, 0, 0, false),
            [1821] = new(0.05, 0, 0, false),
            [1822] = new(0.05, 0, 0, false),
            [1825] = new(0, 0.20, 0.20, false),
            [1221] = new(0, 0.10, 0, false),
            [1878] = new(0.06, 0, 0, false),
            [3849] = new(0.05, 0, 0, false),
            [1717] = new(0.05, 0, 0, false),
            [1721] = new(0.05, 0, 0, true),
            [1882] = new(0.06, 0, 0, false),
            [1883] = new(0.06, 0, 0, false),
            [1884] = new(0.06, 0, 0, false),
            [1885] = new(0.06, 0, 0, false),
            [1886] = new(0.06, 0, 0, false),
            [1887] = new(0.06, 0, 0, false),
            [1876] = new(0.06, 0, 0, false),
            [1877] = new(0.06, 0, 0, false),
            [3887] = new(0.06, 0, 0, false),
            [3889] = new(0.06, 0, 0, false),
        };

        public static bool TryResolve(int statusId, int stacks, out Spec spec)
        {
            if (!Known.TryGetValue(statusId, out spec))
                return false;

            if (statusId != 2964)
                return spec.Damage > 0 || spec.Crit > 0 || spec.Direct > 0;

            var codas = stacks is >= 1 and <= 3 ? stacks : 3;
            spec = spec with { Damage = 0.02 * codas };
            return true;
        }

        public static bool IsMagicJob(int jobId)
        {
            return jobId is 24 or 28 or 33 or 40 or 25 or 27 or 35 or 42 or 36;
        }
    }
}
