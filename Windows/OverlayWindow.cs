using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.JobGauge.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public class OverlayWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly SongTracker songTracker;
        private readonly DotTracker dotTracker;
        private readonly List<OverlayLine> lines = new();

        public OverlayWindow(Configuration configuration, SongTracker songTracker, DotTracker dotTracker) : base(
            "BardSongTrackerOverlay",
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
            this.songTracker = songTracker;
            this.dotTracker = dotTracker;
        }

        public void Dispose() { }

        public override bool DrawConditions()
        {
            if (!configuration.ShowOverlay)
                return false;

            return SongVisible() || DotsVisible();
        }

        public override void PreDraw()
        {
            var viewport = ImGui.GetMainViewport();
            var centerX = viewport.WorkPos.X + viewport.WorkSize.X * 0.5f;
            var topY = viewport.WorkPos.Y + viewport.WorkSize.Y * 0.22f;
            ImGui.SetNextWindowPos(new Vector2(centerX - 160, topY), ImGuiCond.FirstUseEver);
        }

        public override void Draw()
        {
            ImGui.SetWindowFontScale(configuration.OverlayScale);
            try
            {
                DrawPanel();
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private void DrawPanel()
        {
            BuildLines();
            if (lines.Count == 0)
            {
                ImGui.Dummy(new Vector2(8, 8));
                return;
            }

            var font = ImGui.GetFont();
            float fontSize = ImGui.GetFontSize();
            float rowHeight = ImGui.GetTextLineHeightWithSpacing();
            const float pad = 10f;
            const float accentWidth = 3f;
            const float gap = 10f;

            float nameWidth = 0f;
            float timerWidth = 0f;
            float badgeWidth = 0f;
            var anyExpiring = false;
            foreach (var line in lines)
            {
                nameWidth = Math.Max(nameWidth, ImGui.CalcTextSize(line.Name).X);
                timerWidth = Math.Max(timerWidth, ImGui.CalcTextSize(line.Timer).X);
                if (!line.Expiring)
                    continue;

                anyExpiring = true;
                badgeWidth = Math.Max(badgeWidth, ImGui.CalcTextSize("EXPIRING").X);
            }

            float badgeGap = anyExpiring ? gap : 0f;
            float width = pad + accentWidth + gap + nameWidth + gap + badgeWidth + badgeGap + timerWidth + pad;
            float height = pad * 2f;
            foreach (var line in lines)
                height += rowHeight + (line.GapBefore ? 6f : 0f);

            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var panelMax = origin + new Vector2(width, height);
            var background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.05f, 0.07f, 0.82f));
            var border = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.10f));
            drawList.AddRectFilled(origin, panelMax, background, 8f, ImDrawFlags.RoundCornersAll);
            drawList.AddRect(origin, panelMax, border, 8f, ImDrawFlags.RoundCornersAll, 1f);

            float y = origin.Y + pad;
            foreach (var line in lines)
            {
                if (line.GapBefore)
                    y += 6f;

                var color = line.Expiring ? Pulse(line.Color, line.Remaining) : line.Color;
                var packed = ImGui.ColorConvertFloat4ToU32(color);
                var textY = y + (rowHeight - fontSize) * 0.5f;
                var accentMin = new Vector2(origin.X + pad, y + 3f);
                var accentMax = new Vector2(origin.X + pad + accentWidth, y + rowHeight - 3f);
                drawList.AddRectFilled(accentMin, accentMax, packed, 2f);

                var nameX = origin.X + pad + accentWidth + gap;
                drawList.AddText(font, fontSize, new Vector2(nameX, textY), packed, line.Name);

                var timerX = origin.X + width - pad - timerWidth;
                if (line.Expiring)
                {
                    var badgeColor = line.Remaining <= 2f
                        ? new Vector4(1f, 0.35f, 0.32f, 1f)
                        : new Vector4(1f, 0.72f, 0.28f, 1f);
                    var badgeX = timerX - gap - badgeWidth;
                    drawList.AddText(font, fontSize, new Vector2(badgeX, textY), ImGui.ColorConvertFloat4ToU32(badgeColor), "EXPIRING");
                }

                drawList.AddText(font, fontSize, new Vector2(timerX, textY), packed, line.Timer);
                y += rowHeight;
            }

            ImGui.Dummy(new Vector2(width, height));
        }

        private void BuildLines()
        {
            lines.Clear();

            var hasSong = SongVisible();
            if (hasSong)
            {
                var threshold = SongThreshold();
                lines.Add(new OverlayLine(
                    songTracker.ShortName,
                    $"{songTracker.RemainingTime:0.0}s",
                    songTracker.SongColorVec4,
                    songTracker.IsExpiring(threshold),
                    songTracker.RemainingTime,
                    false));
            }

            if (!configuration.TrackDots)
                return;

            var firstDot = hasSong;
            foreach (var dot in dotTracker.Active)
            {
                var expiring = dot.Remaining > 0f && dot.Remaining <= configuration.DotAlertThreshold;
                if (!configuration.ShowDotTimer && !expiring)
                    continue;

                lines.Add(new OverlayLine(
                    dot.Label,
                    $"{dot.Remaining:0.0}s",
                    DotCatalog.RoleColor(dot.Role),
                    expiring,
                    dot.Remaining,
                    firstDot));
                firstDot = false;
            }
        }

        private bool SongVisible()
        {
            if (songTracker.CurrentSong == Song.None)
                return false;

            if (configuration.ShowSongTimer)
                return true;

            return songTracker.IsExpiring(SongThreshold());
        }

        private bool DotsVisible()
        {
            if (!configuration.TrackDots || dotTracker.Active.Count == 0)
                return false;

            if (configuration.ShowDotTimer)
                return true;

            return dotTracker.HasExpiring(configuration.DotAlertThreshold);
        }

        private float SongThreshold()
        {
            return songTracker.CurrentSong == Song.ArmysPaeon
                ? configuration.ArmysPaeonThreshold
                : configuration.AlertThreshold;
        }

        private static Vector4 Pulse(Vector4 baseColor, float remaining)
        {
            float pulseSpeed = remaining <= 2f ? 400f : 200f;
            float time = (float)DateTime.Now.TimeOfDay.TotalMilliseconds / pulseSpeed;
            float pulse = (float)(Math.Sin(time * Math.PI * 2) + 1.0) / 2f;
            var alert = remaining <= 2f
                ? new Vector4(1f, 0.15f, 0.15f, 1f)
                : new Vector4(1f, 0.6f, 0.0f, 1f);
            return Vector4.Lerp(baseColor, alert, pulse);
        }

        private readonly record struct OverlayLine(
            string Name,
            string Timer,
            Vector4 Color,
            bool Expiring,
            float Remaining,
            bool GapBefore);
    }
}
