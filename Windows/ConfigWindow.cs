using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
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
            Func<uint?> currentJobId) : base("BardSongTracker")
        {
            this.Size = new Vector2(780, 640);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(640, 480),
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
            if (!ImGui.BeginTabBar("bst-tabs"))
                return;

            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Bard"))
            {
                DrawBard();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("DoTs"))
            {
                DrawDots();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void DrawGeneral()
        {
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "Overlay and sound");
            ImGui.Separator();
            ImGui.TextWrapped("The overlay shows the Bard song and the current job's damage-over-time effects. A sound plays when one of them reaches the alert threshold.");

            Checkbox("Show overlay", configuration.ShowOverlay, value => configuration.ShowOverlay = value);
            Checkbox("Play sound", configuration.PlaySound, value => configuration.PlaySound = value);

            var scale = configuration.OverlayScale;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Overlay scale", ref scale, 0.6f, 3.0f, "%.1f"))
            {
                configuration.OverlayScale = scale;
                configuration.Save();
            }

            var volumePct = configuration.SoundVolume * 100f;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Volume", ref volumePct, 0f, 100f, "%.0f%%"))
            {
                configuration.SoundVolume = volumePct / 100f;
                soundAlert.SetVolume(configuration.SoundVolume);
                configuration.Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("Test sound"))
                soundAlert.PlayAlert(0f, ignoreCooldown: true);

            ImGui.Spacing();
            ImGui.TextDisabled("This volume only affects the plugin. It does not change the game volume.");
        }

        private void DrawBard()
        {
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "Bard songs");
            ImGui.Separator();
            ImGui.TextWrapped("Mage's Ballad, Army's Paeon, and The Wanderer's Minuet. Tracked only while you are on Bard.");

            Checkbox("Always show the song timer", configuration.ShowSongTimer, value => configuration.ShowSongTimer = value);
            ImGui.TextDisabled("When this is off, the song appears only while it is about to expire.");

            var threshold = configuration.AlertThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Ballad / Minuet threshold", ref threshold, 1f, 15f, "%.1f s"))
            {
                configuration.AlertThreshold = threshold;
                configuration.Save();
            }

            var paeon = configuration.ArmysPaeonThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Army's Paeon threshold", ref paeon, 1f, 15f, "%.1f s"))
            {
                configuration.ArmysPaeonThreshold = paeon;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text($"Current song: {songTracker.SongName}");
            ImGui.Text($"Time remaining: {songTracker.RemainingTime:0.0}s");
        }

        private void DrawDots()
        {
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "Damage over time by job");
            ImGui.Separator();

            Checkbox("Track damage-over-time effects", configuration.TrackDots, value => configuration.TrackDots = value);
            Checkbox("Always show DoT timers", configuration.ShowDotTimer, value => configuration.ShowDotTimer = value);

            var threshold = configuration.DotAlertThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Alert threshold", ref threshold, 1f, 15f, "%.1f s"))
            {
                configuration.DotAlertThreshold = threshold;
                configuration.Save();
            }

            ImGui.TextDisabled("In combat, only your current job is tracked. Enemy effects are read from your current target.");
            ImGui.TextDisabled("Doton, Salted Earth, and Slipstream use the ground-effect timer on you, not on the enemy.");
            ImGui.Text($"{configuration.EnabledDotCount()} of {DotCatalog.Skills.Count} skills enabled.");

            ImGui.Spacing();
            DrawRoleFilters();

            ImGui.SetNextItemWidth(260);
            ImGui.InputTextWithHint("##search", "Search job or skill", ref search, 64);
            ImGui.SameLine();
            Checkbox("Hide jobs without a DoT", hideJobsWithoutDots, value => hideJobsWithoutDots = value);
            ImGui.SameLine();
            if (ImGui.Button("Current job"))
                SelectCurrentJob();

            var visibleJobs = VisibleJobs();
            if (visibleJobs.Count > 0 && !ContainsJob(visibleJobs, selectedJobId))
                selectedJobId = visibleJobs[0].Id;

            var avail = ImGui.GetContentRegionAvail();
            var listHeight = Math.Max(160f, avail.Y - 4f);

            ImGui.BeginChild("bst-jobs", new Vector2(250, listHeight), true);
            DrawJobList(visibleJobs);
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("bst-skills", new Vector2(0, listHeight), true);
            DrawSkillList();
            ImGui.EndChild();
        }

        private void DrawRoleFilters()
        {
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
                    ? $"{job.Abbreviation}   {job.Name}"
                    : $"{job.Abbreviation}   {job.Name}   {enabled}/{total}";

                ImGui.PushStyleColor(ImGuiCol.Text, DotCatalog.RoleColor(job.Role));
                if (ImGui.Selectable($"{caption}##job-{job.Id}", selectedJobId == job.Id))
                    selectedJobId = job.Id;
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

            ImGui.TextColored(DotCatalog.RoleColor(job.Role), $"{job.Abbreviation}  {job.Name}");
            ImGui.Separator();

            var skills = DotCatalog.ForJob(job.Id);
            if (skills.Count == 0)
            {
                ImGui.TextWrapped("This job does not apply a player damage-over-time effect. Warrior, Reaper, Viper, Dancer, and Red Mage are listed for that reason.");
                return;
            }

            if (ImGui.Button("Enable all"))
                configuration.SetJobDots(job.Id, true);
            ImGui.SameLine();
            if (ImGui.Button("Disable all"))
                configuration.SetJobDots(job.Id, false);

            ImGui.Spacing();
            var shown = 0;
            foreach (var skill in skills)
            {
                if (!SkillMatches(job, skill))
                    continue;

                shown++;
                var enabled = configuration.IsDotEnabled(skill.Id);
                if (ImGui.Checkbox($"{skill.Name}##{skill.Id}", ref enabled))
                    configuration.SetDotEnabled(skill.Id, enabled);

                ImGui.SameLine();
                ImGui.TextDisabled(skill.Anchor == DotAnchor.Self ? "on you" : "on target");
                ImGui.TextDisabled($"   {skill.Detail}");
                ImGui.TextDisabled($"   {StatusLabels(skill)}");
                ImGui.Spacing();
            }

            if (shown == 0)
                ImGui.TextDisabled("No skills match this search.");

            if (dotTracker.Active.Count > 0 && currentJobId() == job.Id)
            {
                ImGui.Separator();
                ImGui.Text("Active on target / on you:");
                foreach (var dot in dotTracker.Active)
                    ImGui.BulletText($"{dot.Label}  {dot.Remaining:0.0}s");
            }
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
    }
}
