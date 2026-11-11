using Dalamud.Bindings.ImGui;
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
            var nameSize = fontSize * 1.22f;
            var statSize = fontSize * 1.08f;
            var parseSize = fontSize * 2.15f;
            const float pad = 12f;
            const float accent = 3f;
            const float chipWidth = 84f;
            const float gap = 16f;
            const float columnGap = 22f;

            var name = string.IsNullOrWhiteSpace(snap.PlayerName) ? "You" : snap.PlayerName;
            var meta = Meta(snap);
            var damage = snap.Damage.ToString("N0");
            var dps = snap.Dps.ToString("N0");
            var clock = Clock(snap.Duration);
            var status = Status(snap);
            var percentileText = snap.Percentile?.ToString() ?? "--";
            var color = snap.Percentile is int percentile
                ? PercentileScale.Color(percentile)
                : new Vector4(0.73f, 0.78f, 0.86f, 1f);

            var damageWidth = Math.Max(Width(damage, statSize, fontSize), Width("Damage", fontSize, fontSize));
            var dpsWidth = Math.Max(Width(dps, statSize, fontSize), Width("pDPS", fontSize, fontSize));
            var timeWidth = Math.Max(Width(clock, statSize, fontSize), Width("Time", fontSize, fontSize));
            var statsWidth = damageWidth + columnGap + dpsWidth + columnGap + timeWidth;

            var left = statsWidth;
            left = Math.Max(left, Width(name, nameSize, fontSize));
            left = Math.Max(left, Width(meta, fontSize, fontSize));
            left = Math.Max(left, Math.Min(Width(status, fontSize, fontSize), 340f));
            left = Math.Max(left, 250f);

            var width = pad + accent + 10f + left + gap + chipWidth + pad;
            var block = nameSize + 5f + fontSize + 12f + statSize + 3f + fontSize + 10f + fontSize;
            var height = Math.Max(pad * 2f + block, pad * 2f + parseSize + fontSize + 10f);

            var origin = ImGui.GetCursorScreenPos();
            var max = origin + new Vector2(width, height);
            var drawList = ImGui.GetWindowDrawList();
            var packed = ImGui.ColorConvertFloat4ToU32(color);
            var title = ImGui.ColorConvertFloat4ToU32(new Vector4(0.96f, 0.97f, 0.98f, 1f));
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.74f, 0.80f, 1f));
            var background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.045f, 0.05f, 0.07f, 0.9f));
            var border = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, 0.42f));
            var chip = ImGui.ColorConvertFloat4ToU32(new Vector4(
                0.05f + (color.X * 0.16f),
                0.055f + (color.Y * 0.16f),
                0.07f + (color.Z * 0.16f),
                0.95f));

            drawList.AddRectFilled(origin, max, background, 8f, ImDrawFlags.RoundCornersAll);
            drawList.AddRect(origin, max, border, 8f, ImDrawFlags.RoundCornersAll, 1.25f);
            drawList.AddRectFilled(
                new Vector2(origin.X + pad, origin.Y + pad),
                new Vector2(origin.X + pad + accent, max.Y - pad),
                packed,
                2f);

            var textX = origin.X + pad + accent + 10f;
            var y = origin.Y + pad;
            drawList.AddText(font, nameSize, new Vector2(textX, y), title, name);
            y += nameSize + 5f;
            drawList.AddText(font, fontSize, new Vector2(textX, y), muted, meta);
            y += fontSize + 12f;

            drawList.AddText(font, statSize, new Vector2(textX, y), title, damage);
            drawList.AddText(font, statSize, new Vector2(textX + damageWidth + columnGap, y), packed, dps);
            drawList.AddText(font, statSize, new Vector2(textX + damageWidth + columnGap + dpsWidth + columnGap, y), title, clock);
            y += statSize + 3f;
            drawList.AddText(font, fontSize, new Vector2(textX, y), muted, "Damage");
            drawList.AddText(font, fontSize, new Vector2(textX + damageWidth + columnGap, y), muted, "pDPS");
            drawList.AddText(font, fontSize, new Vector2(textX + damageWidth + columnGap + dpsWidth + columnGap, y), muted, "Time");
            y += fontSize + 10f;
            drawList.AddText(font, fontSize, new Vector2(textX, y), muted, status);

            var chipMin = new Vector2(max.X - pad - chipWidth, origin.Y + pad);
            var chipMax = new Vector2(max.X - pad, max.Y - pad);
            drawList.AddRectFilled(chipMin, chipMax, chip, 6f, ImDrawFlags.RoundCornersAll);
            drawList.AddRect(chipMin, chipMax, border, 6f, ImDrawFlags.RoundCornersAll, 1f);

            var numberWidth = Width(percentileText, parseSize, fontSize);
            var labelWidth = Width("parse", fontSize * 0.85f, fontSize);
            var stack = parseSize + 2f + (fontSize * 0.85f);
            var numberX = chipMin.X + ((chipWidth - numberWidth) * 0.5f);
            var numberY = chipMin.Y + (((chipMax.Y - chipMin.Y) - stack) * 0.5f);
            drawList.AddText(font, parseSize, new Vector2(numberX, numberY), packed, percentileText);
            drawList.AddText(
                font,
                fontSize * 0.85f,
                new Vector2(chipMin.X + ((chipWidth - labelWidth) * 0.5f), numberY + parseSize + 2f),
                muted,
                "parse");

            ImGui.Dummy(new Vector2(width, height));
        }

        private static string Meta(ParseSnapshot snap)
        {
            var zone = string.IsNullOrEmpty(snap.ZoneName) ? "No zone yet" : snap.ZoneName;
            var job = JobLabel(snap.JobId);
            return job.Length == 0 ? zone : $"{job}    {zone}";
        }

        private static string Status(ParseSnapshot snap)
        {
            if (snap.Damage <= 0)
                return Short(snap.Note);

            if (snap.InPull)
                return snap.Percentile is null ? "In pull    No ranking for this fight" : "In pull";

            return snap.Percentile is null ? "Pull locked    No ranking for this fight" : "Pull locked";
        }

        private static string Short(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return "Waiting for a pull";
            if (note.Contains("folder not found", StringComparison.OrdinalIgnoreCase))
                return "ACT log folder not found";
            if (note.Contains("No Network_", StringComparison.Ordinal))
                return "No network log in that folder";
            if (note.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
                return "Waiting for the ACT log";
            if (note.Contains("No percentile", StringComparison.OrdinalIgnoreCase))
                return "No ranking for this fight";
            return note.Length > 48 ? note[..45] + "..." : note;
        }

        private static string Clock(TimeSpan duration)
        {
            var seconds = Math.Max(0, (int)duration.TotalSeconds);
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        private static string JobLabel(int jobId)
        {
            if (jobId <= 0)
                return "";

            foreach (var job in DotCatalog.Jobs)
            {
                if (job.Id == (uint)jobId)
                    return job.Abbreviation;
            }

            return jobId.ToString();
        }

        private static float Width(string text, float size, float fontSize)
        {
            if (fontSize <= 0f)
                return 0f;

            return ImGui.CalcTextSize(text).X * (size / fontSize);
        }
    }
}
