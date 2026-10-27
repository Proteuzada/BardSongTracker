using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace BardSongTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public float AlertThreshold { get; set; } = 5.0f;
        public float ArmysPaeonThreshold { get; set; } = 3.0f;
        public bool ShowOverlay { get; set; } = true;
        public bool PlaySound { get; set; } = true;
        public bool ShowSongTimer { get; set; } = true;
        
        public uint OverlayColor { get; set; } = 0xFF00A5FF;
        public float OverlayScale { get; set; } = 1.5f;
        public float SoundVolume { get; set; } = 0.3f;

        /// <summary>
        /// Id from <see cref="AlertSounds"/>. Unknown values fall back to Classic.
        /// </summary>
        public string AlertSoundId { get; set; } = AlertSounds.DefaultId;

        public bool TrackDots { get; set; } = true;
        public bool ShowDotTimer { get; set; } = true;
        public float DotAlertThreshold { get; set; } = 5.0f;

        /// <summary>
        /// Skill ids the player turned off. Missing ids stay enabled, so new skills
        /// show up without wiping an old config.
        /// </summary>
        public List<string> DisabledDotIds { get; set; } = new();

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }

        public bool IsDotEnabled(string skillId)
        {
            return DisabledDotIds == null || !DisabledDotIds.Contains(skillId);
        }

        public void SetDotEnabled(string skillId, bool enabled, bool save = true)
        {
            DisabledDotIds ??= new List<string>();

            if (enabled)
                DisabledDotIds.Remove(skillId);
            else if (!DisabledDotIds.Contains(skillId))
                DisabledDotIds.Add(skillId);

            if (save)
                Save();
        }

        public void SetJobDots(uint jobId, bool enabled)
        {
            foreach (var skill in DotCatalog.ForJob(jobId))
                SetDotEnabled(skill.Id, enabled, save: false);

            Save();
        }

        public int EnabledDotCount()
        {
            var enabled = 0;
            foreach (var skill in DotCatalog.Skills)
            {
                if (IsDotEnabled(skill.Id))
                    enabled++;
            }

            return enabled;
        }
    }
}
