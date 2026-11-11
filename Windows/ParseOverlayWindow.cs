using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public class ParseOverlayWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly ActLogFollower follower;

        public ParseOverlayWindow(Configuration configuration, ActLogFollower follower) : base(
            "BardSongTrackerParse",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav)
        {
            this.IsOpen = true;
            this.RespectCloseHotkey = false;
            this.SizeCondition = ImGuiCond.Always;
            this.configuration = configuration;
            this.follower = follower;
        }

        public void Dispose() { }

        public override bool DrawConditions()
        {
            return configuration.ShowParseOverlay;
        }

        public override void PreDraw()
        {
            var viewport = ImGui.GetMainViewport();
            var x = viewport.WorkPos.X + 28f;
            var y = viewport.WorkPos.Y + (viewport.WorkSize.Y * 0.32f);
            ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.FirstUseEver);
        }

        public override void Draw()
        {
            var scale = configuration.OverlayScale;
            ImGui.SetWindowFontScale(scale);
            try
            {
                DrawPanel(follower.Snapshot());
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private static void DrawPanel(ParseSnapshot snap)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var bigSize = fontSize * 2.15f;
            var row = ImGui.GetTextLineHeightWithSpacing();
            const float pad = 12f;

            var percentileText = snap.Percentile?.ToString() ?? "--";
            var dpsText = $"{snap.Dps:N0}  pDPS";
            var zone = string.IsNullOrEmpty(snap.ZoneName) ? "No zone yet" : snap.ZoneName;
            var job = JobLabel(snap.JobId);
            var clock = snap.Duration.TotalSeconds < 1
                ? "0:00"
                : $"{(int)snap.Duration.TotalMinutes}:{snap.Duration.Seconds:00}";
            var state = snap.InPull ? "In pull" : snap.Damage > 0 ? "Pull locked" : "Waiting for a pull";
            var detail = $"{job}   {state}   {clock}";
            var note = snap.Note ?? "";

            float width = pad * 2;
            width = Math.Max(width, pad * 2 + ImGui.CalcTextSize(zone).X);
            width = Math.Max(width, pad * 2 + ImGui.CalcTextSize(dpsText).X);
            width = Math.Max(width, pad * 2 + ImGui.CalcTextSize(detail).X);
            width = Math.Max(width, pad * 2 + ImGui.CalcTextSize(note).X);
            width = Math.Max(width, pad * 2 + (bigSize * 0.62f * percentileText.Length));
            width = Math.Max(width, 220f);

            var height = pad * 2 + bigSize + (row * 4);
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var max = origin + new Vector2(width, height);
            var color = snap.Percentile is int percentile
                ? PercentileScale.Color(percentile)
                : new Vector4(0.78f, 0.86f, 0.98f, 1f);
            var packed = ImGui.ColorConvertFloat4ToU32(color);
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.82f, 0.84f, 0.88f, 1f));
            var background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.05f, 0.07f, 0.82f));
            var border = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, 0.55f));

            drawList.AddRectFilled(origin, max, background, 8f, ImDrawFlags.RoundCornersAll);
            drawList.AddRect(origin, max, border, 8f, ImDrawFlags.RoundCornersAll, 1.5f);

            var y = origin.Y + pad;
            drawList.AddText(font, bigSize, new Vector2(origin.X + pad, y), packed, percentileText);
            y += bigSize;
            drawList.AddText(font, fontSize, new Vector2(origin.X + pad, y), packed, dpsText);
            y += row;
            drawList.AddText(font, fontSize, new Vector2(origin.X + pad, y), muted, zone);
            y += row;
            drawList.AddText(font, fontSize, new Vector2(origin.X + pad, y), muted, detail);
            y += row;
            if (note.Length > 0)
                drawList.AddText(font, fontSize, new Vector2(origin.X + pad, y), muted, note);

            ImGui.Dummy(new Vector2(width, height));
        }

        private static string JobLabel(int jobId)
        {
            if (jobId == 0)
                return "JOB";

            foreach (var job in DotCatalog.Jobs)
            {
                if (job.Id == (uint)jobId)
                    return job.Abbreviation;
            }

            return jobId.ToString();
        }
    }
}
