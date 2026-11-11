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
