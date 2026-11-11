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
        private readonly HashSet<string> damagedEnemies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> killedEnemies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> aggroDropped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> hpRestored = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> vanished = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> enemyMaxHp = new(StringComparer.OrdinalIgnoreCase);
        private bool playerDied;
        private string playerId = "";
        private string zoneName = "";
        private bool inPull;
        private DateTimeOffset pullStart;
        private DateTimeOffset lastEvent;
        private TimeSpan lockedDuration;

        public void Apply(string line, DateTimeOffset clock)
        {
            if (string.IsNullOrEmpty(line))
                return;

            var split = line.IndexOf('|');
            if (split < 2 || split > 3)
                return;

            var type = line[..split];
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
                case "04":
                    ApplyRemove(fields);
                    break;
                case "12":
                    ApplyLocalJob(fields);
                    break;
                case "21":
                case "22":
                    ApplyAbility(fields, clock);
                    break;
                case "25":
                    ApplyDeath(fields);
                    break;
                case "33":
                    ApplyDirector(fields);
                    break;
                case "34":
                    ApplyNameToggle(fields);
                    break;
                case "260":
                    ApplyCombatState(fields);
                    break;
                case "261":
                    RememberJob(fields);
                    ApplyCombatantMemory(fields);
                    break;
            }
        }

        /// <summary>
        /// Learns names and jobs from lines already in the log, without counting damage.
        /// </summary>
        public void Remember(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            var split = line.IndexOf('|');
            if (split < 2 || split > 3)
                return;

            var fields = line.Split('|');
            switch (line[..split])
            {
                case "02":
                    ApplyPlayer(fields);
                    break;
                case "03":
                    ApplyCombatant(fields);
                    break;
                case "12":
                    ApplyLocalJob(fields);
                    break;
                case "261":
                    RememberJob(fields);
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

            var seconds = Math.Max(duration.TotalSeconds, 1);
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

                var dps = actor.Damage / seconds;
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
            ResetPull();
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

            vanished.Remove(fields[2]);
        }

        private void ApplyLocalJob(string[] fields)
        {
            if (fields.Length < 3 || playerId.Length == 0)
                return;

            var job = ParseJob(fields[2]);
            if (job is > 0)
                GetOrAdd(playerId).JobId = job.Value;
        }

        private void ApplyRemove(string[] fields)
        {
            if (fields.Length < 3 || !IsEnemy(fields[2]))
                return;

            MarkVanished(fields[2]);
        }

        private void ApplyNameToggle(string[] fields)
        {
            if (fields.Length < 7 || !IsEnemy(fields[2]))
                return;

            if (fields[6] == "0" || fields[6] == "00")
                MarkVanished(fields[2]);
            else
                vanished.Remove(fields[2]);
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
                && DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamped)
                && (stamped - clock).Duration() < TimeSpan.FromMinutes(5))
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
                if (!HasDamage())
                {
                    ClearEncounter();
                    pullStart = when;
                }

                inPull = true;
                lockedDuration = TimeSpan.Zero;
            }

            var credit = GetOrAdd(creditId);
            if (credit.Name.Length == 0)
                credit.Name = CreditName(fields, creditId, sourceId);

            credit.Damage += hit;
            lastEvent = when;
            if (IsEnemy(targetId))
                damagedEnemies.Add(targetId);
        }

        private void ApplyDeath(string[] fields)
        {
            if (fields.Length < 3)
                return;

            if (IsPlayer(fields[2]))
                playerDied = true;
            else if (IsEnemy(fields[2]))
                killedEnemies.Add(fields[2]);
        }

        private void ApplyDirector(string[] fields)
        {
            if (fields.Length < 4 || !IsWipe(fields[3]))
                return;

            ResetPull();
        }

        private void ApplyCombatState(string[] fields)
        {
            if (fields.Length < 6 || fields[2] != "0" || fields[3] != "0" || fields[5] != "1")
                return;

            if (!HasDamage() || killedEnemies.Count > 0 || vanished.Count > 0 || !LivingEnemiesReset())
                return;

            ResetPull();
        }

        private void ApplyCombatantMemory(string[] fields)
        {
            if (fields.Length < 4 || !IsEnemy(fields[3]))
                return;

            var id = fields[3];
            long? currentHp = null;
            long? maxHp = null;
            int? aggression = null;
            bool? targetable = null;
            for (var i = 4; i + 1 < fields.Length; i += 2)
            {
                if (fields[i].Equals("CurrentHP", StringComparison.OrdinalIgnoreCase))
                    currentHp = ParseLong(fields[i + 1]);
                else if (fields[i].Equals("MaxHP", StringComparison.OrdinalIgnoreCase))
                    maxHp = ParseLong(fields[i + 1]);
                else if (fields[i].Equals("AggressionStatus", StringComparison.OrdinalIgnoreCase))
                    aggression = ParseInt(fields[i + 1]);
                else if (fields[i].Equals("IsTargetable", StringComparison.OrdinalIgnoreCase))
                    targetable = fields[i + 1] != "0" && !fields[i + 1].Equals("false", StringComparison.OrdinalIgnoreCase);
            }

            if (maxHp is > 0)
                enemyMaxHp[id] = maxHp.Value;

            if (targetable == false)
            {
                MarkVanished(id);
                return;
            }

            if (targetable == true)
                vanished.Remove(id);
            if (!fields[2].Equals("Change", StringComparison.OrdinalIgnoreCase))
                return;
            if (!damagedEnemies.Contains(id) || killedEnemies.Contains(id))
                return;

            if (aggression == 0)
                aggroDropped.Add(id);

            if (currentHp is not null && enemyMaxHp.TryGetValue(id, out var known) && known > 0 && currentHp.Value >= known)
                hpRestored.Add(id);
        }

        private void RememberJob(string[] fields)
        {
            if (fields.Length < 6 || !IsPlayer(fields[3]))
                return;

            for (var i = 4; i + 1 < fields.Length; i += 2)
            {
                if (!fields[i].Equals("Job", StringComparison.OrdinalIgnoreCase))
                    continue;

                var job = ParseJob(fields[i + 1]);
                if (job is > 0)
                    GetOrAdd(fields[3]).JobId = job.Value;
                return;
            }
        }

        private bool LivingEnemiesReset()
        {
            var anyLiving = false;
            foreach (var id in damagedEnemies)
            {
                if (killedEnemies.Contains(id) || vanished.Contains(id))
                    continue;

                anyLiving = true;
                if (!aggroDropped.Contains(id) || !hpRestored.Contains(id))
                    return false;
            }

            return anyLiving;
        }

        private void MarkVanished(string id)
        {
            vanished.Add(id);
            aggroDropped.Remove(id);
            hpRestored.Remove(id);
        }

        private static long? ParseLong(string text)
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private static int? ParseInt(string text)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private bool IsWipe(string command)
        {
            if (command.Equals("40000006", StringComparison.OrdinalIgnoreCase)
                || command.Equals("40000010", StringComparison.OrdinalIgnoreCase)
                || command.Equals("40000012", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!playerDied)
                return false;

            return command.Equals("40000005", StringComparison.OrdinalIgnoreCase)
                || command.Equals("4000000F", StringComparison.OrdinalIgnoreCase)
                || command.Equals("40000011", StringComparison.OrdinalIgnoreCase);
        }

        private static int? ParseJob(string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var job) && job > 0)
                return job;

            return null;
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

        private void ResetPull()
        {
            ClearDamage();
            ClearEncounter();
            inPull = false;
            lockedDuration = TimeSpan.Zero;
        }

        private void ClearEncounter()
        {
            damagedEnemies.Clear();
            killedEnemies.Clear();
            aggroDropped.Clear();
            hpRestored.Clear();
            vanished.Clear();
            playerDied = false;
        }

        private static bool IsEnemy(string id)
        {
            return id.Length >= 4 && id.StartsWith("40", StringComparison.OrdinalIgnoreCase);
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
