using System;
using System.Collections.Generic;
using System.Globalization;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Damage for every player in the current pull, read from the ACT network log.
    /// Pet damage is added to the owner. The share is that player's part of the pull.
    /// </summary>
    public sealed class PullMeter
    {
        private readonly Dictionary<string, Actor> actors = new(StringComparer.OrdinalIgnoreCase);
        private string playerId = "";
        private string zoneName = "";
        private bool inPull;
        private DateTimeOffset pullStart;
        private DateTimeOffset lastEvent;
        private TimeSpan lockedDuration;

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

        public ParseSnapshot Snapshot(DateTimeOffset clock, string? logFile, string? error)
        {
            var duration = TimeSpan.Zero;
            if (inPull)
                duration = clock > pullStart ? clock - pullStart : TimeSpan.Zero;
            else if (HasDamage())
                duration = lockedDuration;

            var seconds = Math.Max(duration.TotalSeconds, 0);
            long total = 0;
            foreach (var actor in actors.Values)
            {
                if (actor.Damage > 0)
                    total += actor.Damage;
            }

            var rows = new List<MeterRow>();
            foreach (var actor in actors.Values)
            {
                if (actor.Damage <= 0)
                    continue;

                var dps = seconds >= 1 ? actor.Damage / seconds : 0;
                var share = total > 0 ? (float)(actor.Damage * 100d / total) : 0f;
                rows.Add(new MeterRow(
                    actor.Name.Length == 0 ? "Player" : actor.Name,
                    actor.JobId,
                    actor.Damage,
                    dps,
                    share,
                    SameActor(actor.Id, playerId)));
            }

            rows.Sort(static (left, right) => right.Damage.CompareTo(left.Damage));

            var note = "";
            if (!string.IsNullOrEmpty(error))
                note = error;
            else if (rows.Count == 0 && string.IsNullOrEmpty(logFile))
                note = "Waiting for an ACT network log.";
            else if (rows.Count == 0)
                note = "Waiting for a pull.";

            return new ParseSnapshot(
                !string.IsNullOrEmpty(logFile),
                logFile,
                zoneName,
                inPull,
                duration,
                rows.ToArray(),
                note);
        }

        private bool HasDamage()
        {
            foreach (var actor in actors.Values)
            {
                if (actor.Damage > 0)
                    return true;
            }

            return false;
        }

        private void ApplyZone(string[] fields)
        {
            if (fields.Length < 4)
                return;

            zoneName = fields[3];
            ClearDamage();
            inPull = false;
            lockedDuration = TimeSpan.Zero;
        }

        private void ApplyPlayer(string[] fields)
        {
            if (fields.Length < 4)
                return;

            playerId = fields[2];
            var actor = GetOrAdd(playerId);
            if (fields[3].Length > 0)
                actor.Name = fields[3];
        }

        private void ApplyCombatant(string[] fields)
        {
            if (fields.Length < 5 || !IsPlayer(fields[2]))
                return;

            var actor = GetOrAdd(fields[2]);
            if (fields[3].Length > 0)
                actor.Name = fields[3];

            if (int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var job) && job > 0)
                actor.JobId = job;
        }

        private void ApplyAbility(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 10)
                return;

            var sourceId = fields[2];
            var targetId = fields[6];
            var ownerIndex = ActFields.AbilityIndex("ownerId");
            var ownerId = ownerIndex >= 0 && ownerIndex < fields.Length ? fields[ownerIndex] : "";
            if (IsLimitBreak(sourceId) || IsPlayerOrEmpty(targetId))
                return;

            var creditId = IsPlayer(sourceId) ? sourceId : IsPlayer(ownerId) ? ownerId : "";
            if (creditId.Length == 0)
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
                ClearDamage();
                inPull = true;
                pullStart = when;
                lockedDuration = TimeSpan.Zero;
            }

            var credit = GetOrAdd(creditId);
            if (credit.Name.Length == 0)
                credit.Name = CreditName(fields, creditId, sourceId);

            credit.Damage += hit;
            lastEvent = when;
        }

        private static string CreditName(string[] fields, string creditId, string sourceId)
        {
            if (SameActor(creditId, sourceId) && fields.Length > 3)
                return fields[3];

            var ownerNameIndex = ActFields.AbilityIndex("ownerName");
            if (ownerNameIndex >= 0 && ownerNameIndex < fields.Length)
                return fields[ownerNameIndex];

            return "";
        }

        private Actor GetOrAdd(string id)
        {
            if (!actors.TryGetValue(id, out var actor))
            {
                actor = new Actor { Id = id };
                actors[id] = actor;
            }

            return actor;
        }

        private void ClearDamage()
        {
            foreach (var actor in actors.Values)
                actor.Damage = 0;
        }

        private static bool SameActor(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlayer(string id)
        {
            return id.Length >= 4 && id.StartsWith("10", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlayerOrEmpty(string id)
        {
            if (id.Length < 2)
                return true;

            return IsPlayer(id) || id.StartsWith("E", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLimitBreak(string sourceId)
        {
            return sourceId.StartsWith("E", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class Actor
        {
            public string Id = "";
            public string Name = "";
            public int JobId;
            public long Damage;
        }
    }

    public readonly record struct MeterRow(
        string Name,
        int JobId,
        long Damage,
        double Dps,
        float Share,
        bool You);

    public readonly record struct ParseSnapshot(
        bool Watching,
        string? LogFile,
        string ZoneName,
        bool InPull,
        TimeSpan Duration,
        MeterRow[] Rows,
        string Note);
}
