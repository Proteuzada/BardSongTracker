using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        public static void Draw(ParseSnapshot snap)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var labelSize = fontSize * 0.78f;
            var dps = snap.Dps.ToString("N0");
            var parse = snap.Percentile?.ToString() ?? "--";
            var color = Band(snap.Percentile);

            var dpsWidth = Math.Max(ImGui.CalcTextSize(dps).X, Width("rDPS", labelSize, fontSize));
            var parseWidth = Math.Max(ImGui.CalcTextSize(parse).X, Width("Parse", labelSize, fontSize));
            const float padX = 10f;
            const float padY = 5f;
            const float gap = 14f;
            var width = (padX * 2f) + dpsWidth + gap + parseWidth;
            var height = (padY * 2f) + fontSize + labelSize;

            var origin = ImGui.GetCursorScreenPos();
            var max = origin + new Vector2(width, height);
            var drawList = ImGui.GetWindowDrawList();
            var background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.045f, 0.06f, 0.82f));
            var border = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, 0.55f));
            var title = ImGui.ColorConvertFloat4ToU32(new Vector4(0.96f, 0.97f, 0.98f, 1f));
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var packed = ImGui.ColorConvertFloat4ToU32(color);

            drawList.AddRectFilled(origin, max, background, 6f, ImDrawFlags.RoundCornersAll);
            drawList.AddRect(origin, max, border, 6f, ImDrawFlags.RoundCornersAll, 1f);

            var numberY = origin.Y + padY;
            var labelY = numberY + fontSize;
            var dpsX = origin.X + padX;
            var parseX = dpsX + dpsWidth + gap;
            drawList.AddText(font, fontSize, new Vector2(dpsX, numberY), title, dps);
            drawList.AddText(font, labelSize, new Vector2(dpsX, labelY), muted, "rDPS");
            drawList.AddText(font, fontSize, new Vector2(parseX, numberY), packed, parse);
            drawList.AddText(font, labelSize, new Vector2(parseX, labelY), packed, "Parse");

            ImGui.Dummy(new Vector2(width, height));
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

        private static float Width(string text, float size, float fontSize)
        {
            if (fontSize <= 0f)
                return 0f;

            return ImGui.CalcTextSize(text).X * (size / fontSize);
        }
    }
}
