using System;
using System.Collections.Generic;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// FF Logs stores each part of Dancing Mad as its own fight. The log keeps
    /// the same pull going, so the cut is made when the part's boss dies or the
    /// next part announces itself.
    /// </summary>
    internal static class PhasePlan
    {
        private readonly struct Step
        {
            public Step(string ladder, string[] oneOf, string[] allOf)
            {
                Ladder = ladder;
                OneOf = oneOf;
                AllOf = allOf;
            }

            public string Ladder { get; }
            public string[] OneOf { get; }
            public string[] AllOf { get; }
        }

        private static readonly Step[] DancingMad =
        {
            new("Kefka", new[] { "Kefka" }, System.Array.Empty<string>()),
            new("Forsaken Kefka", new[] { "God Kefka", "Forsaken Kefka" }, System.Array.Empty<string>()),
            new("Exdeath and Chaos", System.Array.Empty<string>(), new[] { "Exdeath", "Chaos" }),
            new("Kefka Says", new[] { "Kefka" }, System.Array.Empty<string>()),
            new("Ultima Kefka", System.Array.Empty<string>(), System.Array.Empty<string>()),
        };

        public static string Ladder(string zone, int phase)
        {
            var steps = Steps(zone);
            if (steps == null || phase < 0 || phase >= steps.Length)
                return "";

            return steps[phase].Ladder;
        }

        public static bool IsSplit(string zone, string boss)
        {
            var steps = Steps(zone);
            if (steps == null || string.IsNullOrWhiteSpace(boss))
                return false;

            foreach (var step in steps)
            {
                if (Same(step.Ladder, boss))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A cast or a new boss that means a later part has started.
        /// Returns the phase index, or -1 when this part stays put.
        /// </summary>
        public static int Signal(int phase, string enemy, string cast)
        {
            if (Later(phase, 1) && (Same(enemy, "God Kefka") || Same(enemy, "Forsaken Kefka") || Same(cast, "Ultimate Embrace")))
                return 1;
            if (Later(phase, 2) && (Same(enemy, "Exdeath") || Same(enemy, "Chaos") || Same(cast, "Definition of Insanity") || Same(cast, "The Decisive Battle")))
                return 2;
            if (Later(phase, 3) && Same(cast, "Kefka Says"))
                return 3;
            if (Later(phase, 4) && Same(cast, "Ultima Repeater"))
                return 4;

            return -1;
        }

        /// <summary>
        /// Records a phase boss that died or left. Returns the next phase when
        /// this part is over, or -1 while it is still going.
        /// </summary>
        public static int Down(string zone, int phase, string enemy, ISet<string> down)
        {
            var steps = Steps(zone);
            if (steps == null || phase < 0 || phase >= steps.Length || string.IsNullOrWhiteSpace(enemy))
                return -1;

            var step = steps[phase];
            if (!Marks(step, enemy))
                return -1;

            down.Add(Key(enemy));
            if (!Done(step, down))
                return -1;

            return phase + 1 < steps.Length ? phase + 1 : -1;
        }

        public static string? SelfCheck()
        {
            if (Ladder("Dancing Mad (Ultimate)", 0) != "Kefka")
                return "p1 name";
            if (Ladder("Dancing Mad (Ultimate)", 2) != "Exdeath and Chaos")
                return "p3 name";
            if (Ladder("Dancing Mad (Ultimate)", 4) != "Ultima Kefka")
                return "p5 name";
            if (Ladder("The Epic of Alexander (Ultimate)", 0).Length != 0)
                return "tea split";
            if (Signal(0, "Kefka", "") != -1)
                return "p1 hit";
            if (Signal(0, "God Kefka", "") != 1)
                return "god kefka";
            if (Signal(0, "Exdeath", "") != 2)
                return "exdeath appear";
            if (Signal(1, "Exdeath", "") != 2)
                return "exdeath from p2";
            if (Signal(2, "", "Kefka Says") != 3)
                return "kefka says";
            if (Signal(3, "", "Ultima Repeater") != 4)
                return "ultima";
            if (Signal(4, "", "Ultima Repeater") != -1)
                return "ultima again";

            var down = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Down("Dancing Mad (Ultimate)", 0, "Kefka", down) != 1)
                return "p1 dead";
            down.Clear();
            if (Down("Dancing Mad (Ultimate)", 2, "Exdeath", down) != -1)
                return "one add";
            if (Down("Dancing Mad (Ultimate)", 2, "Neo Exdeath", down) != -1)
                return "neo";
            if (Down("Dancing Mad (Ultimate)", 2, "Chaos", down) != 3)
                return "both adds";
            if (!IsSplit("Dancing Mad (Ultimate)", "Kefka Says"))
                return "split name";
            if (IsSplit("Dancing Mad (Ultimate)", "Dancing Mad"))
                return "whole name";

            return null;
        }

        private static bool Later(int phase, int target)
        {
            return target > phase;
        }

        private static Step[]? Steps(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return null;

            return zone.Contains("Dancing Mad", StringComparison.OrdinalIgnoreCase) ? DancingMad : null;
        }

        private static bool Marks(Step step, string enemy)
        {
            foreach (var name in step.OneOf)
            {
                if (Same(name, enemy))
                    return true;
            }

            foreach (var name in step.AllOf)
            {
                if (Same(name, enemy))
                    return true;
            }

            return false;
        }

        private static bool Done(Step step, ISet<string> down)
        {
            if (step.AllOf.Length > 0)
            {
                foreach (var name in step.AllOf)
                {
                    if (!down.Contains(Key(name)))
                        return false;
                }

                return true;
            }

            if (step.OneOf.Length == 0)
                return false;

            foreach (var name in step.OneOf)
            {
                if (down.Contains(Key(name)))
                    return true;
            }

            return false;
        }

        private static string Key(string name)
        {
            return name.Trim();
        }

        private static bool Same(string left, string right)
        {
            return left.Length > 0
                && right.Length > 0
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
