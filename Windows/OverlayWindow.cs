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
                DrawLines();
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private void DrawLines()
        {
            BuildLines();
            if (lines.Count == 0)
            {
                ImGui.Dummy(new Vector2(8, 8));
                return;
            }

            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            float fontSize = ImGui.GetFontSize();
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();
            var origin = ImGui.GetCursorScreenPos();
            float maxWidth = 40f;
            const uint shadowColor = 0xCC000000;
            var shadow = new Vector2(2f, 2f);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var color = line.Expiring ? Pulse(line.Color, line.Remaining) : line.Color;
                var text = line.Expiring ? $"⚠ {line.Text}   EXPIRING" : line.Text;
                var size = ImGui.CalcTextSize(text);
                if (size.X > maxWidth)
                    maxWidth = size.X;

                var pos = new Vector2(origin.X, origin.Y + (lineHeight * i));
                drawList.AddText(font, fontSize, pos + shadow, shadowColor, text);
                drawList.AddText(font, fontSize, pos, ImGui.ColorConvertFloat4ToU32(color), text);
            }

            ImGui.Dummy(new Vector2(maxWidth, lineHeight * lines.Count));
        }

        private void BuildLines()
        {
            lines.Clear();

            if (SongVisible())
            {
                var threshold = SongThreshold();
                var expiring = songTracker.IsExpiring(threshold);
                lines.Add(new OverlayLine(
                    $"{songTracker.ShortName}   {songTracker.RemainingTime:0.0}s",
                    songTracker.SongColorVec4,
                    expiring,
                    songTracker.RemainingTime));
            }

            if (!configuration.TrackDots)
                return;

            foreach (var dot in dotTracker.Active)
            {
                var expiring = dot.Remaining > 0f && dot.Remaining <= configuration.DotAlertThreshold;
                if (!configuration.ShowDotTimer && !expiring)
                    continue;

                lines.Add(new OverlayLine(
                    $"{dot.Label}   {dot.Remaining:0.0}s",
                    DotCatalog.RoleColor(dot.Role),
                    expiring,
                    dot.Remaining));
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

        private readonly record struct OverlayLine(string Text, Vector4 Color, bool Expiring, float Remaining);
    }
}
