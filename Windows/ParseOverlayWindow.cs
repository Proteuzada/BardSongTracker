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
            ImGui.SetWindowFontScale(configuration.OverlayScale);
            try
            {
                ParseHud.Draw(follower.Snapshot());
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }
    }
}
