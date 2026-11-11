using System;
using System.Collections.Generic;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Picks the FF Logs encounter for the duty and target in the network log.
    /// Phase titles such as "P1: Kefka" have to match the in-game name "Kefka",
    /// and a duty ranked as one encounter has to match when the target is only
    /// a phase boss.
    /// </summary>
    internal static class EncounterMatch
    {
        internal sealed class ZoneNames
        {
            public string Name = "";
            public List<EncounterName> Encounters = new();
        }

        internal struct EncounterName
        {
            public int Id;
            public string Name;

            public EncounterName(int id, string name)
            {
                Id = id;
                Name = name ?? "";
            }
        }

        public static int Pick(string zoneName, string bossName, IReadOnlyList<ZoneNames> zones)
        {
            var bestId = 0;
            var bestScore = 0;
            var bestLength = int.MaxValue;
            foreach (var zone in zones)
            {
                var zoneScore = Score(zone.Name, zoneName);
                if (zoneScore == 0)
                    continue;

                foreach (var encounter in zone.Encounters)
                {
                    var bossScore = Score(encounter.Name, bossName);
                    if (bossScore == 0)
                        continue;

                    Consider(zoneScore + bossScore, encounter, ref bestScore, ref bestLength, ref bestId);
                }
            }

            if (bestId != 0)
                return bestId;

            foreach (var zone in zones)
            {
                var zoneScore = Score(zone.Name, zoneName);
                if (zoneScore == 0)
                    continue;

                foreach (var encounter in zone.Encounters)
                {
                    var named = Score(encounter.Name, zone.Name);
                    var duty = Score(encounter.Name, zoneName);
                    var nameScore = named > duty ? named : duty;
                    if (nameScore == 0)
                        continue;

                    Consider(zoneScore + nameScore, encounter, ref bestScore, ref bestLength, ref bestId);
                }
            }

            return bestId;
        }

        public static int Score(string candidate, string wanted)
        {
            var leftName = StripPhase(StripDuty(candidate));
            var rightName = StripPhase(StripDuty(wanted));
            if (leftName.Length == 0 || rightName.Length == 0)
                return 0;
            if (string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase))
                return 4;

            var left = Letters(leftName);
            var right = Letters(rightName);
            if (left.Length == 0 || right.Length == 0)
                return 0;
            if (left == right)
                return 4;

            if (left.Length < 4 || right.Length < 4)
                return 0;
            if (!left.Contains(right, StringComparison.Ordinal) && !right.Contains(left, StringComparison.Ordinal))
                return 0;

            if (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal))
                return 3;

            return 2;
        }

        public static string? SelfCheck()
        {
            var dancing = Zone("Dancing Mad",
                (1, "P1: Kefka"),
                (2, "P2: Forsaken Kefka"),
                (3, "P3: Exdeath and Chaos"),
                (4, "P4: Kefka Says"),
                (5, "P5: Ultima Kefka"));
            var sigma = Zone("Sigmascape", (8, "Kefka"));
            var both = new List<ZoneNames> { sigma, dancing };

            if (Pick("Dancing Mad (Ultimate)", "Kefka", both) != 1)
                return "kefka phase";
            if (Pick("Dancing Mad (Ultimate)", "Exdeath", both) != 3)
                return "exdeath";
            if (Pick("Dancing Mad (Ultimate)", "Chaos", both) != 3)
                return "chaos";
            if (Pick("Sigmascape V4.0 (Savage)", "Kefka", both) != 8)
                return "sigmascape";

            var whole = new List<ZoneNames> { Zone("Dancing Mad", (76, "Dancing Mad")) };
            if (Pick("Dancing Mad (Ultimate)", "Kefka", whole) != 76)
                return "whole fight";

            var dungeon = new List<ZoneNames>
            {
                Zone("The Dead Ends", (11, "Ra-la"), (12, "Caustic Grebuloff"), (13, "Scarmiglione")),
            };
            if (Pick("The Dead Ends", "Scarmiglione", dungeon) != 13)
                return "dungeon boss";
            if (Pick("The Dead Ends", "Aetheric Void", dungeon) != 0)
                return "dungeon trash";

            var bare = new List<ZoneNames>
            {
                Zone("Dancing Mad",
                    (1, "Kefka"),
                    (2, "Forsaken Kefka"),
                    (4, "Kefka Says"),
                    (5, "Ultima Kefka")),
            };
            if (Pick("Dancing Mad (Ultimate)", "Kefka", bare) != 1)
                return "bare kefka";

            if (Score("The Omega Protocol", "The Omega Protocol (Ultimate)") != 4)
                return "ultimate suffix";
            if (Score("Pantokrator", "Pantokrator") != 4)
                return "pantokrator";

            return null;
        }

        private static void Consider(int total, EncounterName encounter, ref int bestScore, ref int bestLength, ref int bestId)
        {
            var length = encounter.Name.Length;
            if (total < bestScore || (total == bestScore && length >= bestLength))
                return;

            bestScore = total;
            bestLength = length;
            bestId = encounter.Id;
        }

        private static ZoneNames Zone(string name, params (int Id, string Name)[] encounters)
        {
            var zone = new ZoneNames { Name = name };
            foreach (var encounter in encounters)
                zone.Encounters.Add(new EncounterName(encounter.Id, encounter.Name));
            return zone;
        }

        private static string StripDuty(string name)
        {
            var trimmed = name.Trim();
            var paren = trimmed.IndexOf('(');
            if (paren > 0)
                trimmed = trimmed[..paren].Trim();
            return trimmed;
        }

        private static string StripPhase(string name)
        {
            var trimmed = name.Trim();
            var index = PhaseIndex(trimmed);
            if (index <= 0 || index >= trimmed.Length)
                return trimmed;

            return trimmed[index..].Trim();
        }

        private static int PhaseIndex(string name)
        {
            var start = 0;
            if (name.StartsWith("Phase ", StringComparison.OrdinalIgnoreCase))
                start = 6;
            else if (name.Length >= 2 && (name[0] == 'P' || name[0] == 'p') && char.IsDigit(name[1]))
                start = 1;
            else
                return 0;

            var index = start;
            while (index < name.Length && char.IsDigit(name[index]))
                index++;
            if (index == start || index >= name.Length)
                return 0;
            if (name[index] != ':' && name[index] != ' ' && name[index] != '.' && name[index] != '-')
                return 0;

            while (index < name.Length && (name[index] == ':' || name[index] == ' ' || name[index] == '.' || name[index] == '-'))
                index++;

            return index;
        }

        private static string Letters(string text)
        {
            var buffer = new char[text.Length];
            var count = 0;
            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                    buffer[count++] = char.ToLowerInvariant(character);
            }

            return new string(buffer, 0, count);
        }
    }
}
