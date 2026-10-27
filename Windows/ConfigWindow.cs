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

            if (ImGui.BeginTabItem("Geral"))
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
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "Overlay e som");
            ImGui.Separator();
            ImGui.TextWrapped("O overlay mostra a música do Bardo e os DoTs do job atual. O som toca quando algum deles entra no limiar.");

            Checkbox("Mostrar overlay", configuration.ShowOverlay, value => configuration.ShowOverlay = value);
            Checkbox("Tocar som", configuration.PlaySound, value => configuration.PlaySound = value);

            var scale = configuration.OverlayScale;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Escala do overlay", ref scale, 0.6f, 3.0f, "%.1f"))
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
            if (ImGui.Button("Testar som"))
                soundAlert.PlayAlert(0f, ignoreCooldown: true);

            ImGui.Spacing();
            ImGui.TextDisabled("O volume é da sessão do plugin. Não mexe no volume do jogo.");
        }

        private void DrawBard()
        {
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "Músicas do Bardo");
            ImGui.Separator();
            ImGui.TextWrapped("Mage's Ballad, Army's Paeon e The Wanderer's Minuet. Só acompanha enquanto você está de Bard.");

            Checkbox("Mostrar o timer da música o tempo todo", configuration.ShowSongTimer, value => configuration.ShowSongTimer = value);
            ImGui.TextDisabled("Desligado, a música só aparece na tela quando está expirando.");

            var threshold = configuration.AlertThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Limiar Ballad / Minuet", ref threshold, 1f, 15f, "%.1f s"))
            {
                configuration.AlertThreshold = threshold;
                configuration.Save();
            }

            var paeon = configuration.ArmysPaeonThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Limiar Army's Paeon", ref paeon, 1f, 15f, "%.1f s"))
            {
                configuration.ArmysPaeonThreshold = paeon;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text($"Música agora: {songTracker.SongName}");
            ImGui.Text($"Tempo restante: {songTracker.RemainingTime:0.0}s");
        }

        private void DrawDots()
        {
            ImGui.TextColored(new Vector4(0.45f, 0.78f, 1f, 1f), "DoTs por job");
            ImGui.Separator();

            Checkbox("Rastrear DoTs", configuration.TrackDots, value => configuration.TrackDots = value);
            Checkbox("Mostrar o timer do DoT o tempo todo", configuration.ShowDotTimer, value => configuration.ShowDotTimer = value);

            var threshold = configuration.DotAlertThreshold;
            ImGui.SetNextItemWidth(280);
            if (ImGui.SliderFloat("Limiar do alerta", ref threshold, 1f, 15f, "%.1f s"))
            {
                configuration.DotAlertThreshold = threshold;
                configuration.Save();
            }

            ImGui.TextDisabled("Em combate só entra o job que você está jogando. O alvo atual mostra o DoT nele.");
            ImGui.TextDisabled("Doton, Salted Earth e Slipstream usam o tempo da poça em você, não no inimigo.");
            ImGui.Text($"{configuration.EnabledDotCount()} de {DotCatalog.Skills.Count} skills ligadas.");

            ImGui.Spacing();
            DrawRoleFilters();

            ImGui.SetNextItemWidth(260);
            ImGui.InputTextWithHint("##busca", "Buscar job ou skill", ref search, 64);
            ImGui.SameLine();
            Checkbox("Ocultar jobs sem DoT", hideJobsWithoutDots, value => hideJobsWithoutDots = value);
            ImGui.SameLine();
            if (ImGui.Button("Job atual"))
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
            RoleButton("Todos", null);
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
                ImGui.TextDisabled("Nenhum job nesse filtro.");
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
                ImGui.TextDisabled("Escolha um job.");
                return;
            }

            ImGui.TextColored(DotCatalog.RoleColor(job.Role), $"{job.Abbreviation}  {job.Name}");
            ImGui.Separator();

            var skills = DotCatalog.ForJob(job.Id);
            if (skills.Count == 0)
            {
                ImGui.TextWrapped("Este job não aplica um DoT de jogador. Warrior, Reaper, Viper, Dancer e Red Mage ficam nesta lista.");
                return;
            }

            if (ImGui.Button("Ligar todas"))
                configuration.SetJobDots(job.Id, true);
            ImGui.SameLine();
            if (ImGui.Button("Desligar todas"))
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
                ImGui.TextDisabled(skill.Anchor == DotAnchor.Self ? "em você" : "no alvo");
                ImGui.TextDisabled($"   {skill.Detail}");
                ImGui.TextDisabled($"   {StatusLabels(skill)}");
                ImGui.Spacing();
            }

            if (shown == 0)
                ImGui.TextDisabled("Nenhuma skill combina com a busca.");

            if (dotTracker.Active.Count > 0 && currentJobId() == job.Id)
            {
                ImGui.Separator();
                ImGui.Text("No alvo / em você agora:");
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
