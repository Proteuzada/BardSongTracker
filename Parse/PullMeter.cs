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
        private bool pullSealed;
        private bool combatIdle;
        private string playerId = "";
        private string zoneName = "";
        private bool inPull;
        private DateTimeOffset pullStart;
        private DateTimeOffset lastEvent;
        private TimeSpan lockedDuration;
        private ParseSnapshot? lastPull;

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
                case "20":
                    NoteCast(fields, clock);
                    break;
                case "21":
                case "22":
                    ApplyAbility(fields, clock);
                    break;
                case "25":
                    ApplyDeath(fields, clock);
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
                    NoteHealth(fields, clock);
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

        public ParseSnapshot Snapshot(DateTimeOffset clock, string? logFile, string? error, bool keepLast)
        {
            var duration = TimeSpan.Zero;
            if (inPull)
                duration = clock > pullStart ? clock - pullStart : TimeSpan.Zero;
            else if (HasDamage())
                duration = lockedDuration;

            var note = "";
            if (!string.IsNullOrEmpty(error))
                note = error;
            else if (!HasDamage() && string.IsNullOrEmpty(logFile))
                note = "Waiting for an ACT network log.";
            else if (!HasDamage())
                note = "Waiting for a pull.";

            var live = Build(duration, note, false, logFile, error);
            if (live.Rows.Length == 0 && keepLast && string.IsNullOrEmpty(error) && lastPull is { Rows.Length: > 0 } saved)
                return saved with { Watching = live.Watching, LogFile = logFile };

            return live;
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

            Archive();
            zoneName = fields[3];
            ResetPull(archive: false);
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

            // AddCombatant writes the job in hexadecimal: 17 is Bard, 19 is Black Mage, 1E is Ninja.
            // PlayerStats and CombatantMemory write the same id in decimal.
            if (TryParseHexJob(fields[4], out var job))
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
            var when = Stamp(fields, clock);
            if (IsPlayer(sourceId) && AnimationLock(fields) > 0.05)
                Revive(sourceId, when);
            if (IsLimitBreak(sourceId) || IsPlayerOrEmpty(targetId))
                return;

            var creditId = IsPlayer(sourceId) ? sourceId : IsPlayer(ownerId) ? ownerId : "";
            if (creditId.Length == 0)
                return;


            long hit = 0;
            var swings = 0;
            var crits = 0;
            var directs = 0;
            long biggest = 0;
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
                if (amount <= 0)
                    continue;

                hit += amount;
                swings++;
                var mark = (flags >> 8) & 0x60;
                if ((mark & 0x20) != 0)
                    crits++;
                if ((mark & 0x40) != 0)
                    directs++;
                if (amount > biggest)
                    biggest = amount;
            }

            if (hit <= 0)
                return;

            // A tick on a corpse stays on the finished kill. The next living enemy,
            // after combat has dropped, starts a new pull. A phase resume keeps this one.
            var lateOnCorpse = pullSealed && killedEnemies.Contains(targetId);
            if (!lateOnCorpse && (!inPull || (pullSealed && combatIdle)))
            {
                var fresh = !HasDamage() || (pullSealed && combatIdle);
                if (fresh)
                {
                    ClearDamage();
                    ClearEncounter();
                    pullStart = when;
                }

                inPull = true;
                pullSealed = false;
                lockedDuration = TimeSpan.Zero;
            }

            var credit = GetOrAdd(creditId);
            if (credit.Name.Length == 0)
                credit.Name = CreditName(fields, creditId, sourceId);

            credit.Damage += hit;
            credit.Hits += swings;
            credit.Crits += crits;
            credit.Directs += directs;
            if (biggest > credit.MaxHit)
            {
                credit.MaxHit = biggest;
                credit.MaxHitName = fields.Length > 5 ? fields[5] : "";
            }

            if (!pullSealed)
                lastEvent = when;
            if (IsEnemy(targetId))
                damagedEnemies.Add(targetId);
        }

        private void ApplyDeath(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 3)
                return;

            if (IsPlayer(fields[2]))
            {
                playerDied = true;
                var actor = GetOrAdd(fields[2]);
                actor.Deaths++;
                if (actor.DeadSince == null)
                    actor.DeadSince = Stamp(fields, clock);
                if (fields.Length > 3 && fields[3].Length > 0)
                    actor.Name = fields[3];
                return;
            }

            if (!IsEnemy(fields[2]))
                return;

            killedEnemies.Add(fields[2]);
            if (!pullSealed && HasDamage() && AllDamagedDead())
                Seal(Stamp(fields, clock));
        }

        private void ApplyDirector(string[] fields)
        {
            if (fields.Length < 4 || !IsWipe(fields[3]))
                return;

            ResetPull();
        }

        private void ApplyCombatState(string[] fields)
        {
            if (fields.Length < 6)
                return;

            if (fields[2] != "0" || fields[3] != "0")
            {
                combatIdle = false;
                return;
            }

            if (fields[5] != "1")
                return;

            combatIdle = true;
            if (pullSealed || !HasDamage() || killedEnemies.Count > 0 || vanished.Count > 0 || !LivingEnemiesReset())
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
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var job) && job is > 0 and <= 63)
                return job;

            return null;
        }

        private static bool TryParseHexJob(string text, out int job)
        {
            if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out job) && job is > 0 and <= 63)
                return true;

            job = 0;
            return false;
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
            {
                actor.Damage = 0;
                actor.Hits = 0;
                actor.Crits = 0;
                actor.Directs = 0;
                actor.Deaths = 0;
                actor.TimeDead = 0;
                actor.DeadSince = null;
                actor.MaxHit = 0;
                actor.MaxHitName = "";
            }
        }

        private void NoteCast(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 3 || !IsPlayer(fields[2]))
                return;

            Revive(fields[2], Stamp(fields, clock));
        }

        private void NoteHealth(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 6 || !IsPlayer(fields[3]))
                return;

            for (var i = 4; i + 1 < fields.Length; i += 2)
            {
                if (!fields[i].Equals("CurrentHP", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ParseLong(fields[i + 1]) is > 0)
                    Revive(fields[3], Stamp(fields, clock));
                return;
            }
        }

        private void Revive(string id, DateTimeOffset when)
        {
            if (!actors.TryGetValue(id, out var actor) || actor.DeadSince is not DateTimeOffset since)
                return;

            var end = when > since ? when : since;
            actor.TimeDead += (end - since).TotalSeconds;
            actor.DeadSince = null;
        }

        private void DownTime(Actor actor, TimeSpan duration, out double alive, out double dead)
        {
            var span = Math.Max(duration.TotalSeconds, 0);
            dead = actor.TimeDead;
            if (actor.DeadSince is DateTimeOffset since)
            {
                var end = pullStart + duration;
                var from = since < pullStart ? pullStart : since;
                if (end > from)
                    dead += (end - from).TotalSeconds;
            }

            if (dead < 0)
                dead = 0;
            if (dead > span)
                dead = span;
            alive = Math.Max(span - dead, 0);
        }

        private static double AnimationLock(string[] fields)
        {
            var index = ActFields.AbilityIndex("animationLockTime");
            if (index < 0 || index >= fields.Length)
                return 0;

            return double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private void ResetPull(bool archive = true)
        {
            if (archive)
                Archive();

            ClearDamage();
            ClearEncounter();
            inPull = false;
            pullSealed = false;
            combatIdle = false;
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
            pullSealed = false;
            combatIdle = false;
        }

        private void Archive()
        {
            if (!HasDamage())
                return;

            var duration = inPull
                ? (lastEvent > pullStart ? lastEvent - pullStart : TimeSpan.Zero)
                : lockedDuration;
            lastPull = Build(duration, "Last pull", true, null, null);
        }

        private void Seal(DateTimeOffset when)
        {
            pullSealed = true;
            inPull = false;
            var end = when > pullStart ? when : lastEvent;
            lockedDuration = end > pullStart ? end - pullStart : TimeSpan.Zero;
        }

        private bool AllDamagedDead()
        {
            if (damagedEnemies.Count == 0)
                return false;

            foreach (var id in damagedEnemies)
            {
                if (!killedEnemies.Contains(id))
                    return false;
            }

            return true;
        }

        private ParseSnapshot Build(TimeSpan duration, string note, bool lastPull, string? logFile, string? error)
        {
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
                DownTime(actor, duration, out var alive, out var dead);
                rows.Add(new MeterRow(
                    actor.Name.Length == 0 ? "Player" : actor.Name,
                    actor.JobId,
                    actor.Damage,
                    dps,
                    share,
                    SameActor(actor.Id, playerId),
                    actor.Deaths,
                    actor.Hits,
                    actor.Crits,
                    actor.Directs,
                    actor.MaxHit,
                    actor.MaxHitName,
                    alive,
                    dead));
            }

            rows.Sort(static (left, right) => right.Damage.CompareTo(left.Damage));
            if (!string.IsNullOrEmpty(error))
                note = error;

            return new ParseSnapshot(
                !string.IsNullOrEmpty(logFile),
                logFile,
                zoneName,
                inPull && !lastPull,
                duration,
                rows.ToArray(),
                note,
                lastPull);
        }

        private static DateTimeOffset Stamp(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length > 1
                && DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamped)
                && (stamped - clock).Duration() < TimeSpan.FromMinutes(5))
                return stamped;

            return clock;
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
            public int Hits;
            public int Crits;
            public int Directs;
            public int Deaths;
            public double TimeDead;
            public DateTimeOffset? DeadSince;
            public long MaxHit;
            public string MaxHitName = "";
        }
    }

    public readonly record struct MeterColumns(bool DamagePercent, bool Deaths, bool Encounter, bool HitDetails, bool Abbreviate);

    public readonly record struct MeterRow(
        string Name,
        int JobId,
        long Damage,
        double Dps,
        float Share,
        bool You,
        int Deaths,
        int Hits,
        int Crits,
        int Directs,
        long MaxHit,
        string MaxHitName,
        double AliveSeconds,
        double TimeDead);

    public readonly record struct ParseSnapshot(
        bool Watching,
        string? LogFile,
        string ZoneName,
        bool InPull,
        TimeSpan Duration,
        MeterRow[] Rows,
        string Note,
        bool LastPull);
}
