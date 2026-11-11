using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using BardSongTracker.Parse;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace BardSongTracker.Windows
{
    public static class ParseHud
    {
        private static readonly Dictionary<string, int> heldParse = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> seenParse = new(StringComparer.OrdinalIgnoreCase);
        private static string? selectedName;

        public static bool DetailOpen => selectedName != null;

        public static Vector2 MinimumSize(float meterScale, bool onlyYou, MeterColumns columns, bool detail)
        {
            var scale = ClampScale(meterScale);
            var rows = onlyYou ? 1 : 8;
            var top = columns.Encounter ? 14f * scale : 0f;
            var extra = detail ? 16f * scale : 0f;
            var width = 220f * Math.Clamp(scale, 1f, 1.6f);
            if (columns.DamagePercent)
                width += 36f * scale;
            if (!columns.Abbreviate)
                width += 36f * scale;

            return new Vector2(width, 12f + top + (14f * scale) + extra + (15f * scale * rows));
        }

        public static void Draw(ParseSnapshot snap, Vector2 size, bool onlyYou, float meterScale, ITextureProvider textures, MeterColumns columns)
        {
            if (size.X < 8f || size.Y < 8f)
                return;

            if (!columns.HitDetails)
                selectedName = null;

            var rows = snap.Rows ?? Array.Empty<MeterRow>();
            var scale = ClampScale(meterScale);
            var header = 14f * scale;
            var rowH = 15f * scale;
            var fontSize = 11f * scale;
            var nameSize = 9.5f * scale;
            var baseSize = ImGui.GetFontSize();
            var fontScale = baseSize <= 0f ? 1f : fontSize / baseSize;
            var origin = ImGui.GetCursorScreenPos();

            ImGui.SetWindowFontScale(fontScale);
            try
            {
                var top = 0f;
                if (columns.Encounter)
                {
                    DrawEncounter(snap, size.X, header);
                    top = header;
                }

                var dpsSample = columns.Abbreviate ? "999k" : "999999";
                var dpsCol = Math.Max(ImGui.CalcTextSize("rDPS").X, ImGui.CalcTextSize(dpsSample).X);
                var parseCol = Math.Max(ImGui.CalcTextSize("Parse").X, ImGui.CalcTextSize("100").X);
                var shareCol = columns.DamagePercent
                    ? Math.Max(ImGui.CalcTextSize("Dmg").X, ImGui.CalcTextSize("100%").X)
                    : 0f;
                var deathCol = columns.Deaths ? ImGui.CalcTextSize("88").X : 0f;
                DrawHeader(size.X, header, top, dpsCol, parseCol, shareCol, columns, snap.LastPull && !columns.Encounter);

                if (rows.Length == 0)
                {
                    heldParse.Clear();
                    seenParse.Clear();
                    selectedName = null;
                    DrawNote(snap.Note, top + header + 2f, size.X);
                    return;
                }

                var y = top + header + 2f;
                var shown = 0;
                var reserve = columns.HitDetails && selectedName != null ? 16f * scale : 0f;
                string? clicked = null;
                foreach (var row in rows)
                {
                    if (onlyYou && !row.You)
                        continue;
                    if (shown >= 8 || y + rowH > size.Y - reserve + 1f)
                        break;

                    var raw = RoleParse(row, rows, snap.Duration.TotalSeconds);
                    if (DrawRow(row, y, size.X, Math.Max(10f, rowH - Math.Max(2f, 2f * scale)), dpsCol, parseCol, shareCol, deathCol, raw, nameSize, textures, columns))
                        clicked = row.Name;

                    y += rowH;
                    shown++;
                }

                if (clicked != null)
                    selectedName = string.Equals(selectedName, clicked, StringComparison.OrdinalIgnoreCase) ? null : clicked;

                MeterRow? detail = null;
                if (columns.HitDetails && selectedName != null)
                {
                    foreach (var row in rows)
                    {
                        if (onlyYou && !row.You)
                            continue;
                        if (!string.Equals(selectedName, row.Name, StringComparison.OrdinalIgnoreCase))
                            continue;

                        detail = row;
                        break;
                    }

                    if (detail == null)
                        selectedName = null;
                }

                if (shown == 0)
                    DrawNote(onlyYou ? "Waiting for your damage." : snap.Note, top + header + 2f, size.X);
                else if (detail is { } chosen)
                    DrawDetail(chosen, y, size.X, fontSize, columns.Abbreviate);

                ForgetUnseen();
            }
            finally
            {
                ImGui.SetCursorScreenPos(origin);
                ImGui.SetWindowFontScale(1f);
            }
        }

        private static void DrawEncounter(ParseSnapshot snap, float width, float height)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var text = Clock(snap.Duration);
            if (snap.LastPull)
                text = "Last pull  " + text;
            if (!string.IsNullOrWhiteSpace(snap.ZoneName))
                text = text + "  " + snap.ZoneName.Trim();

            var textY = origin.Y + Math.Max(0f, (height - fontSize) * 0.5f);
            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X + 8f, textY), muted, Fit(text, width - 16f, fontSize));
        }

        private static void DrawHeader(float width, float height, float top, float dpsCol, float parseCol, float shareCol, MeterColumns columns, bool lastPull)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.68f, 0.72f, 0.78f, 1f));
            var textY = origin.Y + top + Math.Max(0f, (height - fontSize) * 0.5f);
            var dpsLabel = "rDPS";
            var parseLabel = "Parse";
            var dpsX = origin.X + 8f + dpsCol - ImGui.CalcTextSize(dpsLabel).X;
            var parseX = origin.X + width - 8f - ImGui.CalcTextSize(parseLabel).X;
            drawList.AddText(font, fontSize, new Vector2(dpsX, textY), muted, dpsLabel);
            drawList.AddText(font, fontSize, new Vector2(parseX, textY), muted, parseLabel);
            if (columns.DamagePercent)
            {
                var shareLabel = "Dmg";
                var shareX = parseX - 6f - shareCol;
                drawList.AddText(font, fontSize, new Vector2(shareX + shareCol - ImGui.CalcTextSize(shareLabel).X, textY), muted, shareLabel);
            }

            if (!lastPull)
                return;

            var marker = "Last pull";
            var markerX = origin.X + 8f + dpsCol + 8f;
            drawList.AddText(font, fontSize, new Vector2(markerX, textY), muted, marker);
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

        private static bool DrawRow(MeterRow row, float y, float width, float rowH, float dpsCol, float parseCol, float shareCol, float deathCol, int raw, float nameSize, ITextureProvider textures, MeterColumns columns)
        {
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var parse = HoldParse(row.Name, raw, row.TimeDead >= 1d);
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
            var deathColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.45f, 0.42f, 1f));

            drawList.AddRectFilled(min, max, track, 2f);
            if (parse > 0)
                drawList.AddRectFilled(min, new Vector2(min.X + (width * (parse / 100f)), max.Y), fill, 2f);

            var textY = min.Y + Math.Max(0f, (rowH - fontSize) * 0.5f);
            var nameY = min.Y + Math.Max(0f, (rowH - nameSize) * 0.5f);
            var dpsText = FormatNumber(row.Dps, columns.Abbreviate);
            var dpsWidth = ImGui.CalcTextSize(dpsText).X;
            var dpsX = min.X + 8f + Math.Max(0f, dpsCol - dpsWidth);
            var icon = Math.Max(12f, rowH - 1f);
            var iconX = min.X + 8f + dpsCol + 6f;
            var nameX = iconX + icon + 4f;
            DrawJobIcon(drawList, textures, row.JobId, iconX, min.Y + Math.Max(0f, (rowH - icon) * 0.5f), icon);
            var parseText = parse.ToString(CultureInfo.InvariantCulture);
            var parseX = max.X - 8f - parseCol;
            var shareX = parseX - (columns.DamagePercent ? shareCol + 6f : 0f);
            var deathX = shareX - (columns.Deaths ? deathCol + 4f : 0f);
            var nameWidth = Math.Max(16f, deathX - nameX - 6f);

            drawList.AddText(font, fontSize, new Vector2(dpsX, textY), numberColor, dpsText);
            drawList.AddText(font, nameSize, new Vector2(nameX, nameY), nameColor, Fit(FirstName(row.Name), nameWidth, nameSize));
            if (columns.Deaths && row.Deaths > 0)
            {
                var deaths = row.Deaths.ToString(CultureInfo.InvariantCulture);
                drawList.AddText(font, fontSize, new Vector2(deathX + deathCol - ImGui.CalcTextSize(deaths).X, textY), deathColor, deaths);
            }

            if (columns.DamagePercent)
            {
                var share = ShareText(row.Share);
                drawList.AddText(font, fontSize, new Vector2(shareX + shareCol - ImGui.CalcTextSize(share).X, textY), numberColor, share);
            }

            drawList.AddText(font, fontSize, new Vector2(parseX + parseCol - ImGui.CalcTextSize(parseText).X, textY), packed, parseText);

            if (!columns.HitDetails)
                return false;

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"meter-row-{row.Name}-{row.JobId}", new Vector2(Math.Max(1f, width), Math.Max(1f, rowH)));
            return ImGui.IsItemClicked(ImGuiMouseButton.Left);
        }

        private static void DrawDetail(MeterRow row, float y, float width, float fontSize, bool abbreviate)
        {
            var font = ImGui.GetFont();
            var origin = ImGui.GetCursorScreenPos();
            var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(0.78f, 0.82f, 0.88f, 1f));
            var crit = Rate(row.Crits, row.Hits);
            var direct = Rate(row.Directs, row.Hits);
            var text = $"Crit {crit.ToString(CultureInfo.InvariantCulture)}%    Direct {direct.ToString(CultureInfo.InvariantCulture)}%";
            if (row.MaxHit > 0)
            {
                var skill = string.IsNullOrWhiteSpace(row.MaxHitName) ? "Hit" : row.MaxHitName.Trim();
                text += $"    {skill} {FormatNumber(row.MaxHit, abbreviate)}";
            }

            ImGui.GetWindowDrawList().AddText(font, fontSize, new Vector2(origin.X + 8f, origin.Y + y), muted, Fit(text, width - 16f, fontSize));
        }

        private static void DrawJobIcon(ImDrawListPtr drawList, ITextureProvider textures, int jobId, float x, float y, float size)
        {
            if (jobId <= 0)
                return;

            var lookup = new GameIconLookup((uint)(62100 + jobId));
            var handle = textures.GetFromGameIcon(lookup).GetWrapOrEmpty().Handle;
            drawList.AddImageRounded(
                handle,
                new Vector2(x, y),
                new Vector2(x + size, y + size),
                Vector2.Zero,
                Vector2.One,
                0xFFFFFFFFu,
                2f);
        }

        private static float ClampScale(float meterScale)
        {
            return Math.Clamp(meterScale, 0.8f, 2.5f);
        }

        private static int RoleParse(MeterRow row, MeterRow[] rows, double duration)
        {
            if (duration <= 0 || row.Dps <= 0)
                return 0;

            var role = FightRole(row.JobId);
            var cleanBest = 0d;
            foreach (var other in rows)
            {
                if (FightRole(other.JobId) != role || other.TimeDead >= 1d)
                    continue;
                if (other.Dps > cleanBest)
                    cleanBest = other.Dps;
            }

            var pace = row.AliveSeconds > 0.5 ? row.Damage / row.AliveSeconds : row.Dps;
            var reference = row.TimeDead >= 1d ? Math.Max(cleanBest, pace) : Math.Max(cleanBest, row.Dps);
            if (reference <= 0)
                return 0;

            return (int)Math.Clamp(Math.Floor(row.Dps / reference * 100d), 0, 100);
        }

        private static int HoldParse(string name, int raw, bool steadyDrop)
        {
            var key = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            seenParse.Add(key);
            if (!heldParse.TryGetValue(key, out var held))
                held = raw;
            else if (raw > held || steadyDrop || held - raw >= 2)
                held = raw;

            heldParse[key] = held;
            return held;
        }

        private static void ForgetUnseen()
        {
            if (heldParse.Count != seenParse.Count)
            {
                List<string>? drop = null;
                foreach (var key in heldParse.Keys)
                {
                    if (!seenParse.Contains(key))
                        (drop ??= new List<string>()).Add(key);
                }

                if (drop != null)
                {
                    foreach (var key in drop)
                        heldParse.Remove(key);
                }
            }

            seenParse.Clear();
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

        private static int Rate(int part, int total)
        {
            if (total <= 0 || part <= 0)
                return 0;

            return (int)Math.Clamp(Math.Round(part * 100d / total, MidpointRounding.AwayFromZero), 0, 100);
        }

        private static string ShareText(float share)
        {
            return Math.Round(share, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string Clock(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

            var seconds = (int)duration.TotalSeconds;
            var minutes = seconds / 60;
            var hours = minutes / 60;
            seconds %= 60;
            minutes %= 60;
            if (hours > 0)
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds);

            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, seconds);
        }

        private static string FormatNumber(double value, bool abbreviate)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (!abbreviate || rounded < 1000d)
                return rounded.ToString("0", CultureInfo.InvariantCulture);

            return Math.Round(rounded / 1000d, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "k";
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
