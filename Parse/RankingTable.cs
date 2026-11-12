using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BardSongTracker.Parse
{
    public sealed class RankingTable
    {
        public const int CurveVersion = 3;

        private readonly List<Entry> entries = new();

        public string? Error { get; private set; }

        public int Count => entries.Count;

        public static RankingTable Load(string? path)
        {
            var table = new RankingTable();
            if (string.IsNullOrWhiteSpace(path))
                return table;

            try
            {
                if (!File.Exists(path))
                {
                    table.Error = "Ranking file not found.";
                    return table;
                }

                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<FileModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (file?.Encounters == null)
                {
                    table.Error = "Ranking file has no encounters.";
                    return table;
                }

                foreach (var encounter in file.Encounters)
                {
                    if (encounter.CurveVersion < CurveVersion)
                        continue;

                    table.entries.Add(Entry.From(encounter));
                }
            }
            catch (Exception ex)
            {
                table.Error = ex.Message;
            }

            return table;
        }

        public void Save(string path)
        {
            var model = new FileModel { Encounters = new List<EncounterModel>() };
            lock (entries)
            {
                foreach (var entry in entries)
                {
                    var points = new List<PointModel>();
                    foreach (var point in entry.Points)
                    {
                        points.Add(new PointModel
                        {
                            Percentile = point.Percentile,
                            Dps = point.Dps,
                        });
                    }

                    model.Encounters.Add(new EncounterModel
                    {
                        ZoneName = entry.ZoneName,
                        Boss = entry.Boss,
                        JobId = entry.JobId,
                        CurveVersion = CurveVersion,
                        FetchedAt = entry.FetchedAt,
                        Percentiles = points,
                    });
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Replace(List<Entry> next)
        {
            lock (entries)
                entries.Clear();

            if (next == null)
                return;

            lock (entries)
                entries.AddRange(next);
        }

        public void Upsert(Entry entry)
        {
            if (entry == null)
                return;

            lock (entries)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var current = entries[i];
                    if (current.JobId == entry.JobId
                        && string.Equals(current.ZoneName, entry.ZoneName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(current.Boss, entry.Boss, StringComparison.OrdinalIgnoreCase))
                    {
                        entries[i] = entry;
                        return;
                    }
                }

                entries.Add(entry);
            }
        }

        public bool TryFresh(string zoneName, string bossName, int jobId, TimeSpan maxAge, out Entry entry)
        {
            if (!TryMatch("", zoneName, bossName, jobId, out entry))
                return false;

            return entry.FetchedAt > DateTimeOffset.MinValue && DateTimeOffset.UtcNow - entry.FetchedAt <= maxAge;
        }

        public bool TryDisplay(string zoneId, string zoneName, string bossName, int jobId, out Entry entry)
        {
            if (TryMatch(zoneId, zoneName, bossName, jobId, out entry))
                return true;

            Entry? newest = null;
            lock (entries)
            {
                foreach (var candidate in entries)
                {
                    if (candidate.CurveVersion < CurveVersion)
                        continue;
                    if (jobId <= 0 || candidate.JobId != jobId)
                        continue;
                    if (candidate.Points.Count == 0)
                        continue;
                    if (!Same(candidate.ZoneName, zoneName))
                        continue;
                    if (newest == null || candidate.FetchedAt > newest.FetchedAt)
                        newest = candidate;
                }
            }

            entry = newest!;
            return newest != null;
        }

        public bool TryMatch(string zoneId, string zoneName, string bossName, int jobId, out Entry entry)
        {
            Entry? best = null;
            var bestScore = 0;
            lock (entries)
            {
                foreach (var candidate in entries)
                {
                    if (candidate.CurveVersion < CurveVersion)
                        continue;
                    if (candidate.JobId != 0 && candidate.JobId != jobId)
                        continue;
                    if (candidate.Points.Count == 0)
                        continue;

                    var score = 0;
                    if (candidate.ZoneId != 0 && candidate.ZoneId == ParseZoneId(zoneId))
                        score += 4;
                    else if (Same(candidate.ZoneName, zoneName))
                        score += 4;
                    else if (Overlaps(candidate.ZoneName, zoneName))
                        score += 2;
                    else if (candidate.ZoneName.Length > 0 || candidate.ZoneId != 0)
                        continue;

                    if (candidate.Boss.Length > 0)
                    {
                        if (Same(candidate.Boss, bossName))
                            score += 4;
                        else if (Overlaps(candidate.Boss, bossName))
                            score += 2;
                        else
                            continue;
                    }
                    else if (bossName.Length > 0)
                    {
                        continue;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            entry = best!;
            return best != null;
        }

        private static bool Same(string left, string right)
        {
            return left.Length > 0
                && right.Length > 0
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Overlaps(string left, string right)
        {
            var a = Letters(left);
            var b = Letters(right);
            if (a.Length < 6 || b.Length < 6)
                return false;

            return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
        }

        private static string Letters(string text)
        {
            var buffer = new char[text.Length];
            var count = 0;
            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                    buffer[count++] = char.ToLowerInvariant(character);
            }

            return new string(buffer, 0, count);
        }

        public static int ParseZoneId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[2..];

            return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        public sealed class Entry
        {
            public int ZoneId { get; init; }
            public string ZoneName { get; init; } = "";
            public string Boss { get; init; } = "";
            public int JobId { get; init; }
            public int CurveVersion { get; init; }
            public DateTimeOffset FetchedAt { get; init; }
            public List<PercentileScale.Point> Points { get; init; } = new();

            public static Entry From(EncounterModel model)
            {
                var points = new List<PercentileScale.Point>();
                if (model.Percentiles != null)
                {
                    foreach (var point in model.Percentiles)
                        points.Add(new PercentileScale.Point(point.Percentile, point.Dps));
                }

                points.Sort((a, b) => a.Dps.CompareTo(b.Dps));
                return new Entry
                {
                    ZoneId = ParseZoneId(model.ZoneId ?? ""),
                    ZoneName = model.ZoneName?.Trim() ?? "",
                    Boss = model.Boss?.Trim() ?? model.Encounter?.Trim() ?? "",
                    JobId = model.JobId != 0 ? model.JobId : JobIdFromName(model.Job),
                    CurveVersion = model.CurveVersion,
                    FetchedAt = model.FetchedAt,
                    Points = points,
                };
            }

            private static int JobIdFromName(string? job)
            {
                if (string.IsNullOrWhiteSpace(job))
                    return 0;

                if (int.TryParse(job, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    return id;

                foreach (var known in DotCatalog.Jobs)
                {
                    if (string.Equals(known.Abbreviation, job, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(known.Name, job, StringComparison.OrdinalIgnoreCase))
                    {
                        return (int)known.Id;
                    }
                }

                return 0;
            }
        }

        private sealed class FileModel
        {
            public List<EncounterModel>? Encounters { get; set; }
        }

        public sealed class EncounterModel
        {
            public string? ZoneId { get; set; }
            public string? ZoneName { get; set; }
            public string? Boss { get; set; }
            public string? Encounter { get; set; }
            public string? Job { get; set; }
            public int JobId { get; set; }
            public int CurveVersion { get; set; }
            public DateTimeOffset FetchedAt { get; set; }
            public List<PointModel>? Percentiles { get; set; }
        }

        public sealed class PointModel
        {
            public int Percentile { get; set; }
            public double Dps { get; set; }
        }
    }
}
