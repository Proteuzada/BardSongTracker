using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        public static void Draw(ParseSnapshot snap, IUiBuilder ui)
        {
            var fontSize = ImGui.GetFontSize();
            var width = Math.Max(540f, fontSize * 36f);
            var plotHeight = Math.Max(118f, fontSize * 7.4f);
            var height = (fontSize * 8.6f) + plotHeight;
            var color = Band(snap.Percentile);

            using var rounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 12f);
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
            using var frame = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 8f);
            using var background = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.06f, 0.08f, 0.94f));
            using var border = ImRaii.PushColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.7f));
            using var child = ImRaii.Child("bst-parse-hud", new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar);
            if (!child)
                return;

            DrawIdentity(snap, ui, color, width - 132f);
            ImGui.Spacing();
            DrawStats(snap, ui, color);
            ImGui.Spacing();
            DrawLadder(snap, new Vector2(ImGui.GetContentRegionAvail().X, plotHeight), color);
        }

        private static void DrawIdentity(ParseSnapshot snap, IUiBuilder ui, Vector4 color, float textWidth)
        {
            var name = string.IsNullOrWhiteSpace(snap.PlayerName) ? "You" : snap.PlayerName;
            var zone = string.IsNullOrEmpty(snap.ZoneName) ? "No zone yet" : snap.ZoneName;
            var job = JobLabel(snap.JobId);
            var badgeWidth = 96f;
            var rowHeight = ImGui.GetFrameHeight() * 2.7f;
            var top = ImGui.GetCursorPos();

            ImGui.BeginGroup();
            Icon(ui.IconFontHandle, FontAwesomeIcon.User, color);
            ImGui.SameLine();
            ImGui.Text(name);
            if (job.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(color, job);
            }

            Icon(ui.IconFontHandle, FontAwesomeIcon.MapMarkerAlt, ImGuiColors.DalamudGrey);
            ImGui.SameLine();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(80f, textWidth));
            ImGui.TextDisabled(zone);
            ImGui.PopTextWrapPos();
            ImGui.TextDisabled(Status(snap));
            ImGui.EndGroup();

            var block = ImGui.GetItemRectSize().Y;
            ImGui.SetCursorPos(new Vector2(top.X + textWidth, top.Y));
            DrawBadge(snap, color, new Vector2(badgeWidth, Math.Max(rowHeight, block)));
            ImGui.SetCursorPos(new Vector2(top.X, top.Y + Math.Max(block, rowHeight)));
        }

        private static void DrawBadge(ParseSnapshot snap, Vector4 color, Vector2 size)
        {
            using var rounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 8f);
            using var background = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(color.X, color.Y, color.Z, 0.16f));
            using var border = ImRaii.PushColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.85f));
            using var badge = ImRaii.Child("bst-parse-badge", size, true, ImGuiWindowFlags.NoScrollbar);
            if (!badge)
                return;

            var number = snap.Percentile?.ToString() ?? "--";
            var numberWidth = ImGui.CalcTextSize(number).X;
            ImGui.SetCursorPosX(Math.Max(0f, (size.X - numberWidth) * 0.5f));
            ImGui.TextColored(color, number);
            var labelWidth = ImGui.CalcTextSize("PARSE").X;
            ImGui.SetCursorPosX(Math.Max(0f, (size.X - labelWidth) * 0.5f));
            ImGui.TextDisabled("PARSE");
        }

        private static void DrawStats(ParseSnapshot snap, IUiBuilder ui, Vector4 color)
        {
            using var table = ImRaii.Table(
                "bst-parse-stats",
                3,
                ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.NoSavedSettings);
            if (!table)
                return;

            ImGui.TableNextColumn();
            Stat(ui, FontAwesomeIcon.Burst, "Damage", snap.Damage.ToString("N0"), color);
            ImGui.TableNextColumn();
            Stat(ui, FontAwesomeIcon.Bolt, "pDPS", snap.Dps.ToString("N0"), color);
            ImGui.TableNextColumn();
            Stat(ui, FontAwesomeIcon.Stopwatch, "Time", Clock(snap.Duration), color);
        }

        private static void Stat(IUiBuilder ui, FontAwesomeIcon icon, string label, string value, Vector4 color)
        {
            Icon(ui.IconFontHandle, icon, color);
            ImGui.SameLine();
            ImGuiComponents.TextWithLabel(label, value);
        }

        private static unsafe void DrawLadder(ParseSnapshot snap, Vector2 size, Vector4 color)
        {
            var ladder = snap.Ladder ?? Array.Empty<PercentileScale.Point>();
            var yMax = 1d;
            for (var i = 0; i < ladder.Length; i++)
                yMax = Math.Max(yMax, ladder[i].Dps);
            yMax = Math.Max(yMax, snap.Dps) * 1.08d;

            var plotFlags = ImPlotFlags.CanvasOnly | ImPlotFlags.NoFrame | ImPlotFlags.NoInputs | ImPlotFlags.NoChild;
            var axisFlags = ImPlotAxisFlags.Lock | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoHighlight;

            ImPlot.PushStyleColor(ImPlotCol.Bg, new Vector4(0.03f, 0.035f, 0.05f, 0.8f));
            ImPlot.PushStyleColor(ImPlotCol.AxisGrid, new Vector4(1f, 1f, 1f, 0.08f));
            ImPlot.PushStyleColor(ImPlotCol.AxisText, ImGuiColors.DalamudGrey);
            ImPlot.PushStyleColor(ImPlotCol.Line, new Vector4(0.82f, 0.86f, 0.94f, 1f));
            ImPlot.PushStyleColor(ImPlotCol.Border, new Vector4(color.X, color.Y, color.Z, 0.45f));

            if (ImPlot.BeginPlot("##bst-ladder", size, plotFlags))
            {
                ImPlot.SetupAxes("parse", "pDPS", axisFlags, axisFlags);
                ImPlot.SetupAxisLimits(ImAxis.X1, 0, 100, ImPlotCond.Always);
                ImPlot.SetupAxisLimits(ImAxis.Y1, 0, yMax, ImPlotCond.Always);

                if (ladder.Length >= 2)
                {
                    var xs = stackalloc double[ladder.Length];
                    var ys = stackalloc double[ladder.Length];
                    for (var i = 0; i < ladder.Length; i++)
                    {
                        xs[i] = ladder[i].Percentile;
                        ys[i] = ladder[i].Dps;
                    }

                    ImPlot.SetNextLineStyle(new Vector4(0.78f, 0.84f, 0.94f, 0.95f), 2.2f);
                    ImPlot.PlotLine("ladder", xs, ys, ladder.Length);
                }

                if (snap.Percentile is int percentile && snap.Dps > 0)
                {
                    var x = (double)percentile;
                    var y = snap.Dps;
                    ImPlot.SetNextLineStyle(color, 1.6f);
                    ImPlot.PlotInfLines("you", &x, 1);
                    ImPlot.SetNextMarkerStyle(ImPlotMarker.Circle, 8f, color, 1.5f, new Vector4(0.04f, 0.045f, 0.06f, 1f));
                    ImPlot.PlotScatter("score", &x, &y, 1);
                }

                ImPlot.EndPlot();
            }

            ImPlot.PopStyleColor(5);
        }

        private static void Icon(IFontHandle font, FontAwesomeIcon icon, Vector4 color)
        {
            using (font.Push())
                ImGui.TextColored(color, icon.ToIconString());
        }

        private static Vector4 Band(int? percentile)
        {
            if (percentile is not int value)
                return ImGuiColors.DalamudGrey2;

            if (value >= 100)
                return ImGuiColors.ParsedGold;
            if (value >= 99)
                return ImGuiColors.ParsedPink;
            if (value >= 95)
                return ImGuiColors.ParsedOrange;
            if (value >= 75)
                return ImGuiColors.ParsedPurple;
            if (value >= 50)
                return ImGuiColors.ParsedBlue;
            if (value >= 25)
                return ImGuiColors.ParsedGreen;
            return ImGuiColors.ParsedGrey;
        }

        private static string Status(ParseSnapshot snap)
        {
            if (snap.Damage <= 0)
                return Short(snap.Note);

            if (snap.InPull)
                return snap.Percentile is null ? "In pull  ·  no ranking for this fight" : "In pull";

            return snap.Percentile is null ? "Pull locked  ·  no ranking for this fight" : "Pull locked";
        }

        private static string Short(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return "Waiting for a pull";
            if (note.Contains("folder not found", StringComparison.OrdinalIgnoreCase))
                return "ACT log folder not found";
            if (note.Contains("No Network_", StringComparison.Ordinal))
                return "No network log in that folder";
            if (note.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
                return "Waiting for the ACT log";
            if (note.Contains("No percentile", StringComparison.OrdinalIgnoreCase))
                return "No ranking for this fight";
            return note.Length > 64 ? note[..61] + "..." : note;
        }

        private static string Clock(TimeSpan duration)
        {
            var seconds = Math.Max(0, (int)duration.TotalSeconds);
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        private static string JobLabel(int jobId)
        {
            if (jobId <= 0)
                return "";

            foreach (var job in DotCatalog.Jobs)
            {
                if (job.Id == (uint)jobId)
                    return job.Abbreviation;
            }

            return jobId.ToString();
        }
    }
}
