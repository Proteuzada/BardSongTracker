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
        private readonly Dictionary<string, long> damageToEnemy = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> enemyNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> phaseDown = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> phaseCreditAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> phaseDamageAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PhaseSlice> phases = new();
        private readonly Dictionary<string, int> shownParse = new(StringComparer.OrdinalIgnoreCase);
        private int phaseIndex;
        private bool phaseOpen;
        private bool phaseLatched;
        private DateTimeOffset phaseStart;
        private DateTimeOffset parseDue;
        private readonly List<BuffWindow> buffs = new();
        private readonly List<RdpsMath.External> externalScratch = new();
        private RankingTable? rankings;
        private string playerId = "";
        private string zoneName = "";
        private string zoneId = "";
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
                    ApplyRemove(fields, clock);
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
                case "26":
                    ApplyStatus(fields, clock);
                    break;
                case "30":
                    RemoveStatus(fields, clock);
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
                case "01":
                    ApplyZone(fields);
                    break;
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

        public void UseRankings(RankingTable? table)
        {
            rankings = table;
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
                return Rescore(saved) with { Watching = live.Watching, LogFile = logFile };

            return live;
        }

        private ParseSnapshot Rescore(ParseSnapshot snap)
        {
            if (rankings == null || snap.Rows.Length == 0)
                return snap;
            if (PhasePlan.Ladder(snap.ZoneName, 0).Length > 0)
                return snap;

            var rows = new MeterRow[snap.Rows.Length];
            for (var i = 0; i < snap.Rows.Length; i++)
            {
                var row = snap.Rows[i];
                var parse = row.Parse;
                if (rankings.TryDisplay(zoneId, snap.ZoneName, snap.BossName, row.JobId, out var scale))
                    parse = PercentileScale.Interpolate(row.Dps, scale.Points);

                rows[i] = row with { Parse = parse };
            }

            return snap with { Rows = rows };
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
            zoneId = fields.Length > 2 ? fields[2] : "";
            zoneName = fields[3];
            ResetPull(archive: false);
            buffs.Clear();
            enemyNames.Clear();
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
            if (fields.Length < 5)
                return;

            if (!IsPlayer(fields[2]))
            {
                if (IsEnemy(fields[2]) && fields[3].Length > 0)
                    enemyNames[fields[2]] = fields[3];
                return;
            }

            var actor = GetOrAdd(fields[2]);
            if (fields[3].Length > 0)
                actor.Name = fields[3];

            // AddCombatant writes the job in hexadecimal: 17 is Bard, 19 is Black Mage, 1E is Ninja.
            // PlayerStats (line 12) and CombatantMemory (line 261) write that same id in decimal.
            if (TryParseHexJob(fields[4], out var job))
                actor.JobId = job;
            if (fields.Length > 5 && ParseInt(fields[5]) is int level and > 0 and <= 100)
                actor.Level = level;

            vanished.Remove(fields[2]);
        }

        private void ApplyLocalJob(string[] fields)
        {
            if (fields.Length < 3 || playerId.Length == 0)
                return;

            var actor = GetOrAdd(playerId);
            var job = ParseJob(fields[2]);
            if (job is > 0)
                actor.JobId = job.Value;
            if (fields.Length > 11 && ParseInt(fields[10]) is int direct)
                actor.DirectStat = direct;
            if (fields.Length > 11 && ParseInt(fields[11]) is int crit)
                actor.CritStat = crit;
            actor.HasStats = actor.CritStat > 0 || actor.DirectStat > 0;
        }

        private void ApplyRemove(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 3 || !IsEnemy(fields[2]))
                return;

            var name = fields.Length > 3 ? fields[3] : "";
            if (name.Length == 0 && enemyNames.TryGetValue(fields[2], out var known))
                name = known;
            if (name.Length > 0)
                enemyNames[fields[2]] = name;

            var next = PhasePlan.Down(zoneName, phaseIndex, name, phaseDown);
            if (next > phaseIndex)
            {
                CutTo(next, Stamp(fields, clock));
                return;
            }

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
            var phaseBefore = phaseIndex;
            NoticePhase(sourceId, fields.Length > 3 ? fields[3] : "", targetId, fields.Length > 7 ? fields[7] : "", when);
            var phaseAdvanced = phaseIndex > phaseBefore;
            if (IsLimitBreak(sourceId) || IsPlayerOrEmpty(targetId))
                return;

            var creditId = IsPlayer(sourceId) ? sourceId : IsPlayer(ownerId) ? ownerId : "";
            if (creditId.Length == 0)
                return;


            long hit = 0;
            var swingCount = 0;
            var crits = 0;
            var directs = 0;
            long biggest = 0;
            Span<long> swingAmount = stackalloc long[8];
            Span<bool> swingCrit = stackalloc bool[8];
            Span<bool> swingDirect = stackalloc bool[8];
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

                var crit = ((flags >> 8) & 0x20) != 0;
                var direct = ((flags >> 8) & 0x40) != 0;
                swingAmount[swingCount] = amount;
                swingCrit[swingCount] = crit;
                swingDirect[swingCount] = direct;
                swingCount++;
                hit += amount;
                if (crit)
                    crits++;
                if (direct)
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
                    var startAt = phaseAdvanced || phaseLatched ? phaseIndex : 0;
                    ClearDamage();
                    ClearEncounter();
                    ResetPhases();
                    phaseIndex = startAt;
                    pullStart = when;
                }

                inPull = true;
                pullSealed = false;
                lockedDuration = TimeSpan.Zero;
            }

            OpenPhase(when);
            var credit = GetOrAdd(creditId);
            if (phaseOpen && !phaseCreditAt.ContainsKey(creditId))
            {
                phaseCreditAt[creditId] = credit.RaidCredit;
                phaseDamageAt[creditId] = credit.Damage;
            }

            if (credit.Name.Length == 0)
                credit.Name = CreditName(fields, creditId, sourceId);

            credit.Damage += hit;
            credit.Hits += swingCount;
            credit.Crits += crits;
            credit.Directs += directs;
            if (biggest > credit.MaxHit)
            {
                credit.MaxHit = biggest;
                credit.MaxHitName = fields.Length > 5 ? fields[5] : "";
            }

            for (var swing = 0; swing < swingCount; swing++)
                Attribute(credit, targetId, swingAmount[swing], swingCrit[swing], swingDirect[swing], when);

            if (!pullSealed)
                lastEvent = when;
            if (IsEnemy(targetId))
            {
                damagedEnemies.Add(targetId);
                damageToEnemy.TryGetValue(targetId, out var dealt);
                damageToEnemy[targetId] = dealt + hit;
                if (fields.Length > 7 && fields[7].Length > 0)
                    enemyNames[targetId] = fields[7];
            }
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

            var name = fields.Length > 3 ? fields[3] : "";
            if (name.Length == 0 && enemyNames.TryGetValue(fields[2], out var known))
                name = known;
            if (name.Length > 0)
                enemyNames[fields[2]] = name;

            var when = Stamp(fields, clock);
            var next = PhasePlan.Down(zoneName, phaseIndex, name, phaseDown);
            if (next > phaseIndex)
            {
                CutTo(next, when);
                return;
            }

            killedEnemies.Add(fields[2]);
            if (!pullSealed && HasDamage() && AllDamagedDead())
                Seal(when);
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
            if (PhasePlan.Ladder(zoneName, 0).Length > 0 && (phaseIndex > 0 || phaseOpen || phases.Count > 0))
                return;
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

        private void ApplyStatus(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 8)
                return;

            if (!int.TryParse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var statusId))
                return;

            var stacks = fields.Length > 9 && ParseInt(fields[9]) is int count ? count : 0;
            if (!RaidBuffs.TryResolve(statusId, stacks, out var spec))
                return;

            var duration = fields.Length > 4 ? ParseDouble(fields[4]) : 0;
            if (duration <= 0 || duration > 120)
                duration = 30;

            var sourceId = fields.Length > 5 ? fields[5] : "";
            var targetId = fields.Length > 7 ? fields[7] : "";
            if (sourceId.Length == 0 || targetId.Length == 0)
                return;

            var start = Stamp(fields, clock);
            var window = new BuffWindow(statusId, sourceId, targetId, start, start.AddSeconds(duration), spec.Damage, spec.Crit, spec.Direct, spec.MagicOnly);
            for (var i = buffs.Count - 1; i >= 0; i--)
            {
                var current = buffs[i];
                if (current.StatusId == statusId && SameActor(current.SourceId, sourceId) && SameActor(current.TargetId, targetId))
                    buffs.RemoveAt(i);
            }

            buffs.Add(window);
        }

        private void RemoveStatus(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 7)
                return;

            if (!int.TryParse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var statusId))
                return;

            var sourceId = fields[4];
            var targetId = fields[6];
            var when = Stamp(fields, clock);
            for (var i = buffs.Count - 1; i >= 0; i--)
            {
                var current = buffs[i];
                if (current.StatusId != statusId || !SameActor(current.TargetId, targetId))
                    continue;
                if (sourceId.Length > 0 && !SameActor(current.SourceId, sourceId))
                    continue;
                if (current.End > when)
                    buffs[i] = current with { End = when };
                return;
            }
        }

        private void Attribute(Actor dealer, string targetId, long amount, bool crit, bool direct, DateTimeOffset when)
        {
            var level = dealer.Level is >= 1 and <= 100 ? dealer.Level : 100;
            var critStat = dealer.HasStats ? dealer.CritStat : LevelMods.DefaultCrit(level);
            var directStat = dealer.HasStats ? dealer.DirectStat : LevelMods.DefaultDirect(level);
            externalScratch.Clear();
            foreach (var buff in buffs)
            {
                if (when < buff.Start || when >= buff.End)
                    continue;

                if (IsEnemy(buff.TargetId))
                {
                    if (!SameActor(buff.TargetId, targetId))
                        continue;
                }
                else if (!SameActor(buff.TargetId, dealer.Id))
                {
                    continue;
                }

                if (buff.MagicOnly && !RaidBuffs.IsMagicJob(dealer.JobId))
                    continue;

                externalScratch.Add(new RdpsMath.External(buff.SourceId, buff.Damage, buff.Crit, buff.Direct));
            }

            RdpsMath.Attribute(
                amount,
                crit,
                direct,
                LevelMods.CritChance(level, critStat),
                LevelMods.CritMultiplier(level, critStat),
                LevelMods.DirectChance(level, directStat),
                externalScratch,
                dealer.Id,
                AddCredit);
        }

        private void AddCredit(string id, double amount)
        {
            if (id.Length == 0 || amount == 0)
                return;

            GetOrAdd(id).RaidCredit += amount;
        }

        private string BossName()
        {
            var ladder = PhasePlan.Ladder(zoneName, phaseIndex);
            if (ladder.Length > 0)
                return ladder;

            var bestId = "";
            long best = 0;
            foreach (var pair in damageToEnemy)
            {
                if (pair.Value <= best)
                    continue;

                best = pair.Value;
                bestId = pair.Key;
            }

            return bestId.Length > 0 && enemyNames.TryGetValue(bestId, out var name) ? name : "";
        }

        private static double ParseDouble(string text)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
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
                actor.RaidCredit = 0;
            }

            damageToEnemy.Clear();
        }

        private void NoteCast(string[] fields, DateTimeOffset clock)
        {
            if (fields.Length < 3)
                return;

            var when = Stamp(fields, clock);
            if (fields.Length > 5)
            {
                var next = PhasePlan.Signal(zoneName, phaseIndex, fields.Length > 3 ? fields[3] : "", fields[5]);
                if (next > phaseIndex)
                    CutTo(next, when);
            }

            if (!IsPlayer(fields[2]))
                return;

            Revive(fields[2], when);
        }

        private void NoticePhase(string sourceId, string sourceName, string targetId, string targetName, DateTimeOffset when)
        {
            if (PhasePlan.Ladder(zoneName, 0).Length == 0)
                return;

            if (IsEnemy(sourceId) && sourceName.Length > 0)
                enemyNames[sourceId] = sourceName;
            if (IsEnemy(targetId) && targetName.Length > 0)
                enemyNames[targetId] = targetName;

            var next = PhasePlan.Signal(zoneName, phaseIndex, sourceName, "");
            if (next < 0)
                next = PhasePlan.Signal(zoneName, phaseIndex, targetName, "");
            if (next > phaseIndex)
                CutTo(next, when);
        }

        private void CutTo(int next, DateTimeOffset when)
        {
            if (next <= phaseIndex)
                return;

            // The part is stored for the parse. Damage, the clock, and the
            // number on the meter keep running across the whole pull.
            ClosePhase(when);
            phaseDown.Clear();
            phaseIndex = next;
            phaseLatched = true;
        }

        private void OpenPhase(DateTimeOffset when)
        {
            if (phaseOpen || PhasePlan.Ladder(zoneName, 0).Length == 0)
                return;

            phaseLatched = false;
            phaseStart = when;
            phaseOpen = true;
            phaseCreditAt.Clear();
            phaseDamageAt.Clear();
            foreach (var actor in actors.Values)
            {
                phaseCreditAt[actor.Id] = actor.RaidCredit;
                phaseDamageAt[actor.Id] = actor.Damage;
            }
        }

        private void ClosePhase(DateTimeOffset when)
        {
            if (!phaseOpen)
                return;

            var seconds = when > phaseStart ? (when - phaseStart).TotalSeconds : 0;
            var ladder = PhasePlan.Ladder(zoneName, phaseIndex);
            phaseOpen = false;
            if (seconds < 1 || ladder.Length == 0)
                return;

            var slice = new PhaseSlice { Ladder = ladder, Seconds = seconds };
            foreach (var actor in actors.Values)
            {
                var credit = PhaseCredit(actor);
                if (credit <= 0)
                    continue;

                slice.Credit[actor.Id] = credit;
            }

            if (slice.Credit.Count > 0)
                phases.Add(slice);
        }

        private double PhaseCredit(Actor actor)
        {
            phaseCreditAt.TryGetValue(actor.Id, out var creditStart);
            phaseDamageAt.TryGetValue(actor.Id, out var damageStart);
            var credit = actor.RaidCredit - creditStart;
            if (credit <= 0)
                credit = actor.Damage - damageStart;
            return credit > 0 ? credit : 0;
        }

        private void ResetPhases()
        {
            phases.Clear();
            phaseCreditAt.Clear();
            phaseDamageAt.Clear();
            shownParse.Clear();
            phaseDown.Clear();
            phaseIndex = 0;
            phaseOpen = false;
            phaseLatched = false;
            phaseStart = default;
            parseDue = default;
        }

        private int ParseFor(Actor actor, double wholeDps, string boss, bool refresh)
        {
            if (PhasePlan.Ladder(zoneName, 0).Length == 0)
            {
                if (rankings != null && rankings.TryDisplay(zoneId, zoneName, boss, actor.JobId, out var scale))
                    return PercentileScale.Interpolate(wholeDps, scale.Points);
                return -1;
            }

            if (!refresh)
                return shownParse.TryGetValue(actor.Id, out var held) ? held : -1;

            var blended = Blend(actor);
            if (blended >= 0)
                shownParse[actor.Id] = blended;
            else if (shownParse.TryGetValue(actor.Id, out var held))
                return held;

            return blended;
        }

        private int Blend(Actor actor)
        {
            double weight = 0;
            double sum = 0;
            var closed = false;
            foreach (var slice in phases)
            {
                if (slice.Seconds < 8 || !slice.Credit.TryGetValue(actor.Id, out var credit) || credit <= 0)
                    continue;
                if (!Score(slice.Ladder, credit / slice.Seconds, actor.JobId, out var parse))
                    continue;

                closed = true;
                weight += slice.Seconds;
                sum += parse * slice.Seconds;
            }

            if (phaseOpen)
            {
                var seconds = lastEvent > phaseStart ? (lastEvent - phaseStart).TotalSeconds : 0;
                var credit = PhaseCredit(actor);
                var ready = seconds >= (closed ? 15 : 8);
                if (ready && credit > 0 && Score(PhasePlan.Ladder(zoneName, phaseIndex), credit / seconds, actor.JobId, out var parse))
                {
                    weight += seconds;
                    sum += parse * seconds;
                }
            }

            return weight > 0 ? (int)(sum / weight) : -1;
        }

        private bool Score(string ladder, double rdps, int jobId, out int parse)
        {
            parse = -1;
            if (ladder.Length == 0 || rankings == null || rdps <= 0)
                return false;
            if (!rankings.TryDisplay(zoneId, zoneName, ladder, jobId, out var scale))
                return false;

            parse = PercentileScale.Interpolate(rdps, scale.Points);
            return parse >= 0;
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
            ResetPhases();
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

            var boss = BossName();
            var refresh = DateTimeOffset.UtcNow >= parseDue;
            var rows = new List<MeterRow>();
            var scored = false;
            foreach (var actor in actors.Values)
            {
                if (actor.Damage <= 0)
                    continue;

                var raid = actor.RaidCredit > 0 ? actor.RaidCredit : actor.Damage;
                var dps = raid / seconds;
                var share = total > 0 ? (float)(actor.Damage * 100d / total) : 0f;
                DownTime(actor, duration, out var alive, out var dead);
                var parse = ParseFor(actor, dps, boss, refresh);
                if (parse >= 0)
                    scored = true;

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
                    dead,
                    parse));
            }

            if (refresh)
                parseDue = DateTimeOffset.UtcNow.AddSeconds(scored ? 10 : 2);

            rows.Sort(static (left, right) => right.Dps.CompareTo(left.Dps));
            if (!string.IsNullOrEmpty(error))
                note = error;

            var party = rows.Count > 8 ? 24 : rows.Count > 4 ? 8 : 4;
            return new ParseSnapshot(
                !string.IsNullOrEmpty(logFile),
                logFile,
                zoneName,
                inPull && !lastPull,
                duration,
                rows.ToArray(),
                note,
                lastPull,
                boss,
                party);
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
            public int Level;
            public int CritStat;
            public int DirectStat;
            public bool HasStats;
            public double RaidCredit;
        }

        private sealed class PhaseSlice
        {
            public string Ladder = "";
            public double Seconds;
            public Dictionary<string, double> Credit = new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly record struct BuffWindow(
            int StatusId,
            string SourceId,
            string TargetId,
            DateTimeOffset Start,
            DateTimeOffset End,
            double Damage,
            double Crit,
            double Direct,
            bool MagicOnly);
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
        double TimeDead,
        int Parse);

    public readonly record struct ParseSnapshot(
        bool Watching,
        string? LogFile,
        string ZoneName,
        bool InPull,
        TimeSpan Duration,
        MeterRow[] Rows,
        string Note,
        bool LastPull,
        string BossName,
        int PartySize);
}
