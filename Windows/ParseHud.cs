using Dalamud.Bindings.ImGui;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        private static readonly Vector4 Gold = new(0.90f, 0.80f, 0.50f, 1f);

        public static void Draw(ParseSnapshot snap, Vector2 size)
        {
            if (size.X < 8f || size.Y < 8f)
                return;

            var rows = snap.Rows ?? Array.Empty<MeterRow>();
            var header = Math.Clamp(size.Y * 0.22f, 34f, 58f);
            var body = Math.Max(8f, size.Y - header - 4f);
            var rowCount = Math.Max(rows.Length, 1);
            var rowH = Math.Clamp(body / rowCount, 22f, 46f);
            var fontSize = Math.Clamp(rowH * 0.42f, 12f, 20f);
            var baseSize = ImGui.GetFontSize();
            var scale = baseSize <= 0f ? 1f : fontSize / baseSize;

            ImGui.SetWindowFontScale(scale);
            try
            {
                DrawHeader(YourDps(rows), size.X, header, fontSize);
                if (rows.Length == 0)
                {
                    DrawNote(snap.Note, header + 6f, size.X, fontSize);
                    return;
                }

                var y = header + 6f;
                foreach (var row in rows)
                {
                    if (y + rowH > size.Y + 1f)
                        break;

                    DrawRow(row, y, size.X, rowH - 3f, fontSize);
                    y += rowH;
                }
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private static double YourDps(MeterRow[] rows)
        {
            foreach (var row in rows)
            {
                if (row.You)
                    return row.Dps;
            }

            return 0;
        }

        private static void DrawHeader(double dps, float width, float height, float fontSize)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var numberSize = fontSize * 1.35f;
            var labelSize = fontSize * 0.72f;
            var number = dps.ToString("N0");
            var title = ImGui.ColorConvertFloat4ToU32(new Vector4(0.97f, 0.97f, 0.98f, 1f));
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var edge = ImGui.ColorConvertFloat4ToU32(Gold);
            var min = origin;
            var max = origin + new Vector2(width, height);
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.04f)), 4f);
            drawList.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), edge, 2f);

            var stack = numberSize + labelSize;
            var textY = min.Y + Math.Max(2f, (height - stack) * 0.5f);
            drawList.AddText(font, numberSize, new Vector2(min.X + 12f, textY), title, number);
            drawList.AddText(font, labelSize, new Vector2(min.X + 12f, textY + numberSize), muted, "rDPS");
        }

        private static void DrawNote(string note, float y, float width, float fontSize)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.75f, 0.78f, 0.84f, 1f));
            var text = string.IsNullOrWhiteSpace(note) ? "Waiting for a pull." : note;
            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X, origin.Y + y), muted, Fit(text, width));
        }

        private static void DrawRow(MeterRow row, float y, float width, float rowH, float fontSize)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var parse = (int)Math.Clamp(Math.Round(row.Share), 0, 100);
            var color = PercentileScale.Color(parse);
            var min = origin + new Vector2(0f, y);
            var max = origin + new Vector2(width, y + rowH);
            var track = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.045f, 0.06f, 0.55f));
            var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, row.You ? 0.42f : 0.28f));
            var nameColor = ImGui.ColorConvertFloat4ToU32(row.You
                ? new Vector4(0.98f, 0.98f, 0.99f, 1f)
                : new Vector4(0.90f, 0.92f, 0.95f, 1f));
            var packed = ImGui.ColorConvertFloat4ToU32(color);
            var edge = ImGui.ColorConvertFloat4ToU32(Gold);

            drawList.AddRectFilled(min, max, track, 4f);
            var fraction = parse / 100f;
            if (fraction > 0f)
                drawList.AddRectFilled(min, new Vector2(min.X + (width * fraction), max.Y), fill, 4f);
            drawList.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), edge, 2f);

            var number = parse.ToString();
            var labelSize = fontSize * 0.68f;
            var showCaption = rowH >= 30f;
            var numberWidth = Width(number, fontSize, fontSize);
            var block = showCaption ? fontSize + labelSize : fontSize;
            var textY = min.Y + Math.Max(1f, (rowH - block) * 0.5f);
            var nameY = min.Y + Math.Max(1f, (rowH - fontSize) * 0.5f);
            var numberX = max.X - 10f - numberWidth;
            drawList.AddText(font, fontSize, new Vector2(min.X + 12f, nameY), nameColor, Fit(FirstName(row.Name), Math.Max(24f, numberX - min.X - 20f)));
            drawList.AddText(font, fontSize, new Vector2(numberX, textY), packed, number);
            if (showCaption)
            {
                var caption = "Parse";
                var captionWidth = Width(caption, labelSize, fontSize);
                drawList.AddText(font, labelSize, new Vector2(max.X - 10f - captionWidth, textY + fontSize), packed, caption);
            }
        }

        private static string FirstName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Player";

            var trimmed = name.Trim();
            var space = trimmed.IndexOf(' ');
            return space <= 0 ? trimmed : trimmed[..space];
        }

        private static string Fit(string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
                return text ?? "";

            var cut = text;
            while (cut.Length > 1 && ImGui.CalcTextSize(cut + "…").X > maxWidth)
                cut = cut[..^1];

            return cut + "…";
        }

        private static float Width(string text, float size, float fontSize)
        {
            if (fontSize <= 0f)
                return 0f;

            return ImGui.CalcTextSize(text).X * (size / fontSize);
        }
    }
}
