using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.JobGauge.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private const float SongDuration = 45f;

        private readonly Configuration configuration;
        private readonly SongTracker songTracker;
        private readonly DotTracker dotTracker;
        private readonly SoundAlert soundAlert;
        private readonly Func<uint?> currentJobId;

        private CombatRole? roleFilter;
        private uint selectedJobId = 24;
        private string search = string.Empty;
        private bool hideJobsWithoutDots;

        public ConfigWindow(
            Configuration configuration,
            SongTracker songTracker,
            DotTracker dotTracker,
            SoundAlert soundAlert,
            Func<uint?> currentJobId) : base("Bard Song Tracker")
        {
            this.Size = new Vector2(920, 700);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(760, 560),
                MaximumSize = new Vector2(1400, 1200),
            };

            this.configuration = configuration;
            this.songTracker = songTracker;
            this.dotTracker = dotTracker;
            this.soundAlert = soundAlert;
            this.currentJobId = currentJobId;
        }

        public void Dispose() { }

        public override void Draw()
        {
            DrawHeader();
            ImGui.Spacing();

            if (!ImGui.BeginTabBar("bst-tabs"))
                return;

            if (ImGui.BeginTabItem("General"))
            {
                ImGui.Spacing();
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Bard"))
            {
                ImGui.Spacing();
                DrawBard();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("DoTs"))
            {
                ImGui.Spacing();
                DrawDots();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void DrawHeader()
        {
            ImGui.TextColored(new Vector4(0.78f, 0.88f, 1f, 1f), "Bard Song Tracker");
            var status = HeaderStatus();
            var width = ImGui.CalcTextSize(status).X;
            var right = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - width));
            ImGui.TextDisabled(status);
        }

        private string HeaderStatus()
        {
            var parts = new List<string>();
            if (songTracker.CurrentSong != Song.None)
                parts.Add($"{songTracker.ShortName}  {songTracker.RemainingTime:0.0}s");

            var dots = dotTracker.Active.Count;
            if (dots == 1)
                parts.Add("1 DoT");
            else if (dots > 1)
                parts.Add($"{dots} DoTs");

            return parts.Count == 0 ? "Nothing active" : string.Join("   ·   ", parts);
        }

        private void DrawGeneral()
        {
            var avail = ImGui.GetContentRegionAvail();
            const float gap = 12f;
            var column = Math.Max(220f, (avail.X - gap) / 2f);

            BeginPanel("bst-card-overlay", new Vector2(column, 430f));
            PanelTitle("Overlay");
            ImGui.TextWrapped("Show the Bard song and the current job's damage-over-time effects.");
            ImGui.Spacing();
            Checkbox("Show overlay", configuration.ShowOverlay, value => configuration.ShowOverlay = value);
            ImGui.Spacing();
            var scale = configuration.OverlayScale;
            LabeledSlider("Overlay scale", "##overlay-scale", ref scale, 0.6f, 3.0f, "%.1f", value =>
            {
                configuration.OverlayScale = value;
                configuration.Save();
            });
            EndPanel();

            ImGui.SameLine(0f, gap);

            BeginPanel("bst-card-sound", new Vector2(column, 430f));
            PanelTitle("Sound");
            ImGui.TextWrapped("Play a sound when a song or a DoT reaches its alert threshold.");
            ImGui.Spacing();
            Checkbox("Play sound", configuration.PlaySound, value => configuration.PlaySound = value);
            ImGui.Spacing();
            var volumePct = configuration.SoundVolume * 100f;
            LabeledSlider("Volume", "##volume", ref volumePct, 0f, 100f, "%.0f%%", value =>
            {
                configuration.SoundVolume = value / 100f;
                soundAlert.SetVolume(configuration.SoundVolume);
                configuration.Save();
            });
            ImGui.Spacing();
            var soundIndex = AlertSounds.IndexOf(configuration.AlertSoundId);
            ImGui.Text("Alert sound");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##alert-sound", ref soundIndex, AlertSounds.Names, -1))
            {
                configuration.AlertSoundId = AlertSounds.All[soundIndex].Id;
                soundAlert.SetSound(configuration.AlertSoundId);
                configuration.Save();
            }
            ImGui.TextDisabled("Chimes are original. Voices are by DarkNightPrincess, CC0, not from a show.");
            ImGui.Spacing();
            if (ImGui.Button("Test sound", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
                soundAlert.PlayAlert(0f, ignoreCooldown: true);
            ImGui.Spacing();
            ImGui.TextDisabled("This volume only affects the plugin. It does not change the game volume.");
            EndPanel();
        }

        private void DrawBard()
        {
            BeginPanel("bst-now-playing", new Vector2(0f, 148f));
            PanelTitle("Now playing");
            var active = songTracker.CurrentSong != Song.None;
            var fraction = active ? Math.Clamp(songTracker.RemainingTime / SongDuration, 0f, 1f) : 0f;
            var barLabel = active ? $"{songTracker.SongName}    {songTracker.RemainingTime:0.0}s" : "No song active";
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, active ? songTracker.SongColorVec4 : new Vector4(0.35f, 0.35f, 0.38f, 1f));
            ImGui.ProgressBar(fraction, new Vector2(ImGui.GetContentRegionAvail().X, 26f), barLabel);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.TextDisabled("Tracked only while you are on Bard. Drag the overlay in game to move it.");
            EndPanel();

            ImGui.Spacing();

            BeginPanel("bst-song-options", new Vector2(0f, 0f));
            PanelTitle("Alert");
            Checkbox("Always show the song timer", configuration.ShowSongTimer, value => configuration.ShowSongTimer = value);
            ImGui.TextDisabled("When this is off, the song appears only while it is about to expire.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var threshold = configuration.AlertThreshold;
            LabeledSlider("Mage's Ballad and The Wanderer's Minuet", "##ballad-minuet", ref threshold, 1f, 15f, "%.1f s", value =>
            {
                configuration.AlertThreshold = value;
                configuration.Save();
            });
            ImGui.TextDisabled("These two songs share one threshold.");
            ImGui.Spacing();

            var paeon = configuration.ArmysPaeonThreshold;
            LabeledSlider("Army's Paeon", "##paeon", ref paeon, 1f, 15f, "%.1f s", value =>
            {
                configuration.ArmysPaeonThreshold = value;
                configuration.Save();
            });
            EndPanel();
        }

        private void DrawDots()
        {
            BeginPanel("bst-dot-options", new Vector2(0f, 196f));
            Checkbox("Track damage-over-time effects", configuration.TrackDots, value => configuration.TrackDots = value);
            Checkbox("Always show DoT timers", configuration.ShowDotTimer, value => configuration.ShowDotTimer = value);
            ImGui.Spacing();

            var threshold = configuration.DotAlertThreshold;
            var sliderWidth = Math.Min(360f, ImGui.GetContentRegionAvail().X);
            ImGui.Text("Alert threshold");
            ImGui.SetNextItemWidth(sliderWidth);
            if (ImGui.SliderFloat("##dot-threshold", ref threshold, 1f, 15f, "%.1f s"))
            {
                configuration.DotAlertThreshold = threshold;
                configuration.Save();
            }

            ImGui.TextDisabled("In combat, only your current job is tracked. Enemy effects are read from your current target.");
            ImGui.TextDisabled("Doton, Salted Earth, and Slipstream use the ground-effect timer on you, not on the enemy.");
            EndPanel();

            ImGui.Spacing();
            DrawRoleFilters();
            ImGui.Spacing();

            ImGui.SetNextItemWidth(Math.Max(180f, ImGui.GetContentRegionAvail().X - 130f));
            ImGui.InputTextWithHint("##search", "Search job or skill", ref search, 64);
            ImGui.SameLine();
            if (ImGui.Button("Current job", new Vector2(118f, 0f)))
                SelectCurrentJob();

            var count = $"{configuration.EnabledDotCount()} of {DotCatalog.Skills.Count} skills enabled";
            Checkbox("Hide jobs without a DoT", hideJobsWithoutDots, value => hideJobsWithoutDots = value);
            var countWidth = ImGui.CalcTextSize(count).X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - countWidth));
            ImGui.TextDisabled(count);

            var visibleJobs = VisibleJobs();
            if (visibleJobs.Count > 0 && !ContainsJob(visibleJobs, selectedJobId))
                selectedJobId = visibleJobs[0].Id;

            var listHeight = Math.Max(180f, ImGui.GetContentRegionAvail().Y - 4f);
            BeginPanel("bst-jobs", new Vector2(248f, listHeight));
            DrawJobList(visibleJobs);
            EndPanel();

            ImGui.SameLine();

            BeginPanel("bst-skills", new Vector2(0f, listHeight));
            DrawSkillList();
            EndPanel();
        }

        private void DrawRoleFilters()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            RoleButton("All", null);
            ImGui.SameLine();
            RoleButton("Tanks", CombatRole.Tank);
            ImGui.SameLine();
            RoleButton("Healers", CombatRole.Healer);
            ImGui.SameLine();
            RoleButton("Melee", CombatRole.Melee);
            ImGui.SameLine();
            RoleButton("Ranged", CombatRole.PhysicalRanged);
            ImGui.SameLine();
            RoleButton("Casters", CombatRole.Caster);
            ImGui.SameLine();
            RoleButton("Limited", CombatRole.Limited);
            ImGui.PopStyleVar();
        }

        private void RoleButton(string label, CombatRole? role)
        {
            var active = roleFilter == role;
            if (active)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.42f, 0.72f, 1f));

            if (ImGui.Button(label))
                roleFilter = role != null && roleFilter != role ? role : null;

            if (active)
                ImGui.PopStyleColor();
        }

        private void DrawJobList(List<JobInfo> jobs)
        {
            CombatRole? lastRole = null;
            if (jobs.Count == 0)
            {
                ImGui.TextDisabled("No jobs match this filter.");
                return;
            }

            foreach (var job in jobs)
            {
                if (lastRole != job.Role)
                {
                    if (lastRole != null)
                        ImGui.Spacing();

                    ImGui.TextColored(DotCatalog.RoleColor(job.Role), DotCatalog.RoleLabel(job.Role));
                    lastRole = job.Role;
                }

                var total = DotCatalog.CountForJob(job.Id);
                var enabled = CountEnabled(job.Id);
                var caption = total == 0
                    ? $"{job.Abbreviation}    {job.Name}"
                    : $"{job.Abbreviation}    {job.Name}    {enabled}/{total}";

                var selected = selectedJobId == job.Id;
                if (selected)
                {
                    var color = DotCatalog.RoleColor(job.Role);
                    ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(color.X, color.Y, color.Z, 0.45f));
                }

                if (ImGui.Selectable($"{caption}##job-{job.Id}", selected))
                    selectedJobId = job.Id;

                if (selected)
                    ImGui.PopStyleColor();
            }
        }

        private void DrawSkillList()
        {
            var job = FindJob(selectedJobId);
            if (job == null)
            {
                ImGui.TextDisabled("Select a job.");
                return;
            }

            ImGui.TextColored(DotCatalog.RoleColor(job.Role), $"{job.Abbreviation}   {job.Name}");
            ImGui.Spacing();

            var skills = DotCatalog.ForJob(job.Id);
            if (skills.Count == 0)
            {
                ImGui.TextWrapped("This job does not apply a tracked effect. Warrior, Viper, Dancer, and Red Mage are listed for that reason.");
                return;
            }

            if (ImGui.Button("Enable all"))
                configuration.SetJobDots(job.Id, true);
            ImGui.SameLine();
            if (ImGui.Button("Disable all"))
                configuration.SetJobDots(job.Id, false);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var shown = 0;
            foreach (var skill in skills)
            {
                if (!SkillMatches(job, skill))
                    continue;

                shown++;
                DrawSkillRow(skill);
            }

            if (shown == 0)
                ImGui.TextDisabled("No skills match this search.");

            if (dotTracker.Active.Count > 0 && currentJobId() == job.Id)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextDisabled("Active now");
                foreach (var dot in dotTracker.Active)
                {
                    var timer = $"{dot.Remaining:0.0}s";
                    ImGui.BulletText(dot.Label);
                    var timerWidth = ImGui.CalcTextSize(timer).X;
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - timerWidth));
                    ImGui.Text(timer);
                }
            }
        }

        private void DrawSkillRow(DotSkill skill)
        {
            var enabled = configuration.IsDotEnabled(skill.Id);
            if (ImGui.Checkbox($"{skill.Name}##{skill.Id}", ref enabled))
                configuration.SetDotEnabled(skill.Id, enabled);

            var place = skill.Anchor == DotAnchor.Self ? "on you" : "on target";
            var placeWidth = ImGui.CalcTextSize(place).X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - placeWidth));
            ImGui.TextDisabled(place);

            ImGui.Indent(26f);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextDisabled(skill.Detail);
            ImGui.TextDisabled(StatusLabels(skill));
            ImGui.PopTextWrapPos();
            ImGui.Unindent(26f);
            ImGui.Spacing();
        }

        private void SelectCurrentJob()
        {
            var jobId = currentJobId();
            if (jobId == null)
                return;

            selectedJobId = jobId.Value;
            roleFilter = null;
            search = string.Empty;
        }

        private List<JobInfo> VisibleJobs()
        {
            var jobs = new List<JobInfo>();
            foreach (var job in DotCatalog.Jobs)
            {
                if (roleFilter != null && job.Role != roleFilter)
                    continue;

                if (hideJobsWithoutDots && DotCatalog.CountForJob(job.Id) == 0)
                    continue;

                if (!JobMatches(job))
                    continue;

                jobs.Add(job);
            }

            return jobs;
        }

        private bool JobMatches(JobInfo job)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            if (job.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || job.Abbreviation.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var skill in DotCatalog.ForJob(job.Id))
            {
                if (SkillTextMatches(skill))
                    return true;
            }

            return false;
        }

        private bool SkillMatches(JobInfo job, DotSkill skill)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            if (job.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || job.Abbreviation.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            return SkillTextMatches(skill);
        }

        private bool SkillTextMatches(DotSkill skill)
        {
            if (skill.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || skill.Detail.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var status in skill.Statuses)
            {
                if (status.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private int CountEnabled(uint jobId)
        {
            var count = 0;
            foreach (var skill in DotCatalog.ForJob(jobId))
            {
                if (configuration.IsDotEnabled(skill.Id))
                    count++;
            }

            return count;
        }

        private static bool ContainsJob(List<JobInfo> jobs, uint jobId)
        {
            foreach (var job in jobs)
            {
                if (job.Id == jobId)
                    return true;
            }

            return false;
        }

        private static JobInfo? FindJob(uint jobId)
        {
            foreach (var job in DotCatalog.Jobs)
            {
                if (job.Id == jobId)
                    return job;
            }

            return null;
        }

        private static string StatusLabels(DotSkill skill)
        {
            var labels = new List<string>();
            foreach (var status in skill.Statuses)
            {
                if (!labels.Contains(status.Label))
                    labels.Add(status.Label);
            }

            return string.Join(", ", labels);
        }

        private void Checkbox(string label, bool current, Action<bool> assign)
        {
            var value = current;
            if (!ImGui.Checkbox(label, ref value))
                return;

            assign(value);
            configuration.Save();
        }

        private static void LabeledSlider(string label, string id, ref float value, float min, float max, string format, Action<float> save)
        {
            ImGui.Text(label);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat(id, ref value, min, max, format))
                save(value);
        }

        private static void BeginPanel(string id, Vector2 size)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.04f));
            ImGui.BeginChild(id, size, true);
        }

        private static void EndPanel()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        private static void PanelTitle(string title)
        {
            ImGui.TextColored(new Vector4(0.78f, 0.86f, 0.98f, 1f), title);
            ImGui.Spacing();
        }
    }
}
