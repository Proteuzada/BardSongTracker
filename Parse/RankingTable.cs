using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BardSongTracker.Parse
{
    public sealed class RankingTable
    {
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
                    table.entries.Add(Entry.From(encounter));
            }
            catch (Exception ex)
            {
                table.Error = ex.Message;
            }

            return table;
        }

        public bool TryMatch(string zoneId, string zoneName, int jobId, out Entry entry)
        {
            Entry? byName = null;
            foreach (var candidate in entries)
            {
                if (candidate.JobId != 0 && candidate.JobId != jobId)
                    continue;

                if (candidate.ZoneId != 0 && candidate.ZoneId == ParseZoneId(zoneId))
                {
                    entry = candidate;
                    return true;
                }

                if (byName == null
                    && candidate.ZoneName.Length > 0
                    && zoneName.Length > 0
                    && string.Equals(candidate.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase))
                {
                    byName = candidate;
                }
            }

            if (byName != null)
            {
                entry = byName;
                return true;
            }

            entry = null!;
            return false;
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
            public int JobId { get; init; }
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
                    JobId = model.JobId != 0 ? model.JobId : JobIdFromName(model.Job),
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
            public string? Job { get; set; }
            public int JobId { get; set; }
            public List<PointModel>? Percentiles { get; set; }
        }

        public sealed class PointModel
        {
            public int Percentile { get; set; }
            public double Dps { get; set; }
        }
    }
}
