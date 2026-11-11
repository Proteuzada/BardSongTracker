using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public class ParseOverlayWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly ActLogFollower follower;
        private readonly ITextureProvider textures;
        private readonly Func<int> linkState;
        private readonly Func<string> linkText;

        public ParseOverlayWindow(
            Configuration configuration,
            ActLogFollower follower,
            ITextureProvider textures,
            Func<int> linkState,
            Func<string> linkText) : base(
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
            this.textures = textures;
            this.linkState = linkState;
            this.linkText = linkText;
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
            var columns = Columns();
            var minimum = ParseHud.MinimumSize(
                configuration.MeterScale,
                configuration.ShowOnlyMyMeter,
                columns,
                ParseHud.DetailOpen && columns.HitDetails);
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = minimum,
                MaximumSize = new Vector2(1200, 900),
            };
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
            var columns = Columns();
            var minimum = ParseHud.MinimumSize(
                configuration.MeterScale,
                configuration.ShowOnlyMyMeter,
                columns,
                ParseHud.DetailOpen && columns.HitDetails);
            var height = ImGui.GetWindowHeight();
            var width = ImGui.GetWindowWidth();
            if (height < minimum.Y - 1f || width < minimum.X - 1f)
                ImGui.SetWindowSize(new Vector2(Math.Max(width, minimum.X), Math.Max(height, minimum.Y)));

            ParseHud.Draw(
                follower.Snapshot(configuration.KeepLastPull),
                ImGui.GetContentRegionAvail(),
                configuration.ShowOnlyMyMeter,
                configuration.MeterScale,
                textures,
                columns,
                linkState(),
                linkText());
        }

        private MeterColumns Columns()
        {
            var extra = !configuration.MeterMinimal;
            return new MeterColumns(
                extra && configuration.ShowDamagePercent,
                extra && configuration.ShowDeaths,
                extra && configuration.ShowEncounterLine,
                extra && configuration.ShowHitDetails,
                configuration.AbbreviateNumbers);
        }
    }
}
