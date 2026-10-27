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
            new("voice-woah", "Voice: Woah", "BardSongTracker.Resources.voice-woah.wav", "voice-woah.wav"),
            new("voice-wow", "Voice: Wow", "BardSongTracker.Resources.voice-wow.wav", "voice-wow.wav"),
            new("voice-okay", "Voice: Okay", "BardSongTracker.Resources.voice-okay.wav", "voice-okay.wav"),
            new("voice-ah", "Voice: Ah", "BardSongTracker.Resources.voice-ah.wav", "voice-ah.wav"),
            new("voice-nya", "Voice: Nya", "BardSongTracker.Resources.voice-nya.wav", "voice-nya.wav"),
            new("voice-giggle", "Voice: Giggle", "BardSongTracker.Resources.voice-giggle.wav", "voice-giggle.wav"),
            new("voice-laugh", "Voice: Laugh", "BardSongTracker.Resources.voice-laugh.wav", "voice-laugh.wav"),
            new("voice-oh", "Voice: Oh", "BardSongTracker.Resources.voice-oh.wav", "voice-oh.wav"),
            new("voice-sigh", "Voice: Sigh", "BardSongTracker.Resources.voice-sigh.wav", "voice-sigh.wav"),
            new("voice-sneeze", "Voice: Sneeze", "BardSongTracker.Resources.voice-sneeze.wav", "voice-sneeze.wav"),
            new("voice-loveyou", "Voice: Love You", "BardSongTracker.Resources.voice-loveyou.wav", "voice-loveyou.wav"),
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
