using System;
using System.Collections.Generic;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// FF Logs rDPS split. Percentage buffs are removed with log weights.
    /// Crit and direct hit buffs take their share of that portion of the hit.
    /// </summary>
    internal static class RdpsMath
    {
        public readonly record struct External(string SourceId, double Damage, double Crit, double Direct);

        public static void Attribute(
            double amount,
            bool crit,
            bool direct,
            double critChance,
            double critMultiplier,
            double directChance,
            IReadOnlyList<External> buffs,
            string dealerId,
            Action<string, double> add)
        {
            if (amount <= 0 || dealerId.Length == 0)
                return;

            add(dealerId, amount);
            if (buffs == null || buffs.Count == 0)
                return;

            var multiplier = 1d;
            var damageBuffs = 0;
            foreach (var buff in buffs)
            {
                if (buff.Damage <= 0)
                    continue;

                multiplier *= 1d + buff.Damage;
                damageBuffs++;
            }

            if (damageBuffs > 0 && multiplier > 1d)
            {
                var lost = amount - (amount / multiplier);
                var logTotal = Math.Log(multiplier);
                if (lost > 0 && logTotal > 0)
                {
                    foreach (var buff in buffs)
                    {
                        if (buff.Damage <= 0 || buff.SourceId.Length == 0)
                            continue;

                        Give(add, dealerId, buff.SourceId, lost * (Math.Log(1d + buff.Damage) / logTotal));
                    }
                }
            }

            var kept = multiplier > 1d ? amount / multiplier : amount;
            var critRate = critChance;
            var directRate = directChance;
            if (critRate < 0)
                critRate = 0;
            if (directRate < 0)
                directRate = 0;

            foreach (var buff in buffs)
            {
                if (buff.Crit > 0)
                    critRate += buff.Crit;
                if (buff.Direct > 0)
                    directRate += buff.Direct;
            }

            if (critRate > 1d)
                critRate = 1d;
            if (directRate > 1d)
                directRate = 1d;

            var critBonus = critMultiplier < 1d ? 1d : critMultiplier;
            var combined = 1d;
            if (crit && direct)
                combined = critBonus * 1.25d;
            else if (crit)
                combined = critBonus;
            else if (direct)
                combined = 1.25d;

            if (combined <= 1d)
                return;

            var bonus = kept - (kept / combined);
            if (bonus <= 0)
                return;

            var critPortion = 0d;
            var directPortion = 0d;
            if (crit && direct)
            {
                var logCombined = Math.Log(combined);
                if (logCombined > 0)
                {
                    critPortion = Math.Log(critBonus) / logCombined * bonus;
                    directPortion = Math.Log(1.25d) / logCombined * bonus;
                }
            }
            else if (crit)
            {
                critPortion = bonus;
            }
            else if (direct)
            {
                directPortion = bonus;
            }

            if (critPortion > 0 && critRate > 0)
            {
                foreach (var buff in buffs)
                {
                    if (buff.Crit <= 0 || buff.SourceId.Length == 0)
                        continue;

                    Give(add, dealerId, buff.SourceId, buff.Crit / critRate * critPortion);
                }
            }

            if (directPortion > 0 && directRate > 0)
            {
                foreach (var buff in buffs)
                {
                    if (buff.Direct <= 0 || buff.SourceId.Length == 0)
                        continue;

                    Give(add, dealerId, buff.SourceId, buff.Direct / directRate * directPortion);
                }
            }
        }

        public static string? SelfCheck()
        {
            var totals = new Dictionary<string, double>();
            var buffs = new List<External>
            {
                new("Mary", 0.10, 0, 0),
                new("Alice", 0.10, 0, 0),
            };
            Attribute(121, false, false, 0.25, 1.6, 0.2, buffs, "Bob", (id, value) =>
            {
                totals.TryGetValue(id, out var soFar);
                totals[id] = soFar + value;
            });

            if (Math.Abs(totals["Bob"] - 100d) > 0.01)
                return "dealer kept " + totals["Bob"];
            if (Math.Abs(totals["Mary"] - 10.5d) > 0.01 || Math.Abs(totals["Alice"] - 10.5d) > 0.01)
                return "buffers " + totals["Mary"] + " " + totals["Alice"];

            totals.Clear();
            Attribute(1000, true, false, 0.25, 1.6, 0.2, new List<External> { new("Litany", 0, 0.10, 0) }, "Sam", (id, value) =>
            {
                totals.TryGetValue(id, out var soFar);
                totals[id] = soFar + value;
            });
            if (Math.Abs(totals["Litany"] - (0.10 / 0.35 * 375d)) > 0.01)
                return "litany " + totals["Litany"];

            var chance = LevelMods.CritChance(100, 420 + 2780);
            if (Math.Abs(chance - 0.25d) > 0.0001)
                return "crit chance " + chance;

            return null;
        }

        private static void Give(Action<string, double> add, string dealerId, string sourceId, double amount)
        {
            if (amount == 0 || sourceId.Length == 0)
                return;

            add(dealerId, -amount);
            add(sourceId, amount);
        }
    }
}
