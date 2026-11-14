using System.Collections.Generic;

namespace BardSongTracker.Ninja
{
    /// <summary>
    /// Official mudra orders from the Ninja job guide. The jutsu is the last
    /// mudra: Ten finishes Katon and Huton, Chi finishes Raiton and Doton,
    /// Jin finishes Hyoton and Suiton. One mudra is Fuma Shuriken.
    /// </summary>
    internal static class MudraTable
    {
        public const uint Ten = 2259;
        public const uint Ninjutsu = 2260;
        public const uint Chi = 2261;
        public const uint Jin = 2263;
        public const uint Kassatsu = 2264;

        public const uint MudraStatus = 496;
        public const uint KassatsuStatus = 497;
        public const uint TenChiJinStatus = 1186;

        public static readonly Jutsu[] All =
        {
            new("fuma", "Fuma Shuriken", 2265, false, Ten),
            new("raiton", "Raiton", 2267, false, Ten, Chi),
            new("katon", "Katon", 2266, false, Chi, Ten),
            new("hyoton", "Hyoton", 2268, false, Ten, Jin),
            new("huton", "Huton", 2269, false, Jin, Chi, Ten),
            new("doton", "Doton", 2270, false, Ten, Jin, Chi),
            new("suiton", "Suiton", 2271, false, Ten, Chi, Jin),
            new("goka", "Goka Mekkyaku", 16491, true, Chi, Ten),
            new("hyosho", "Hyosho Ranryu", 16492, true, Ten, Jin),
        };

        public static string? SelfCheck()
        {
            if (All.Length != 9)
                return "count";

            foreach (var jutsu in All)
            {
                if (jutsu.Mudras.Length is < 1 or > 3)
                    return jutsu.Id + " length";
                if (!EndsCorrectly(jutsu))
                    return jutsu.Id + " order";
            }

            return null;
        }

        public static string Steps(Jutsu jutsu)
        {
            var names = new List<string>();
            if (jutsu.NeedsKassatsu)
                names.Add("Kassatsu");
            foreach (var mudra in jutsu.Mudras)
                names.Add(Name(mudra));
            names.Add("Ninjutsu");
            return string.Join("  →  ", names);
        }

        private static bool EndsCorrectly(Jutsu jutsu)
        {
            var last = jutsu.Mudras[^1];
            return jutsu.Id switch
            {
                "fuma" => jutsu.Mudras.Length == 1,
                "raiton" or "doton" => last == Chi,
                "katon" or "huton" or "goka" => last == Ten,
                "hyoton" or "suiton" or "hyosho" => last == Jin,
                _ => false,
            };
        }

        private static string Name(uint mudra)
        {
            if (mudra == Ten)
                return "Ten";
            if (mudra == Chi)
                return "Chi";
            if (mudra == Jin)
                return "Jin";
            return "Mudra";
        }

        internal readonly struct Jutsu
        {
            public Jutsu(string id, string name, uint actionId, bool needsKassatsu, params uint[] mudras)
            {
                Id = id;
                Name = name;
                ActionId = actionId;
                NeedsKassatsu = needsKassatsu;
                Mudras = mudras;
            }

            public string Id { get; }
            public string Name { get; }
            public uint ActionId { get; }
            public bool NeedsKassatsu { get; }
            public uint[] Mudras { get; }
        }
    }
}
