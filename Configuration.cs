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

        public bool ShowParseOverlay { get; set; } = true;
        public bool ShowOnlyMyMeter { get; set; } = false;
        public float MeterScale { get; set; } = 1f;
        public bool MeterMinimal { get; set; } = false;
        public bool ShowDamagePercent { get; set; } = true;
        public bool ShowDeaths { get; set; } = true;
        public bool ShowEncounterLine { get; set; } = true;
        public bool ShowHitDetails { get; set; } = true;
        public bool KeepLastPull { get; set; } = true;
        public bool AbbreviateNumbers { get; set; } = true;
        public string ActLogDirectory { get; set; } = "";
        public string FflogsClientId { get; set; } = "";
        public string FflogsClientSecret { get; set; } = "";
        public string RankingsPath { get; set; } = "";
        public float PullIdleSeconds { get; set; } = 25f;

        public bool NinjaMudraKeys { get; set; } = true;

        /// <summary>
        /// Virtual key code for each ninjutsu id. Zero means the jutsu has no key.
        /// </summary>
        public Dictionary<string, int> NinjaMudraKey { get; set; } = new();

        /// <summary>
        /// Hotbar slot chosen for each ninjutsu icon. Bars are 1–10 and slots are 1–12.
        /// </summary>
        public Dictionary<string, MudraHotbarSlot> NinjaMudraSlot { get; set; } = new();

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

        public int MudraKey(string jutsuId)
        {
            if (NinjaMudraKey != null && NinjaMudraKey.TryGetValue(jutsuId, out var key))
                return key;
            return 0;
        }

        public void SetMudraKey(string jutsuId, int key)
        {
            NinjaMudraKey ??= new Dictionary<string, int>();
            if (key != 0)
            {
                var taken = new List<string>();
                foreach (var pair in NinjaMudraKey)
                {
                    if (pair.Value == key && pair.Key != jutsuId)
                        taken.Add(pair.Key);
                }

                foreach (var owner in taken)
                    NinjaMudraKey.Remove(owner);
            }

            if (key == 0)
                NinjaMudraKey.Remove(jutsuId);
            else
                NinjaMudraKey[jutsuId] = key;
            Save();
        }

        public MudraHotbarSlot MudraSlot(string jutsuId)
        {
            NinjaMudraSlot ??= new Dictionary<string, MudraHotbarSlot>();
            if (NinjaMudraSlot.TryGetValue(jutsuId, out var saved) && saved != null)
            {
                saved.Hotbar = Math.Clamp(saved.Hotbar, 1, 10);
                saved.Slot = Math.Clamp(saved.Slot, 1, 12);
                return saved;
            }

            var index = 0;
            foreach (var jutsu in Ninja.MudraTable.All)
            {
                if (jutsu.Id == jutsuId)
                    break;
                index++;
            }

            var created = new MudraHotbarSlot
            {
                Hotbar = 1,
                Slot = Math.Clamp(index + 1, 1, 12),
            };
            NinjaMudraSlot[jutsuId] = created;
            return created;
        }

        public void SetMudraSlotChoice(string jutsuId, int hotbar, int slot)
        {
            var entry = MudraSlot(jutsuId);
            hotbar = Math.Clamp(hotbar, 1, 10);
            slot = Math.Clamp(slot, 1, 12);
            if (entry.Hotbar == hotbar && entry.Slot == slot)
                return;

            entry.Hotbar = hotbar;
            entry.Slot = slot;
            entry.Placed = false;
            Save();
        }

        public void MarkMudraPlaced(string jutsuId)
        {
            var entry = MudraSlot(jutsuId);
            entry.Placed = true;
            if (NinjaMudraSlot != null)
            {
                foreach (var pair in NinjaMudraSlot)
                {
                    if (pair.Key == jutsuId || pair.Value == null)
                        continue;
                    if (pair.Value.Hotbar == entry.Hotbar && pair.Value.Slot == entry.Slot)
                        pair.Value.Placed = false;
                }
            }

            Save();
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

    public sealed class MudraHotbarSlot
    {
        public int Hotbar { get; set; } = 1;
        public int Slot { get; set; } = 1;
        public bool Placed { get; set; }
    }
}
