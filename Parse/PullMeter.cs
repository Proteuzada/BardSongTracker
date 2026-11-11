using System;
using System.Collections.Generic;
using System.Globalization;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Running personal damage for the current pull. DPS is damage divided by the
    /// length of the pull, which is the duration FFLogs uses before it turns a
    /// score into a percentile.
    /// </summary>
    public sealed class PullMeter
    {
        private string playerId = "";
        private string playerName = "";
        private int jobId;
        private string zoneId = "";
        private string zoneName = "";
        private bool inPull;
        private DateTimeOffset pullStart;
        private DateTimeOffset lastEvent;
        private long damage;
        private TimeSpan lockedDuration;

        public void ResetPull()
        {
            inPull = false;
            damage = 0;
            lockedDuration = TimeSpan.Zero;
        }

        public void Apply(string line, DateTimeOffset clock)
        {
            if (string.IsNullOrEmpty(line) || line.Length < 3 || line[2] != '|')
                return;

            var type = line[..2];
            var fields = line.Split('|');
            switch (type)
            {
                case "01":
                    ApplyZone(fields);
                    break;
                case "02":
                    ApplyPlayer(fields);
                    break;
                case "03":
                    ApplyCombatant(fields);
                    break;
                case "21":
                case "22":
                    ApplyAbility(fields, clock);
                    break;
            }
        }

        public void Advance(DateTimeOffset clock, double idleSeconds)
        {
            if (!inPull)
                return;

            if ((clock - lastEvent).TotalSeconds < idleSeconds)
                return;

            lockedDuration = lastEvent - pullStart;
            inPull = false;
        }

        public ParseSnapshot Snapshot(DateTimeOffset clock, RankingTable? rankings, string? logFile, string? error)
        {
            var duration = TimeSpan.Zero;
            if (inPull)
                duration = clock > pullStart ? clock - pullStart : TimeSpan.Zero;
            else if (damage > 0)
                duration = lockedDuration;

            var seconds = Math.Max(duration.TotalSeconds, 0);
            var dps = seconds >= 1 ? damage / seconds : 0;
            int? percentile = null;
            var ladder = Array.Empty<PercentileScale.Point>();
            var note = "";

            if (!string.IsNullOrEmpty(error))
            {
                note = error;
            }
            else if (rankings != null && rankings.Error != null && rankings.Count == 0)
            {
                note = rankings.Error;
            }
            else if (damage > 0 && rankings != null && rankings.TryMatch(zoneId, zoneName, jobId, out var entry) && entry.Points.Count > 0)
            {
                percentile = PercentileScale.Interpolate(dps, entry.Points);
                ladder = CopyLadder(entry.Points);
                note = "Estimated from the ranking table.";
            }
            else if (damage > 0)
            {
                note = "No percentile table for this zone and job.";
            }
            else if (string.IsNullOrEmpty(logFile))
            {
                note = "Waiting for an ACT network log.";
            }

            return new ParseSnapshot(
                !string.IsNullOrEmpty(logFile),
                logFile,
                zoneId,
                zoneName,
                playerName,
                jobId,
                inPull,
                duration,
                damage,
                dps,
                percentile,
                note,
                ladder);
        }

        private static PercentileScale.Point[] CopyLadder(List<PercentileScale.Point> points)
        {
            var copy = new PercentileScale.Point[points.Count];
            for (var i = 0; i < points.Count; i++)
                copy[i] = points[i];

            Array.Sort(copy, static (left, right) => left.Percentile.CompareTo(right.Percentile));
            return copy;
        }

        private void ApplyZone(string[] fields)
        {
            if (fields.Length < 4)
                return;

            zoneId = fields[2];
            zoneName = fields[3];
            ResetPull();
        }

        private void ApplyPlayer(string[] fields)
        {
            if (fields.Length < 4)
                return;

            playerId = fields[2];
            playerName = fields[3];
        }

        private void ApplyCombatant(string[] fields)
        {
            if (fields.Length < 5 || playerId.Length == 0)
                return;

            if (!string.Equals(fields[2], playerId, StringComparison.OrdinalIgnoreCase))
                return;

            if (int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var job))
                jobId = job;
        }

        private void ApplyAbility(string[] fields, DateTimeOffset clock)
        {
            if (playerId.Length == 0 || fields.Length < 10)
                return;

            var sourceId = fields[2];
            var targetId = fields[6];
            var ownerIndex = ActFields.AbilityIndex("ownerId");
            var ownerId = ownerIndex >= 0 && ownerIndex < fields.Length ? fields[ownerIndex] : "";
            var mine = SameActor(sourceId, playerId) || (ownerId.Length > 0 && SameActor(ownerId, playerId));
            if (!mine || IsPlayerOrEmpty(targetId) || IsLimitBreak(sourceId))
                return;

            var when = clock;
            if (fields.Length > 1
                && DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamped))
            {
                when = stamped;
            }

            long hit = 0;
            for (var pair = 0; pair < 8; pair++)
            {
                var flagsIndex = 8 + (pair * 2);
                var damageIndex = flagsIndex + 1;
                if (damageIndex >= fields.Length)
                    break;

                if (!DamageCodec.TryHex(fields[flagsIndex], out var flags) || !DamageCodec.IsDamage(flags))
                    continue;
                if (!DamageCodec.TryHex(fields[damageIndex], out var raw))
                    continue;

                var amount = DamageCodec.Amount(raw);
                if (amount > 0)
                    hit += amount;
            }

            if (hit <= 0)
                return;

            if (!inPull)
            {
                inPull = true;
                pullStart = when;
                damage = 0;
                lockedDuration = TimeSpan.Zero;
            }

            damage += hit;
            lastEvent = when;
        }

        private static bool SameActor(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlayerOrEmpty(string id)
        {
            if (id.Length < 2)
                return true;

            return id.StartsWith("10", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("E", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLimitBreak(string sourceId)
        {
            return sourceId.StartsWith("E", StringComparison.OrdinalIgnoreCase);
        }
    }

    public readonly record struct ParseSnapshot(
        bool Watching,
        string? LogFile,
        string ZoneId,
        string ZoneName,
        string PlayerName,
        int JobId,
        bool InPull,
        TimeSpan Duration,
        long Damage,
        double Dps,
        int? Percentile,
        string Note,
        PercentileScale.Point[] Ladder);
}
