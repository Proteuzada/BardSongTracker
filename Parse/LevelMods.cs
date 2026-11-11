using System;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Level sub and div from the current ParamGrow sheet. Crit and direct hit
    /// rates use the Allagan Studies formulas.
    /// </summary>
    internal static class LevelMods
    {
        private static readonly int[] Sub =
        {
            0,
            56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116,
            122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244,
            253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 342, 344, 345, 346, 347, 349, 350, 351, 352, 354,
            355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 367, 368, 370, 372, 374, 376, 378, 380,
            382, 384, 386, 388, 390, 392, 394, 396, 398, 400, 402, 404, 406, 408, 410, 412, 414, 416, 418, 420,
        };

        private static readonly int[] Div =
        {
            0,
            56, 57, 60, 62, 65, 68, 70, 73, 76, 78, 82, 85, 89, 93, 96, 100, 104, 109, 113, 116,
            122, 127, 133, 138, 144, 150, 155, 162, 168, 173, 181, 188, 194, 202, 209, 215, 223, 229, 236, 244,
            253, 263, 272, 283, 292, 302, 311, 322, 331, 341, 366, 392, 418, 444, 470, 496, 522, 548, 574, 600,
            630, 660, 690, 720, 750, 780, 810, 840, 870, 900, 940, 980, 1020, 1060, 1100, 1140, 1180, 1220, 1260, 1300,
            1360, 1420, 1480, 1540, 1600, 1660, 1720, 1780, 1840, 1900, 1988, 2076, 2164, 2252, 2340, 2428, 2516, 2604, 2692, 2780,
        };

        public static int ClampLevel(int level)
        {
            if (level < 1 || level >= Sub.Length)
                return 100;

            return level;
        }

        public static int DefaultCrit(int level)
        {
            var used = ClampLevel(level);
            return Sub[used] + Div[used];
        }

        public static int DefaultDirect(int level)
        {
            var used = ClampLevel(level);
            return Sub[used] + (int)Math.Round(Div[used] * 200d / 550d);
        }

        public static double CritChance(int level, int crit)
        {
            var (sub, div) = Pair(level);
            var rate = (int)Math.Floor(200d * (crit - sub) / div) + 50;
            if (rate < 0)
                rate = 0;

            return rate / 1000d;
        }

        public static double CritMultiplier(int level, int crit)
        {
            var (sub, div) = Pair(level);
            var bonus = (int)Math.Floor(200d * (crit - sub) / div);
            if (bonus < 0)
                bonus = 0;

            return (1400 + bonus) / 1000d;
        }

        public static double DirectChance(int level, int direct)
        {
            var (sub, div) = Pair(level);
            var rate = (int)Math.Floor(550d * (direct - sub) / div);
            if (rate < 0)
                rate = 0;

            return rate / 1000d;
        }

        private static (int Sub, int Div) Pair(int level)
        {
            var used = ClampLevel(level);
            var div = Div[used];
            if (div <= 0)
                div = 2780;

            return (Sub[used], div);
        }
    }
}
