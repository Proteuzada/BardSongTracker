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
            // The duty name and the current target. A zone such as "Ultimates (Legacy)"
            // does not contain the duty name; the encounter does.
            var byBoss = 0;
            var bestScore = 0;
            var bestLength = int.MaxValue;
            foreach (var zone in zones)
            {
                var zoneScore = ZoneScore(zone.Name, zoneName);
                if (zoneScore < 3)
                    continue;

                foreach (var encounter in zone.Encounters)
                {
                    var bossScore = Score(encounter.Name, bossName);
                    if (bossScore == 0)
                        continue;

                    Consider(zoneScore + bossScore, encounter, ref bestScore, ref bestLength, ref byBoss);
                }
            }

            if (byBoss != 0)
                return byBoss;

            var byDuty = 0;
            var dutyFit = 0;
            var dutyLength = int.MaxValue;
            foreach (var zone in zones)
            {
                var zoneScore = Score(zone.Name, zoneName);
                foreach (var encounter in zone.Encounters)
                {
                    var fit = DutyFit(encounter.Name, zoneName);
                    if (fit == 0)
                        continue;
                    if (fit < dutyFit)
                        continue;
                    if (fit == dutyFit && zoneScore < bestScore)
                        continue;
                    if (fit == dutyFit && zoneScore == bestScore && encounter.Name.Length > dutyLength)
                        continue;
                    if (fit == dutyFit && zoneScore == bestScore && encounter.Name.Length == dutyLength && encounter.Id <= byDuty)
                        continue;

                    dutyFit = fit;
                    bestScore = zoneScore;
                    dutyLength = encounter.Name.Length;
                    byDuty = encounter.Id;
                }
            }

            if (byDuty != 0)
                return byDuty;

            var byFloor = FloorPick(zoneName, zones);
            if (byFloor != 0)
                return byFloor;

            return UniqueBoss(bossName, zones);
        }

        public static int Score(string candidate, string wanted)
        {
            var leftName = Normalize(candidate);
            var rightName = Normalize(wanted);
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

            if (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal)
                || left.EndsWith(right, StringComparison.Ordinal) || right.EndsWith(left, StringComparison.Ordinal))
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
            if (Score("Endsinger", "The Endsinger") != 4)
                return "article";

            var legacy = Zone("Ultimates (Legacy)",
                (1060, "The Unending Coil of Bahamut"),
                (1061, "The Weapon's Refrain"),
                (1062, "The Epic of Alexander"));
            var ultimates = Zone("Ultimates",
                (1065, "Dragonsong's Reprise"),
                (1068, "The Omega Protocol"),
                (1072, "Futures Rewritten"));
            var alexander = Zone("Alexander", (3, "Living Liquid"), (11, "Cruise Chaser"));
            var alphascape = Zone("Alphascape", (12, "Omega"));
            var promise = Zone("Eden's Promise", (90, "Cloud of Darkness"), (111, "Fatebreaker"), (120, "Oracle of Darkness"));
            var trials = Zone("Trials (Extreme)",
                (1063, "Endsinger"),
                (1064, "Hydaelyn"),
                (1066, "Barbariccia"),
                (1067, "Zodiark"));
            var abyssos = Zone("Abyssos", (83, "Proto-Carbuncle"), (87, "Hephaistos II"));
            var heavy = Zone("AAC Heavyweight", (201, "M1"), (202, "M2"), (203, "M3"), (204, "M4"));
            var catalog = new List<ZoneNames>
            {
                legacy, ultimates, dancing, sigma, alexander, alphascape, promise, trials, abyssos, heavy, dungeon[0],
            };

            if (Pick("the Epic of Alexander (Ultimate)", "Living Liquid", catalog) != 1062)
                return "tea liquid";
            if (Pick("The Epic of Alexander (Ultimate)", "Cruise Chaser", catalog) != 1062)
                return "tea cruise";
            if (Pick("The Epic of Alexander (Ultimate)", "Perfect Alexander", catalog) != 1062)
                return "tea perfect";
            if (Pick("Alexander - The Arm of the Father (Savage)", "Living Liquid", catalog) != 3)
                return "a3";
            if (Pick("the Weapon's Refrain (Ultimate)", "Titan", catalog) != 1061)
                return "uwu";
            if (Pick("the Unending Coil of Bahamut (Ultimate)", "Twintania", catalog) != 1060)
                return "ucob";
            if (Pick("Dragonsong's Reprise (Ultimate)", "Nidhogg", catalog) != 1065)
                return "dsr";
            if (Pick("the Omega Protocol (Ultimate)", "Omega", catalog) != 1068)
                return "top";
            if (Pick("Alphascape V4.0 (Savage)", "Omega", catalog) != 12)
                return "o12";
            if (Pick("Futures Rewritten (Ultimate)", "Fatebreaker", catalog) != 1072)
                return "fru";
            if (Pick("Eden's Promise: Litany (Savage)", "Fatebreaker", catalog) != 111)
                return "e11";
            if (Pick("Eden's Promise: Eternity (Savage)", "Oracle of Darkness", catalog) != 120)
                return "e12";
            if (Pick("the Minstrel's Ballad: Endsinger's Aria", "The Endsinger", catalog) != 1063)
                return "endsinger";
            if (Pick("the Minstrel's Ballad: Hydaelyn's Call", "Hydaelyn", catalog) != 1064)
                return "hydaelyn";
            if (Pick("the Minstrel's Ballad: Zodiark's Fall", "Zodiark", catalog) != 1067)
                return "zodiark";
            if (Pick("Storm's Crown (Extreme)", "Barbariccia", catalog) != 1066)
                return "barbariccia";
            if (Pick("Abyssos: The Eighth Circle (Savage)", "Hephaistos II", catalog) != 87)
                return "p8s";
            if (Pick("AAC Heavyweight M1 (Savage)", "Black Cat", catalog) != 201)
                return "m1";
            if (Pick("AAC Heavyweight M4 (Savage)", "M4", catalog) != 204)
                return "m4";
            if (Pick("Dancing Mad (Ultimate)", "Kefka", catalog) != 1)
                return "dancing catalog";
            if (Pick("The Dead Ends", "Aetheric Void", catalog) != 0)
                return "catalog trash";

            var currentLegacy = Zone("Ultimates (Legacy)",
                (1073, "The Unending Coil of Bahamut"),
                (1074, "The Weapon's Refrain"),
                (1075, "The Epic of Alexander"),
                (1076, "Dragonsong's Reprise"),
                (1077, "The Omega Protocol"));
            var dancingNow = Zone("Dancing Mad", (1085, "Dancing Mad"));
            var moved = new List<ZoneNames> { legacy, currentLegacy, dancing, dancingNow, alexander };
            if (Pick("The Epic of Alexander (Ultimate)", "Living Liquid", moved) != 1075)
                return "tea current";
            if (Pick("Dancing Mad (Ultimate)", "Kefka", new List<ZoneNames> { dancingNow }) != 1085)
                return "dancing whole";
            if (!TryFind("Dancing Mad (Ultimate)", out var dancingId, out var dancingDifficulty) || dancingId != 1085 || dancingDifficulty != 100)
                return "known dancing";
            if (!TryFind("The Epic of Alexander (Ultimate)", out var teaId, out _) || teaId != 1075)
                return "known tea";
            if (!TryFind("the Weapon's Refrain (Ultimate)", out var uwuId, out _) || uwuId != 1074)
                return "known uwu";
            if (!TryFind("the Unending Coil of Bahamut (Ultimate)", out var ucobId, out _) || ucobId != 1073)
                return "known ucob";
            if (!TryFind("Dragonsong's Reprise (Ultimate)", out var dsrId, out _) || dsrId != 1076)
                return "known dsr";
            if (!TryFind("the Omega Protocol (Ultimate)", out var topId, out _) || topId != 1077)
                return "known top";
            if (!TryFind("Futures Rewritten (Ultimate)", out var fruId, out _) || fruId != 1079)
                return "known fru";
            if (TryFind("The Dead Ends", out _, out _))
                return "known trash";

            return null;
        }

        /// <summary>
        /// Current FF Logs encounter for a duty. Ultimates were copied onto new
        /// ids; the old ids no longer have the live ranking curve.
        /// </summary>
        public static bool TryFind(string duty, out int encounterId, out int difficulty)
        {
            encounterId = 0;
            difficulty = 0;
            if (string.IsNullOrWhiteSpace(duty))
                return false;

            var dutyWords = Words(duty);
            var best = 0;
            foreach (var fight in KnownFights)
            {
                var words = Words(fight.Name);
                if (words.Count < 2 || words.Count < best)
                    continue;

                var found = true;
                foreach (var word in words)
                {
                    if (!dutyWords.Contains(word))
                    {
                        found = false;
                        break;
                    }
                }

                if (!found)
                    continue;

                best = words.Count;
                encounterId = fight.Id;
                difficulty = fight.Difficulty;
            }

            return encounterId != 0;
        }

        private readonly record struct KnownFight(string Name, int Id, int Difficulty);

        private static readonly KnownFight[] KnownFights =
        {
            new("Dancing Mad", 1085, 100),
            new("Futures Rewritten", 1079, 100),
            new("The Omega Protocol", 1077, 100),
            new("Dragonsong's Reprise", 1076, 100),
            new("The Epic of Alexander", 1075, 100),
            new("The Weapon's Refrain", 1074, 100),
            new("The Unending Coil of Bahamut", 1073, 100),
        };

        private static int ZoneScore(string zone, string duty)
        {
            var score = Score(zone, duty);
            if (score >= 4)
                return score;
            if (score == 3 && StartsWithName(zone, duty))
                return score;

            return 0;
        }

        private static bool StartsWithName(string zone, string duty)
        {
            var left = Letters(Normalize(zone));
            var right = Letters(Normalize(duty));
            return left.Length >= 4 && right.StartsWith(left, StringComparison.Ordinal);
        }

        private static int DutyFit(string encounter, string duty)
        {
            var left = Words(encounter);
            var right = Words(duty);
            if (left.Count == 0 || right.Count == 0)
                return 0;

            foreach (var word in left)
            {
                if (!right.Contains(word))
                    return 0;
            }

            var longest = 0;
            foreach (var word in right)
            {
                if (word.Length > longest)
                    longest = word.Length;
            }

            if (longest < 6)
                return 0;

            foreach (var word in right)
            {
                if (word.Length == longest && left.Contains(word))
                    return 10 + left.Count;
            }

            return 0;
        }

        private static int FloorPick(string duty, IReadOnlyList<ZoneNames> zones)
        {
            var token = FloorToken(duty);
            if (token.Length < 2)
                return 0;

            var bestZone = 0;
            var bestId = 0;
            var ties = 0;
            foreach (var zone in zones)
            {
                var zoneScore = ZoneScore(zone.Name, duty);
                if (zoneScore < 3)
                    continue;

                var hit = 0;
                var hits = 0;
                foreach (var encounter in zone.Encounters)
                {
                    if (!IsFloor(encounter.Name, token))
                        continue;

                    hits++;
                    hit = encounter.Id;
                }

                if (hits != 1)
                    continue;
                if (zoneScore < bestZone)
                    continue;
                if (zoneScore == bestZone)
                {
                    ties++;
                    continue;
                }

                bestZone = zoneScore;
                bestId = hit;
                ties = 1;
            }

            return ties == 1 ? bestId : 0;
        }

        private static int UniqueBoss(string bossName, IReadOnlyList<ZoneNames> zones)
        {
            var bestId = 0;
            var count = 0;
            foreach (var zone in zones)
            {
                foreach (var encounter in zone.Encounters)
                {
                    if (Score(encounter.Name, bossName) < 4)
                        continue;

                    count++;
                    bestId = encounter.Id;
                }
            }

            return count == 1 ? bestId : 0;
        }

        private static bool IsFloor(string encounter, string token)
        {
            var name = Letters(Normalize(encounter));
            if (name.Length == 0)
                return false;
            if (name == token || name == token + "s")
                return true;

            return token[0] == 'v' && name.TrimEnd('0') == token.TrimEnd('0');
        }

        private static string FloorToken(string duty)
        {
            var name = StripDuty(duty).Trim();
            var turnAt = name.LastIndexOf("Turn ", StringComparison.OrdinalIgnoreCase);
            if (turnAt >= 0)
            {
                var rest = name[(turnAt + 5)..].Trim();
                var digits = rest.Length > 0;
                foreach (var character in rest)
                {
                    if (!char.IsDigit(character))
                        digits = false;
                }

                if (digits)
                    return "turn" + rest;
            }

            var space = name.LastIndexOf(' ');
            var last = space >= 0 ? name[(space + 1)..] : name;
            var token = Letters(last);
            if (token.Length >= 2 && (token[0] == 'm' || token[0] == 'v' || token[0] == 'p') && char.IsDigit(token[1]))
                return token;

            return "";
        }

        private static List<string> Words(string name)
        {
            var words = new List<string>();
            var normalized = Normalize(name);
            var index = 0;
            while (index < normalized.Length)
            {
                while (index < normalized.Length && !char.IsLetterOrDigit(normalized[index]))
                    index++;
                var start = index;
                while (index < normalized.Length && char.IsLetterOrDigit(normalized[index]))
                    index++;
                if (index == start)
                    break;

                var word = normalized[start..index].ToLowerInvariant();
                if (word.Length >= 3 && !IsStopWord(word))
                    words.Add(word);
            }

            return words;
        }

        private static bool IsStopWord(string word)
        {
            return word is "the" or "and" or "ballad" or "minstrel";
        }

        private static string Normalize(string name)
        {
            var trimmed = StripPhase(StripDuty(name)).Trim();
            if (trimmed.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[4..].Trim();
            return trimmed;
        }

        private static void Consider(int total, EncounterName encounter, ref int bestScore, ref int bestLength, ref int bestId)
        {
            var length = encounter.Name.Length;
            if (total < bestScore)
                return;
            if (total == bestScore && length > bestLength)
                return;
            if (total == bestScore && length == bestLength && encounter.Id <= bestId)
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
