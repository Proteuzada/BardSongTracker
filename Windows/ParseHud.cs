using Dalamud.Bindings.ImGui;
using BardSongTracker.Parse;
using System;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        public static void Draw(ParseSnapshot snap, Vector2 size)
        {
            if (size.X < 8f || size.Y < 8f)
                return;

            var rows = snap.Rows ?? Array.Empty<MeterRow>();
            var header = Math.Clamp(size.Y * 0.16f, 16f, 28f);
            var body = Math.Max(8f, size.Y - header);
            var rowCount = Math.Max(rows.Length, 1);
            var rowH = Math.Clamp(body / rowCount, 16f, 40f);
            var fontSize = Math.Clamp(rowH * 0.46f, 11f, 22f);
            var baseSize = ImGui.GetFontSize();
            var scale = baseSize <= 0f ? 1f : fontSize / baseSize;

            ImGui.SetWindowFontScale(scale);
            try
            {
                DrawHeader(snap, size.X, header);
                if (rows.Length == 0)
                {
                    DrawNote(snap.Note, header, size.X, fontSize);
                    return;
                }

                long top = 1;
                foreach (var row in rows)
                    top = Math.Max(top, row.Damage);

                var y = header;
                foreach (var row in rows)
                {
                    if (y + rowH > size.Y + 1f)
                        break;

                    DrawRow(row, y, size.X, rowH, fontSize, top);
                    y += rowH;
                }
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        }

        private static void DrawHeader(ParseSnapshot snap, float width, float height)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var title = string.IsNullOrEmpty(snap.ZoneName) ? "Damage" : snap.ZoneName;
            var clock = Clock(snap.Duration);
            var state = snap.InPull ? "In pull" : snap.Rows.Length > 0 ? "Locked" : "";
            var right = state.Length == 0 ? clock : $"{state}   {clock}";
            var textY = origin.Y + Math.Max(0f, (height - fontSize) * 0.5f);
            var rightWidth = ImGui.CalcTextSize(right).X;
            drawList.AddText(font, fontSize, new Vector2(origin.X, textY), muted, Fit(title, Math.Max(20f, width - rightWidth - 12f)));
            drawList.AddText(font, fontSize, new Vector2(origin.X + width - rightWidth, textY), muted, right);
        }

        private static void DrawNote(string note, float y, float width, float fontSize)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.75f, 0.78f, 0.84f, 1f));
            var text = string.IsNullOrWhiteSpace(note) ? "Waiting for a pull." : note;
            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X, origin.Y + y + 4f), muted, Fit(text, width));
        }

        private static void DrawRow(MeterRow row, float y, float width, float rowH, float fontSize, long top)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var color = JobColor(row.JobId);
            var min = origin + new Vector2(0f, y);
            var max = origin + new Vector2(width, y + rowH - 2f);
            var track = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.04f));
            var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, row.You ? 0.55f : 0.34f));
            var text = ImGui.ColorConvertFloat4ToU32(row.You
                ? new Vector4(0.98f, 0.98f, 0.99f, 1f)
                : new Vector4(0.86f, 0.88f, 0.92f, 1f));
            var accent = ImGui.ColorConvertFloat4ToU32(color);

            drawList.AddRectFilled(min, max, track, 3f);
            var fraction = top <= 0 ? 0f : Math.Clamp((float)row.Damage / top, 0f, 1f);
            if (fraction > 0f)
                drawList.AddRectFilled(min, new Vector2(min.X + (width * fraction), max.Y), fill, 3f);
            drawList.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), accent, 2f);

            var damage = row.Damage.ToString("N0");
            var share = $"{row.Share:0}%";
            var showDps = width >= 430f;
            var dps = row.Dps.ToString("N0");
            var shareWidth = Math.Max(ImGui.CalcTextSize("100%").X, ImGui.CalcTextSize(share).X);
            var damageWidth = Math.Max(ImGui.CalcTextSize("99,999,999").X, ImGui.CalcTextSize(damage).X);
            var dpsWidth = showDps ? Math.Max(ImGui.CalcTextSize("999,999").X, ImGui.CalcTextSize(dps).X) : 0f;
            const float gap = 10f;
            var textY = min.Y + Math.Max(0f, (rowH - 2f - fontSize) * 0.5f);
            var shareX = max.X - 8f - shareWidth;
            var damageX = shareX - gap - damageWidth;
            var dpsX = damageX - gap - dpsWidth;
            var nameMax = (showDps ? dpsX : damageX) - gap - (min.X + 10f);

            drawList.AddText(font, fontSize, new Vector2(min.X + 10f, textY), text, Fit(row.Name, Math.Max(24f, nameMax)));
            if (showDps)
                drawList.AddText(font, fontSize, new Vector2(dpsX, textY), text, dps);
            drawList.AddText(font, fontSize, new Vector2(damageX, textY), text, damage);
            drawList.AddText(font, fontSize, new Vector2(shareX, textY), text, share);
        }

        private static Vector4 JobColor(int jobId)
        {
            if (jobId <= 0)
                return new Vector4(0.72f, 0.74f, 0.78f, 1f);

            foreach (var job in DotCatalog.Jobs)
            {
                if (job.Id == (uint)jobId)
                    return DotCatalog.RoleColor(job.Role);
            }

            return new Vector4(0.72f, 0.74f, 0.78f, 1f);
        }

        private static string Clock(TimeSpan duration)
        {
            var seconds = Math.Max(0, (int)duration.TotalSeconds);
            return $"{seconds / 60}:{seconds % 60:00}";
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
