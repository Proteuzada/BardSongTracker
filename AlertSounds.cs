namespace BardSongTracker
{
    public static class AlertSounds
    {
        public const string DefaultId = "classic";

        public readonly record struct AlertSound(string Id, string Name, string ResourceName, string FileName);

        public static readonly AlertSound[] All =
        {
            new("classic", "Classic", "BardSongTracker.Resources.alert.mp3", "alert.mp3"),
            new("chime", "Chime", "BardSongTracker.Resources.chime.wav", "chime.wav"),
            new("sparkle", "Sparkle", "BardSongTracker.Resources.sparkle.wav", "sparkle.wav"),
            new("pop", "Pop", "BardSongTracker.Resources.pop.wav", "pop.wav"),
            new("bell", "Bell", "BardSongTracker.Resources.bell.wav", "bell.wav"),
            new("twinkle", "Twinkle", "BardSongTracker.Resources.twinkle.wav", "twinkle.wav"),
            new("slide", "Slide", "BardSongTracker.Resources.slide.wav", "slide.wav"),
        };

        public static string[] Names { get; } = BuildNames();

        public static int IndexOf(string? id)
        {
            var normalized = Normalize(id);
            for (var i = 0; i < All.Length; i++)
            {
                if (All[i].Id == normalized)
                    return i;
            }

            return 0;
        }

        public static string Normalize(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return DefaultId;

            foreach (var sound in All)
            {
                if (sound.Id == id)
                    return sound.Id;
            }

            return DefaultId;
        }

        public static AlertSound Find(string? id) => All[IndexOf(id)];

        private static string[] BuildNames()
        {
            var names = new string[All.Length];
            for (var i = 0; i < All.Length; i++)
                names[i] = All[i].Name;
            return names;
        }
    }
}
