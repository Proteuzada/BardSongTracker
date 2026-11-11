using Dalamud.Bindings.ImGui;
using BardSongTracker.Parse;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        private static readonly Vector4 Gold = new(0.90f, 0.80f, 0.50f, 1f);

        public static Vector2 MinimumSize(float meterScale, bool onlyYou)
        {
            var scale = ClampScale(meterScale);
            var rows = onlyYou ? 1 : 8;
            return new Vector2(200f * Math.Clamp(scale, 1f, 1.6f), 12f + (14f * scale) + (15f * scale * rows));
        }

        public static void Draw(ParseSnapshot snap, Vector2 size, bool onlyYou = false, float meterScale = 1f)
        {
            if (size.X < 8f || size.Y < 8f)
                return;

            var rows = snap.Rows ?? Array.Empty<MeterRow>();
            var scale = ClampScale(meterScale);
            var header = 14f * scale;
            var rowH = 15f * scale;
            var fontSize = 11f * scale;
            var nameSize = 9.5f * scale;
            var baseSize = ImGui.GetFontSize();
            var fontScale = baseSize <= 0f ? 1f : fontSize / baseSize;

            ImGui.SetWindowFontScale(fontScale);
            try
            {
                var dpsCol = Math.Max(ImGui.CalcTextSize("rDPS").X, ImGui.CalcTextSize("999k").X);
                var parseCol = Math.Max(ImGui.CalcTextSize("Parse").X, ImGui.CalcTextSize("100").X);
                DrawHeader(size.X, header, dpsCol, parseCol);

                if (rows.Length == 0)
                {
                    DrawNote(snap.Note, header + 2f, size.X);
                    return;
                }

                var topByRole = new Dictionary<CombatRole, double>();
                foreach (var row in rows)
                {
                    var role = FightRole(row.JobId);
                    topByRole.TryGetValue(role, out var current);
                    if (row.Dps > current)
                        topByRole[role] = row.Dps;
                }

                var y = header + 2f;
                var shown = 0;
                foreach (var row in rows)
                {
                    if (onlyYou && !row.You)
                        continue;
                    if (shown >= 8 || y + rowH > size.Y + 1f)
                        break;

                    var roleTop = topByRole.TryGetValue(FightRole(row.JobId), out var best) ? best : 0d;
                    DrawRow(row, y, size.X, Math.Max(10f, rowH - Math.Max(2f, 2f * scale)), dpsCol, parseCol, roleTop, nameSize);
                    y += rowH;
                    shown++;
                }

                if (shown == 0)
                    DrawNote(onlyYou ? "Waiting for your damage." : snap.Note, header + 2f, size.X);
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
            var dpsX = origin.X + 8f + dpsCol - ImGui.CalcTextSize(dpsLabel).X;
            var parseX = origin.X + width - 8f - ImGui.CalcTextSize(parseLabel).X;
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
            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X, origin.Y + y), muted, Fit(text, width, fontSize));
        }

        private static void DrawRow(MeterRow row, float y, float width, float rowH, float dpsCol, float parseCol, double top, float nameSize)
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

            drawList.AddRectFilled(min, max, track, 2f);
            if (parse > 0)
                drawList.AddRectFilled(min, new Vector2(min.X + (width * (parse / 100f)), max.Y), fill, 2f);
            drawList.AddRectFilled(min, new Vector2(min.X + 2f, max.Y), edge, 1f);

            var textY = min.Y + Math.Max(0f, (rowH - fontSize) * 0.5f);
            var nameY = min.Y + Math.Max(0f, (rowH - nameSize) * 0.5f);
            var dpsText = Thousands(row.Dps);
            var dpsWidth = ImGui.CalcTextSize(dpsText).X;
            var dpsX = min.X + 8f + Math.Max(0f, dpsCol - dpsWidth);
            var nameX = min.X + 8f + dpsCol + 6f;
            var parseText = parse.ToString(CultureInfo.InvariantCulture);
            var parseX = max.X - 8f - parseCol;
            var nameWidth = Math.Max(16f, parseX - nameX - 6f);

            drawList.AddText(font, fontSize, new Vector2(dpsX, textY), numberColor, dpsText);
            drawList.AddText(font, nameSize, new Vector2(nameX, nameY), nameColor, Fit(FirstName(row.Name), nameWidth, nameSize));
            drawList.AddText(font, fontSize, new Vector2(parseX + parseCol - ImGui.CalcTextSize(parseText).X, textY), packed, parseText);
        }

        private static float ClampScale(float meterScale)
        {
            return Math.Clamp(meterScale, 0.8f, 2.5f);
        }

        private static CombatRole FightRole(int jobId)
        {
            var role = DotCatalog.RoleOf((uint)jobId);
            if (role != CombatRole.Limited)
                return role;

            return jobId switch
            {
                1 or 3 => CombatRole.Tank,
                6 => CombatRole.Healer,
                2 or 4 or 29 => CombatRole.Melee,
                5 => CombatRole.PhysicalRanged,
                7 or 26 => CombatRole.Caster,
                _ => CombatRole.Limited,
            };
        }

        private static string Thousands(double value)
        {
            if (value >= 1000d)
                return Math.Round(value / 1000d, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "k";

            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string FirstName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Player";

            var trimmed = name.Trim();
            var space = trimmed.IndexOf(' ');
            return space <= 0 ? trimmed : trimmed[..space];
        }

        private static string Fit(string text, float maxWidth, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            if (TextWidth(text, fontSize) <= maxWidth)
                return text;

            var cut = text;
            while (cut.Length > 1 && TextWidth(cut + "…", fontSize) > maxWidth)
                cut = cut[..^1];

            return cut + "…";
        }

        private static float TextWidth(string text, float fontSize)
        {
            var current = ImGui.GetFontSize();
            if (current <= 0f)
                return 0f;

            return ImGui.CalcTextSize(text).X * (fontSize / current);
        }
    }
}
