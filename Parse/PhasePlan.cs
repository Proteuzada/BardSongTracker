using System;
using System.Collections.Generic;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// The log keeps one pull running. FF Logs ranks some ultimates as one
    /// encounter and others as a ladder per part. The meter follows that split.
    /// </summary>
    internal static class PhasePlan
    {
        private readonly struct Cue
        {
            public Cue(string name, bool cast = false, int minPhase = 0)
            {
                Name = name;
                Cast = cast;
                MinPhase = minPhase;
            }

            public string Name { get; }
            public bool Cast { get; }
            public int MinPhase { get; }
        }

        private readonly struct Step
        {
            public Step(string ladder, string[] oneOf, string[] allOf, Cue[] open)
            {
                Ladder = ladder;
                OneOf = oneOf;
                AllOf = allOf;
                Open = open;
            }

            public string Ladder { get; }
            public string[] OneOf { get; }
            public string[] AllOf { get; }
            public Cue[] Open { get; }
        }

        private static readonly string[] None = System.Array.Empty<string>();
        private static readonly Cue[] NoCue = System.Array.Empty<Cue>();

        private static readonly Step[] DancingMad =
        {
            new("Kefka", new[] { "Kefka" }, None, NoCue),
            new("Forsaken Kefka", new[] { "God Kefka", "Forsaken Kefka" }, None, new[]
            {
                new Cue("God Kefka"),
                new Cue("Forsaken Kefka"),
                new Cue("Ultimate Embrace", cast: true),
            }),
            new("Exdeath and Chaos", None, new[] { "Exdeath", "Chaos" }, new[]
            {
                new Cue("Exdeath"),
                new Cue("Chaos"),
                new Cue("Definition of Insanity", cast: true),
                new Cue("The Decisive Battle", cast: true),
            }),
            new("Kefka Says", new[] { "Kefka" }, None, new[] { new Cue("Kefka Says", cast: true) }),
            new("Ultima Kefka", None, None, new[] { new Cue("Ultima Repeater", cast: true) }),
        };

        private static readonly Step[] FuturesRewritten =
        {
            new("Fatebreaker", new[] { "Fatebreaker" }, None, NoCue),
            new("Usurper of Frost", None, None, new[] { new Cue("Usurper of Frost") }),
            new("Oracle of Darkness", None, None, new[] { new Cue("Oracle of Darkness", minPhase: 1) }),
            new("Enter the Dragon", None, None, new[]
            {
                new Cue("Usurper of Frost", minPhase: 2),
                new Cue("Darklit Dragonsong", cast: true, minPhase: 2),
                new Cue("Materialization", cast: true, minPhase: 2),
            }),
            new("Pandora", None, None, new[] { new Cue("Pandora") }),
        };

        private static readonly Step[] OmegaProtocol =
        {
            new("Omega", None, None, NoCue),
            new("Omega-M/F", None, None, new[] { new Cue("Omega-M"), new Cue("Omega-F") }),
            new("Omega Reconfigured", None, None, new[] { new Cue("Omega", minPhase: 1) }),
            new("Blue Screen", None, None, new[] { new Cue("Wave Cannon", cast: true, minPhase: 2) }),
            new("Run: Dynamis", None, None, new[]
            {
                new Cue("Omega-M", minPhase: 3),
                new Cue("Omega-F", minPhase: 3),
                new Cue("Run: Dynamis", cast: true, minPhase: 2),
                new Cue("Run Dynamis", cast: true, minPhase: 2),
            }),
            new("Alpha Omega", None, None, new[] { new Cue("Alpha Omega", minPhase: 4) }),
        };

        private static readonly Step[] Dragonsong =
        {
            new("Adelphel, Grinnaux and Charibert", None, None, NoCue),
            new("King Thordan", None, None, new[] { new Cue("King Thordan") }),
            new("Nidhogg", None, None, new[] { new Cue("Nidhogg", minPhase: 1) }),
            new("The Eyes", None, new[] { "Left Eye", "Right Eye" }, new[]
            {
                new Cue("Left Eye", minPhase: 2),
                new Cue("Right Eye", minPhase: 2),
            }),
            new("Intermission: Rewind!", None, None, NoCue),
            new("King Thordan II", None, None, new[] { new Cue("King Thordan", minPhase: 2) }),
            new("Nidhogg and Hraesvelgr", None, None, new[]
            {
                new Cue("Hraesvelgr", minPhase: 5),
                new Cue("Nidhogg", minPhase: 5),
            }),
            new("The Dragon King", None, None, new[] { new Cue("Dragon-King Thordan", minPhase: 5) }),
        };

        private static readonly Step[] EpicOfAlexander =
        {
            new("Living Liquid", None, None, NoCue),
            new("Brute Justice and Cruise Chaser", None, new[] { "Brute Justice", "Cruise Chaser" }, new[]
            {
                new Cue("Brute Justice"),
                new Cue("Cruise Chaser"),
            }),
            new("Alexander Prime", new[] { "Alexander Prime" }, None, new[] { new Cue("Alexander Prime", minPhase: 1) }),
            new("Perfect Alexander", None, None, new[]
            {
                new Cue("Perfect Alexander", minPhase: 2),
                new Cue("The Final Word", cast: true, minPhase: 2),
            }),
        };

        private static readonly Step[] WeaponsRefrain =
        {
            new("Garuda", new[] { "Garuda" }, None, NoCue),
            new("Ifrit", new[] { "Ifrit" }, None, new[] { new Cue("Ifrit") }),
            new("Titan", new[] { "Titan" }, None, new[] { new Cue("Titan", minPhase: 1) }),
            new("Ultima", None, None, new[]
            {
                new Cue("Ultima", minPhase: 2),
                new Cue("Ultima, the High Seraph", minPhase: 2),
            }),
            new("Ultimate Predation", None, None, new[]
            {
                new Cue("The Ultima Weapon", minPhase: 3),
                new Cue("Ultima Weapon", minPhase: 3),
            }),
            new("Ultimate Annihilation", None, None, new[] { new Cue("Ultimate Annihilation", cast: true, minPhase: 4) }),
            new("Ultimate Suppression", None, None, new[] { new Cue("Ultimate Suppression", cast: true, minPhase: 4) }),
        };

        private static readonly Step[] UnendingCoil =
        {
            new("Twintania", new[] { "Twintania" }, None, NoCue),
            new("Nael deus Darnus", new[] { "Nael deus Darnus" }, None, new[]
            {
                new Cue("Nael deus Darnus"),
                new Cue("Nael Deus Darnus"),
            }),
            new("Bahamut Prime", new[] { "Bahamut Prime" }, None, new[] { new Cue("Bahamut Prime", minPhase: 1) }),
            new("Adds", None, None, new[] { new Cue("Quickmarch Trio", cast: true, minPhase: 2) }),
            new("Golden Bahamut", None, None, new[] { new Cue("Golden Bahamut", minPhase: 2) }),
        };

        public static string[] Names(string zone)
        {
            var steps = Steps(zone);
            if (steps == null)
                return System.Array.Empty<string>();

            var names = new string[steps.Length];
            for (var i = 0; i < steps.Length; i++)
                names[i] = steps[i].Ladder;
            return names;
        }

        public static string Ladder(string zone, int phase)
        {
            var steps = Steps(zone);
            if (steps == null || phase < 0 || phase >= steps.Length)
                return "";

            return steps[phase].Ladder;
        }

        /// <summary>
        /// The current FF Logs boss menu lists these three with no phase submenu:
        /// The Unending Coil of Bahamut (1073), The Weapon's Refrain (1074),
        /// and The Epic of Alexander (1075). Dancing Mad, Futures Rewritten,
        /// Dragonsong's Reprise, and The Omega Protocol list each part.
        /// </summary>
        public static bool SingleLadder(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return false;

            return zone.Contains("Unending Coil", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Weapon's Refrain", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Weapons Refrain", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Epic of Alexander", StringComparison.OrdinalIgnoreCase);
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
        public static int Signal(string zone, int phase, string enemy, string cast)
        {
            var steps = Steps(zone);
            if (steps == null)
                return -1;

            for (var index = phase + 1; index < steps.Length; index++)
            {
                foreach (var cue in steps[index].Open)
                {
                    if (phase < cue.MinPhase)
                        continue;
                    if (Same(cue.Cast ? cast : enemy, cue.Name))
                        return index;
                }
            }

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
            if (Names("Dancing Mad (Ultimate)").Length != 5)
                return "dancing names";
            if (Names("The Omega Protocol (Ultimate)").Length != 6)
                return "top names";
            if (Names("The Dead Ends").Length != 0)
                return "dungeon names";
            if (Ladder("Dancing Mad (Ultimate)", 0) != "Kefka")
                return "p1 name";
            if (Ladder("Dancing Mad (Ultimate)", 4) != "Ultima Kefka")
                return "p5 name";
            if (Ladder("The Dead Ends", 0).Length != 0)
                return "dungeon split";
            if (Signal("Dancing Mad (Ultimate)", 0, "Kefka", "") != -1)
                return "p1 hit";
            if (Signal("Dancing Mad (Ultimate)", 0, "God Kefka", "") != 1)
                return "god kefka";
            if (Signal("Dancing Mad (Ultimate)", 0, "Exdeath", "") != 2)
                return "exdeath appear";
            if (Signal("Dancing Mad (Ultimate)", 3, "", "Ultima Repeater") != 4)
                return "ultima";
            if (Signal("Dancing Mad (Ultimate)", 4, "", "Ultima Repeater") != -1)
                return "ultima again";
            if (Signal("The Weapon's Refrain (Ultimate)", 0, "", "Ultima Repeater") != -1)
                return "foreign cast";

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

            if (Ladder("Futures Rewritten (Ultimate)", 0) != "Fatebreaker")
                return "fru p1";
            if (Ladder("Futures Rewritten (Ultimate)", 3) != "Enter the Dragon")
                return "fru p4";
            if (Signal("Futures Rewritten (Ultimate)", 0, "Usurper of Frost", "") != 1)
                return "usurper";
            if (Signal("Futures Rewritten (Ultimate)", 1, "Usurper of Frost", "") != -1)
                return "usurper stay";
            if (Signal("Futures Rewritten (Ultimate)", 2, "Usurper of Frost", "") != 3)
                return "enter the dragon";
            if (Signal("Futures Rewritten (Ultimate)", 0, "Pandora", "") != 4)
                return "pandora jump";

            if (Ladder("The Omega Protocol (Ultimate)", 1) != "Omega-M/F")
                return "top p2";
            if (Signal("The Omega Protocol (Ultimate)", 0, "Omega", "") != -1)
                return "omega stay";
            if (Signal("The Omega Protocol (Ultimate)", 0, "Omega-M", "") != 1)
                return "omega m";
            if (Signal("The Omega Protocol (Ultimate)", 1, "Omega-M", "") != -1)
                return "omega m stay";
            if (Signal("The Omega Protocol (Ultimate)", 1, "Omega", "") != 2)
                return "reconfigured";
            if (Signal("The Omega Protocol (Ultimate)", 2, "", "Wave Cannon") != 3)
                return "blue screen";
            if (Signal("The Omega Protocol (Ultimate)", 2, "", "Oversampled Wave Cannon") != -1)
                return "oversampled";
            if (Signal("The Omega Protocol (Ultimate)", 3, "Omega-M", "") != 4)
                return "dynamis";
            if (Signal("The Omega Protocol (Ultimate)", 4, "Alpha Omega", "") != 5)
                return "alpha";

            if (Ladder("Dragonsong's Reprise (Ultimate)", 0) != "Adelphel, Grinnaux and Charibert")
                return "dsr p1";
            if (Ladder("Dragonsong's Reprise (Ultimate)", 7) != "The Dragon King")
                return "dsr p7";
            if (Signal("Dragonsong's Reprise (Ultimate)", 0, "King Thordan", "") != 1)
                return "thordan";
            if (Signal("Dragonsong's Reprise (Ultimate)", 1, "King Thordan", "") != -1)
                return "thordan stay";
            if (Signal("Dragonsong's Reprise (Ultimate)", 2, "King Thordan", "") != 5)
                return "thordan ii";
            if (Signal("Dragonsong's Reprise (Ultimate)", 2, "Left Eye", "") != 3)
                return "eyes";
            down.Clear();
            if (Down("Dragonsong's Reprise (Ultimate)", 3, "Left Eye", down) != -1)
                return "one eye";
            if (Down("Dragonsong's Reprise (Ultimate)", 3, "Right Eye", down) != 4)
                return "both eyes";
            if (Signal("Dragonsong's Reprise (Ultimate)", 5, "Hraesvelgr", "") != 6)
                return "hraesvelgr";
            if (Signal("Dragonsong's Reprise (Ultimate)", 5, "Dragon-King Thordan", "") != 7)
                return "dragon king";

            if (Ladder("The Epic of Alexander (Ultimate)", 0) != "Living Liquid")
                return "tea p1";
            if (Ladder("The Epic of Alexander (Ultimate)", 3) != "Perfect Alexander")
                return "tea p4";
            if (Signal("The Epic of Alexander (Ultimate)", 0, "Brute Justice", "") != 1)
                return "brute";
            if (Signal("Alexander - The Burden of the Father (Savage)", 0, "Brute Justice", "") != -1)
                return "savage alexander";
            down.Clear();
            if (Down("The Epic of Alexander (Ultimate)", 1, "Brute Justice", down) != -1)
                return "one robot";
            if (Down("The Epic of Alexander (Ultimate)", 1, "Cruise Chaser", down) != 2)
                return "both robots";

            if (Ladder("the Weapon's Refrain (Ultimate)", 0) != "Garuda")
                return "uwu p1";
            if (Signal("the Weapon's Refrain (Ultimate)", 0, "Ifrit", "") != 1)
                return "ifrit";
            if (Signal("the Weapon's Refrain (Ultimate)", 4, "Ifrit", "") != -1)
                return "ifrit later";
            if (Signal("the Weapon's Refrain (Ultimate)", 3, "The Ultima Weapon", "") != 4)
                return "weapon";
            if (Signal("the Weapon's Refrain (Ultimate)", 4, "", "Ultimate Suppression") != 6)
                return "suppression";

            if (Ladder("the Unending Coil of Bahamut (Ultimate)", 4) != "Golden Bahamut")
                return "ucob p5";
            if (Signal("the Unending Coil of Bahamut (Ultimate)", 0, "Nael deus Darnus", "") != 1)
                return "nael";
            if (Signal("the Unending Coil of Bahamut (Ultimate)", 2, "", "Quickmarch Trio") != 3)
                return "adds";
            if (Signal("the Unending Coil of Bahamut (Ultimate)", 2, "Golden Bahamut", "") != 4)
                return "golden";
            if (!IsSplit("Futures Rewritten (Ultimate)", "Pandora"))
                return "split name";
            if (IsSplit("Futures Rewritten (Ultimate)", "Futures Rewritten"))
                return "whole name";
            if (!SingleLadder("the Unending Coil of Bahamut (Ultimate)"))
                return "ucob ladder";
            if (!SingleLadder("The Weapon's Refrain (Ultimate)"))
                return "uwu ladder";
            if (!SingleLadder("The Epic of Alexander (Ultimate)"))
                return "tea ladder";
            if (SingleLadder("Dancing Mad (Ultimate)"))
                return "dmu ladder";
            if (SingleLadder("Futures Rewritten (Ultimate)"))
                return "fru ladder";
            if (SingleLadder("The Omega Protocol (Ultimate)"))
                return "top ladder";
            if (SingleLadder("Dragonsong's Reprise (Ultimate)"))
                return "dsr ladder";

            return null;
        }

        private static Step[]? Steps(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return null;

            if (zone.Contains("Dancing Mad", StringComparison.OrdinalIgnoreCase))
                return DancingMad;
            if (zone.Contains("Futures Rewritten", StringComparison.OrdinalIgnoreCase))
                return FuturesRewritten;
            if (zone.Contains("Omega Protocol", StringComparison.OrdinalIgnoreCase))
                return OmegaProtocol;
            if (zone.Contains("Dragonsong", StringComparison.OrdinalIgnoreCase))
                return Dragonsong;
            if (zone.Contains("Epic of Alexander", StringComparison.OrdinalIgnoreCase))
                return EpicOfAlexander;
            if (zone.Contains("Weapon's Refrain", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Weapons Refrain", StringComparison.OrdinalIgnoreCase))
                return WeaponsRefrain;
            if (zone.Contains("Unending Coil", StringComparison.OrdinalIgnoreCase))
                return UnendingCoil;

            return null;
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
