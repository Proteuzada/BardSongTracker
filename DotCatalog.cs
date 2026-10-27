using System;
using System.Collections.Generic;
using System.Numerics;

namespace BardSongTracker
{
    public enum CombatRole
    {
        Tank,
        Healer,
        Melee,
        PhysicalRanged,
        Caster,
        Limited,
    }

    public enum DotAnchor
    {
        /// <summary>Debuff on the current target, applied by the local player.</summary>
        Target,

        /// <summary>Ground effect whose remaining time is a buff on the player.</summary>
        Self,
    }

    public sealed class JobInfo
    {
        public JobInfo(uint id, string abbreviation, string name, CombatRole role)
        {
            Id = id;
            Abbreviation = abbreviation;
            Name = name;
            Role = role;
        }

        public uint Id { get; }
        public string Abbreviation { get; }
        public string Name { get; }
        public CombatRole Role { get; }
    }

    public readonly record struct TrackedStatus(uint Id, string Label);

    public sealed class DotSkill
    {
        public DotSkill(string id, uint jobId, string name, string detail, DotAnchor anchor, params TrackedStatus[] statuses)
        {
            Id = id;
            JobId = jobId;
            Name = name;
            Detail = detail;
            Anchor = anchor;
            Statuses = statuses;
        }

        public string Id { get; }
        public uint JobId { get; }
        public string Name { get; }
        public string Detail { get; }
        public DotAnchor Anchor { get; }
        public TrackedStatus[] Statuses { get; }

        public bool Contains(uint statusId)
        {
            foreach (var status in Statuses)
            {
                if (status.Id == statusId)
                    return true;
            }

            return false;
        }

        public string LabelFor(uint statusId)
        {
            foreach (var status in Statuses)
            {
                if (status.Id == statusId)
                    return status.Label;
            }

            return Name;
        }
    }

    /// <summary>
    /// Player DoTs keyed by the status id the game actually applies.
    /// Ids come from the FFXIV Status sheet (including low-level upgrades and the
    /// PvP row when it is a different id for the same skill). Ground puddles that
    /// do not put a debuff on the enemy are tracked from the buff on the player.
    /// </summary>
    public static class DotCatalog
    {
        public const uint SelfSourceId = 0xE0000000;

        private static readonly Dictionary<uint, DotSkill[]> SkillsByJob;
        private static readonly DotSkill[] AllSkills;

        static DotCatalog()
        {
            AllSkills = BuildSkills();
            var byJob = new Dictionary<uint, List<DotSkill>>();
            var seen = new HashSet<uint>();

            foreach (var skill in AllSkills)
            {
                var knownJob = false;
                foreach (var job in Jobs)
                {
                    if (job.Id == skill.JobId)
                    {
                        knownJob = true;
                        break;
                    }
                }

                if (!knownJob)
                    throw new InvalidOperationException($"DoT skill {skill.Id} points at unknown job {skill.JobId}.");

                if (!byJob.TryGetValue(skill.JobId, out var list))
                {
                    list = new List<DotSkill>();
                    byJob[skill.JobId] = list;
                }

                list.Add(skill);
                foreach (var status in skill.Statuses)
                {
                    if (!seen.Add(status.Id))
                        throw new InvalidOperationException($"Status {status.Id} is listed on more than one DoT skill.");
                }
            }

            SkillsByJob = new Dictionary<uint, DotSkill[]>();
            foreach (var pair in byJob)
                SkillsByJob[pair.Key] = pair.Value.ToArray();
        }

        public static IReadOnlyList<JobInfo> Jobs { get; } = new JobInfo[]
        {
            new(19, "PLD", "Paladin", CombatRole.Tank),
            new(21, "WAR", "Warrior", CombatRole.Tank),
            new(32, "DRK", "Dark Knight", CombatRole.Tank),
            new(37, "GNB", "Gunbreaker", CombatRole.Tank),

            new(24, "WHM", "White Mage", CombatRole.Healer),
            new(28, "SCH", "Scholar", CombatRole.Healer),
            new(33, "AST", "Astrologian", CombatRole.Healer),
            new(40, "SGE", "Sage", CombatRole.Healer),

            new(20, "MNK", "Monk", CombatRole.Melee),
            new(22, "DRG", "Dragoon", CombatRole.Melee),
            new(30, "NIN", "Ninja", CombatRole.Melee),
            new(34, "SAM", "Samurai", CombatRole.Melee),
            new(39, "RPR", "Reaper", CombatRole.Melee),
            new(41, "VPR", "Viper", CombatRole.Melee),

            new(23, "BRD", "Bard", CombatRole.PhysicalRanged),
            new(31, "MCH", "Machinist", CombatRole.PhysicalRanged),
            new(38, "DNC", "Dancer", CombatRole.PhysicalRanged),

            new(25, "BLM", "Black Mage", CombatRole.Caster),
            new(27, "SMN", "Summoner", CombatRole.Caster),
            new(35, "RDM", "Red Mage", CombatRole.Caster),
            new(42, "PCT", "Pictomancer", CombatRole.Caster),

            new(36, "BLU", "Blue Mage", CombatRole.Limited),
            new(43, "BST", "Beastmaster", CombatRole.Limited),
        };

        public static IReadOnlyList<DotSkill> Skills => AllSkills;

        public static IReadOnlyList<DotSkill> ForJob(uint jobId)
        {
            return SkillsByJob.TryGetValue(jobId, out var skills) ? skills : Array.Empty<DotSkill>();
        }

        public static int CountForJob(uint jobId) => ForJob(jobId).Count;

        public static CombatRole RoleOf(uint jobId)
        {
            foreach (var job in Jobs)
            {
                if (job.Id == jobId)
                    return job.Role;
            }

            return CombatRole.Limited;
        }

        public static string RoleLabel(CombatRole role) => role switch
        {
            CombatRole.Tank => "Tanks",
            CombatRole.Healer => "Healers",
            CombatRole.Melee => "Melee",
            CombatRole.PhysicalRanged => "Ranged",
            CombatRole.Caster => "Casters",
            CombatRole.Limited => "Limited",
            _ => role.ToString(),
        };

        public static Vector4 RoleColor(CombatRole role) => role switch
        {
            CombatRole.Tank => new Vector4(0.45f, 0.68f, 1.00f, 1f),
            CombatRole.Healer => new Vector4(0.35f, 0.90f, 0.55f, 1f),
            CombatRole.Melee => new Vector4(1.00f, 0.48f, 0.38f, 1f),
            CombatRole.PhysicalRanged => new Vector4(1.00f, 0.82f, 0.35f, 1f),
            CombatRole.Caster => new Vector4(0.78f, 0.52f, 1.00f, 1f),
            _ => new Vector4(0.78f, 0.78f, 0.78f, 1f),
        };

        private static DotSkill[] BuildSkills()
        {
            return new[]
            {
                // Tanks
                S("pld-circle-of-scorn", 19, "Circle of Scorn", "DoT em área no alvo.", DotAnchor.Target,
                    T(248, "Circle of Scorn")),
                S("drk-salted-earth", 32, "Salted Earth", "Poça no chão. O tempo fica em você.", DotAnchor.Self,
                    T(749, "Salted Earth"), T(3036, "Salted Earth")),
                S("gnb-sonic-break", 37, "Sonic Break", "DoT no alvo.", DotAnchor.Target,
                    T(1837, "Sonic Break")),
                S("gnb-bow-shock", 37, "Bow Shock", "DoT em área no alvo.", DotAnchor.Target,
                    T(1838, "Bow Shock")),

                // Healers
                S("whm-dia", 24, "Dia", "Inclui Aero e Aero II.", DotAnchor.Target,
                    T(143, "Aero"), T(144, "Aero II"), T(1871, "Dia")),
                S("sch-biolysis", 28, "Biolysis", "Inclui Bio e Bio II.", DotAnchor.Target,
                    T(179, "Bio"), T(189, "Bio II"), T(1895, "Biolysis"), T(3089, "Biolysis")),
                S("sch-baneful-impaction", 28, "Baneful Impaction", "DoT do Scholar.", DotAnchor.Target,
                    T(3883, "Baneful Impaction")),
                S("ast-combust", 33, "Combust III", "Inclui Combust e Combust II.", DotAnchor.Target,
                    T(838, "Combust"), T(843, "Combust II"), T(1881, "Combust III")),
                S("sge-dosis", 40, "Eukrasian Dosis", "Linha single-target, do Dosis I ao III.", DotAnchor.Target,
                    T(2614, "Eukrasian Dosis"), T(2615, "Eukrasian Dosis II"), T(2616, "Eukrasian Dosis III"),
                    T(3108, "Eukrasian Dosis III"), T(3976, "Eukrasian Dosis III")),
                S("sge-dyskrasia", 40, "Eukrasian Dyskrasia", "DoT em área.", DotAnchor.Target,
                    T(3897, "Eukrasian Dyskrasia")),

                // Melee
                S("mnk-demolish", 20, "Demolish", "DoT no alvo.", DotAnchor.Target,
                    T(246, "Demolish"), T(1309, "Demolish")),
                S("drg-chaotic-spring", 22, "Chaotic Spring", "Inclui Chaos Thrust.", DotAnchor.Target,
                    T(118, "Chaos Thrust"), T(2719, "Chaotic Spring"), T(1312, "Chaos Thrust")),
                S("nin-doton", 30, "Doton", "Poça no chão. O tempo fica em você.", DotAnchor.Self,
                    T(501, "Doton"), T(3187, "Doton")),
                S("sam-higanbana", 34, "Higanbana", "DoT no alvo.", DotAnchor.Target,
                    T(1228, "Higanbana"), T(1319, "Higanbana")),

                // Physical ranged
                S("brd-caustic-bite", 23, "Caustic Bite", "Inclui Venomous Bite.", DotAnchor.Target,
                    T(124, "Venomous Bite"), T(1200, "Caustic Bite"), T(1321, "Caustic Bite")),
                S("brd-stormbite", 23, "Stormbite", "Inclui Windbite.", DotAnchor.Target,
                    T(129, "Windbite"), T(1201, "Stormbite"), T(1322, "Stormbite")),
                S("mch-bioblaster", 31, "Bioblaster", "DoT em área no alvo.", DotAnchor.Target,
                    T(1866, "Bioblaster"), T(2019, "Bioblaster")),

                // Casters
                S("blm-high-thunder", 25, "High Thunder", "Linha single-target: Thunder, Thunder III, High Thunder.", DotAnchor.Target,
                    T(161, "Thunder"), T(163, "Thunder III"), T(3871, "High Thunder"), T(1324, "Thunder")),
                S("blm-high-thunder-ii", 25, "High Thunder II", "Linha em área: Thunder II, Thunder IV, High Thunder II.", DotAnchor.Target,
                    T(162, "Thunder II"), T(1210, "Thunder IV"), T(3872, "High Thunder II"), T(2075, "Thunder II")),
                S("smn-slipstream", 27, "Slipstream", "Poça no chão. O tempo fica em você.", DotAnchor.Self,
                    T(2706, "Slipstream"), T(3225, "Slipstream")),
                S("pct-fanged-muse", 42, "Fanged Muse (PvP)", "DoT do Pictomancer no Crystalline Conflict.", DotAnchor.Target,
                    T(4112, "Fanged Muse")),

                // Limited
                S("blu-song-of-torment", 36, "Song of Torment", "O mesmo efeito de Nightbloom. Os dois dividem este status.", DotAnchor.Target,
                    T(1714, "Song of Torment")),
                S("blu-breath-of-magic", 36, "Breath of Magic", "DoT do Blue Mage.", DotAnchor.Target,
                    T(3712, "Breath of Magic")),
                S("blu-feather-rain", 36, "Feather Rain", "DoT de vento no alvo.", DotAnchor.Target,
                    T(1723, "Feather Rain")),
                S("bst-seeds-sown", 43, "Seeds Sown", "DoT do Seedsower.", DotAnchor.Target,
                    T(4622, "Seeds Sown")),
            };
        }

        private static DotSkill S(string id, uint jobId, string name, string detail, DotAnchor anchor, params TrackedStatus[] statuses)
            => new DotSkill(id, jobId, name, detail, anchor, statuses);

        private static TrackedStatus T(uint id, string label) => new TrackedStatus(id, label);
    }
}
