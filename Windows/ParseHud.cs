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
            var fontSize = Math.Clamp(size.Y / Math.Max(rows.Length + 1.4f, 3f) * 0.46f, 12f, 20f);
            var baseSize = ImGui.GetFontSize();
            var scale = baseSize <= 0f ? 1f : fontSize / baseSize;
            var header = Math.Max(16f, fontSize + 6f);

            ImGui.SetWindowFontScale(scale);
            try
            {
                var dpsCol = Math.Max(ImGui.CalcTextSize("rDPS").X, ImGui.CalcTextSize("999,999").X);
                var parseCol = Math.Max(ImGui.CalcTextSize("Parse").X, ImGui.CalcTextSize("100").X);
                DrawHeader(size.X, header, dpsCol, parseCol);

                if (rows.Length == 0)
                {
                    DrawNote(snap.Note, header + 4f, size.X);
                    return;
                }

                var top = 0d;
                foreach (var row in rows)
                    top = Math.Max(top, row.Dps);

                var body = Math.Max(8f, size.Y - header - 4f);
                var rowH = Math.Clamp(body / rows.Length, 20f, 42f);
                var y = header + 4f;
                foreach (var row in rows)
                {
                    if (y + rowH > size.Y + 1f)
                        break;

                    DrawRow(row, y, size.X, rowH - 3f, dpsCol, parseCol, top);
                    y += rowH;
                }
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private static void DrawHeader(float width, float height, float dpsCol, float parseCol)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var textY = origin.Y + Math.Max(0f, (height - fontSize) * 0.5f);
            var dpsLabel = "rDPS";
            var parseLabel = "Parse";
            var dpsX = origin.X + 12f + dpsCol - ImGui.CalcTextSize(dpsLabel).X;
            var parseX = origin.X + width - 10f - ImGui.CalcTextSize(parseLabel).X;
            drawList.AddText(font, fontSize, new Vector2(dpsX, textY), muted, dpsLabel);
            drawList.AddText(font, fontSize, new Vector2(parseX, textY), muted, parseLabel);
        }

        private static void DrawNote(string note, float y, float width)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.75f, 0.78f, 0.84f, 1f));
            var text = string.IsNullOrWhiteSpace(note) ? "Waiting for a pull." : note;
            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X, origin.Y + y), muted, Fit(text, width));
        }

        private static void DrawRow(MeterRow row, float y, float width, float rowH, float dpsCol, float parseCol, double top)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var parse = top <= 0 || row.Dps <= 0 ? 0 : (int)Math.Clamp(Math.Floor(row.Dps * 100d / top), 0, 100);
            var color = PercentileScale.Color(parse);
            var min = origin + new Vector2(0f, y);
            var max = origin + new Vector2(width, y + rowH);
            var track = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.045f, 0.06f, 0.55f));
            var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, row.You ? 0.42f : 0.28f));
            var nameColor = ImGui.ColorConvertFloat4ToU32(row.You
                ? new Vector4(0.98f, 0.98f, 0.99f, 1f)
                : new Vector4(0.90f, 0.92f, 0.95f, 1f));
            var numberColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.96f, 0.97f, 0.98f, 1f));
            var packed = ImGui.ColorConvertFloat4ToU32(color);
            var edge = ImGui.ColorConvertFloat4ToU32(Gold);

            drawList.AddRectFilled(min, max, track, 4f);
            if (parse > 0)
                drawList.AddRectFilled(min, new Vector2(min.X + (width * (parse / 100f)), max.Y), fill, 4f);
            drawList.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), edge, 2f);

            var textY = min.Y + Math.Max(0f, (rowH - fontSize) * 0.5f);
            var dpsText = row.Dps.ToString("N0");
            var dpsWidth = ImGui.CalcTextSize(dpsText).X;
            var dpsX = min.X + 12f + Math.Max(0f, dpsCol - dpsWidth);
            var nameX = min.X + 12f + dpsCol + 10f;
            var parseText = parse.ToString();
            var parseX = max.X - 10f - parseCol;
            var nameWidth = Math.Max(16f, parseX - nameX - 8f);

            drawList.AddText(font, fontSize, new Vector2(dpsX, textY), numberColor, dpsText);
            drawList.AddText(font, fontSize, new Vector2(nameX, textY), nameColor, Fit(FirstName(row.Name), nameWidth));
            drawList.AddText(font, fontSize, new Vector2(parseX + parseCol - ImGui.CalcTextSize(parseText).X, textY), packed, parseText);
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
    }
}
