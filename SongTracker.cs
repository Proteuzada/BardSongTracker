using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.JobGauge.Enums;
using System.Numerics;

namespace BardSongTracker
{
    /// <summary>
    /// Tracks the currently active Bard song and its remaining duration
    /// by reading the Bard Job Gauge each frame.
    /// </summary>
    public class SongTracker
    {
        public Song CurrentSong { get; private set; } = Song.None;
        public float RemainingTime { get; private set; } = 0f;

        /// <summary>
        /// Returns true if the current song exists and is within the given alert threshold.
        /// </summary>
        public bool IsExpiring(float threshold) =>
            CurrentSong != Song.None && RemainingTime > 0 && RemainingTime <= threshold;

        /// <summary>
        /// Full display name of the current song.
        /// </summary>
        public string SongName => CurrentSong switch
        {
            Song.MagesBallad => "Mage's Ballad",
            Song.ArmysPaeon => "Army's Paeon",
            Song.WanderersMinuet => "The Wanderer's Minuet",
            _ => "No song active"
        };

        /// <summary>
        /// Short name for the overlay display.
        /// </summary>
        public string ShortName => CurrentSong switch
        {
            Song.MagesBallad => "♪ Mage's Ballad",
            Song.ArmysPaeon => "♪ Army's Paeon",
            Song.WanderersMinuet => "♪ Wanderer's Minuet",
            _ => ""
        };

        /// <summary>
        /// Color for each song as packed ABGR uint (for ImGui draw lists).
        /// Mage's Ballad = Magenta, Army's Paeon = Green, Wanderer's Minuet = Cyan
        /// </summary>
        public uint SongColor => CurrentSong switch
        {
            Song.MagesBallad => 0xFFFF00FF,   // Magenta (ABGR)
            Song.ArmysPaeon => 0xFF00FF00,     // Green (ABGR)
            Song.WanderersMinuet => 0xFFFFE000, // Cyan (ABGR)
            _ => 0xFFFFFFFF
        };

        /// <summary>
        /// Color for each song as Vector4 (RGBA float, for ImGui style operations).
        /// </summary>
        public Vector4 SongColorVec4 => CurrentSong switch
        {
            Song.MagesBallad => new Vector4(0.85f, 0.35f, 0.90f, 1.0f),    // Purple/Magenta
            Song.ArmysPaeon => new Vector4(0.30f, 0.90f, 0.40f, 1.0f),      // Green
            Song.WanderersMinuet => new Vector4(0.40f, 0.80f, 1.00f, 1.0f), // Cyan/Light Blue
            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
        };

        /// <summary>
        /// Read the Bard's job gauge to get the active song and duration.
        /// </summary>
        public void Update(BRDGauge? gauge)
        {
            if (gauge == null)
            {
                CurrentSong = Song.None;
                RemainingTime = 0f;
                return;
            }

            CurrentSong = gauge.Song;
            
            // SongTimer is usually in milliseconds, but let's make sure.
            // In FFXIV job gauges, it's often milliseconds (e.g. 45000 for 45s).
            RemainingTime = gauge.SongTimer / 1000f;
            
            // Just in case it's 0 (meaning song is actually inactive but gauge is still showing it)
            if (RemainingTime <= 0f) 
            {
                CurrentSong = Song.None;
            }
        }
    }
}
