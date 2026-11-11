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
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav)
        {
            this.IsOpen = true;
            this.RespectCloseHotkey = false;
            this.Size = new Vector2(280, 156);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(200, 148),
                MaximumSize = new Vector2(1200, 900),
            };
            this.BgAlpha = 0.64f;
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
            var x = viewport.WorkPos.X + 24f;
            var y = viewport.WorkPos.Y + (viewport.WorkSize.Y * 0.28f);
            ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.FirstUseEver);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6f, 4f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(1f, 1f, 1f, 0.45f));
        }

        public override void PostDraw()
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        public override void Draw()
        {
            ParseHud.Draw(follower.Snapshot(), ImGui.GetContentRegionAvail());
        }
    }
}
