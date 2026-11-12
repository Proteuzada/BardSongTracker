using System;
using System.Collections.Generic;
using System.Numerics;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// FFLogs does not store a rank for every score. It caches the DPS at fixed
    /// percentiles and places a new score by linear interpolation between the two
    /// nearest cached values. A score halfway between the 95% and 99% DPS values
    /// is a 97. The displayed percent is rounded down.
    /// </summary>
    public static class PercentileScale
    {
        public readonly record struct Point(int Percentile, double Dps);

        /// <summary>
        /// Rank 1 is the best log. A rank's parse is the site's
        /// floor(100 * (1 - (rank - 1) / count)).
        /// </summary>
        public static int RankFor(int percentile, int count)
        {
            if (count < 1 || percentile >= 100)
                return 1;

            var rank = 1 + ((100 - percentile) * (long)count) / 100;
            if (rank < 1)
                rank = 1;
            if (rank > count)
                rank = count;

            return (int)rank;
        }

        public static int PercentOf(int rank, int count)
        {
            if (count < 1 || rank <= 1)
                return 100;

            var value = (100L * (count - rank + 1)) / count;
            if (value > 100)
                value = 100;
            if (value < 0)
                value = 0;
            return (int)value;
        }

        public static string? SelfCheck()
        {
            if (RankFor(100, 10000) != 1)
                return "best rank";
            if (RankFor(95, 1000) != 51 || PercentOf(51, 1000) != 95)
                return "p95";
            if (RankFor(10, 1000) != 901 || PercentOf(901, 1000) != 10)
                return "p10";

            // The first page of rankings is the top logs. Treating that page as the
            // whole ladder labels one of those top logs as the 10% mark, so a 95
            // and a 35 both draw between 0 and 10.
            var collapsed = new List<Point> { new(0, 0), new(10, 45000), new(100, 48000) };
            if (Interpolate(38000, collapsed) != 8)
                return "collapsed " + Interpolate(38000, collapsed);

            var ladder = new List<Point>
            {
                new(0, 0),
                new(10, 18000),
                new(50, 28000),
                new(90, 35000),
                new(95, 37000),
                new(99, 42000),
                new(100, 45000),
            };
            var parse = Interpolate(38000, ladder);
            if (parse < 95 || parse > 97)
                return "ladder " + parse;

            return null;
        }

        public static int Interpolate(double dps, IReadOnlyList<Point> points)
        {
            if (points == null || points.Count == 0 || dps <= 0)
                return 0;

            if (dps <= points[0].Dps)
                return Math.Clamp(points[0].Percentile, 0, 100);

            var last = points[points.Count - 1];
            if (dps >= last.Dps)
                return Math.Clamp(last.Percentile, 0, 100);

            for (var i = 1; i < points.Count; i++)
            {
                var low = points[i - 1];
                var high = points[i];
                if (dps > high.Dps)
                    continue;

                var span = high.Dps - low.Dps;
                var t = span <= 0 ? 0 : (dps - low.Dps) / span;
                var value = low.Percentile + ((high.Percentile - low.Percentile) * t);
                return Math.Clamp((int)Math.Floor(value), 0, 100);
            }

            return Math.Clamp(last.Percentile, 0, 100);
        }

        public static Vector4 Color(int percentile)
        {
            if (percentile >= 100)
                return new Vector4(0.90f, 0.80f, 0.50f, 1f);
            if (percentile >= 99)
                return new Vector4(0.89f, 0.41f, 0.66f, 1f);
            if (percentile >= 95)
                return new Vector4(1.00f, 0.50f, 0.00f, 1f);
            if (percentile >= 75)
                return new Vector4(0.64f, 0.21f, 0.93f, 1f);
            if (percentile >= 50)
                return new Vector4(0.00f, 0.44f, 1.00f, 1f);
            if (percentile >= 25)
                return new Vector4(0.12f, 1.00f, 0.00f, 1f);
            return new Vector4(0.62f, 0.62f, 0.62f, 1f);
        }
    }
}
