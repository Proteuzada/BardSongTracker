using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Loads the FF Logs ranking curve for the boss on screen. The parse is the
    /// site's percentile of boss rDPS for that job, interpolated the same way
    /// the historical score is.
    /// </summary>
    public sealed class RankingClient
    {
        private static readonly HttpClient Http = CreateHttp();
        private static readonly int[] Marks = { 10, 25, 50, 75, 90, 95, 97, 98, 99, 100 };

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BardSongTracker");
            return http;
        }

        private readonly string cachePath;
        private readonly RankingTable table;
        private readonly List<ZoneInfo> zones = new();
        private readonly object gate = new();
        private string token = "";
        private DateTimeOffset tokenUntil;
        private int fetching;
        private int probing;
        private int clientState;
        private string clientLine = "API client not set.";
        private string checkedId = "";
        private string checkedSecret = "";
        private DateTimeOffset clientRetry;
        private string status = "Add an FF Logs API client on the Logs tab. The parse stays blank until the curve for this boss is loaded.";
        private string attemptKey = "";
        private DateTimeOffset retryAfter;

        public RankingClient(string cachePath)
        {
            this.cachePath = cachePath ?? "";
            table = string.IsNullOrWhiteSpace(cachePath) ? new RankingTable() : RankingTable.Load(cachePath);
        }

        public RankingTable Table => table;

        public string Status
        {
            get
            {
                lock (gate)
                    return status;
            }
        }

        public int ClientState
        {
            get
            {
                lock (gate)
                    return clientState;
            }
        }

        public string ClientLine
        {
            get
            {
                lock (gate)
                    return clientLine;
            }
        }

        public string MeterLink
        {
            get
            {
                lock (gate)
                {
                    if (clientState == 0)
                        return "FF Logs client not set";
                    if (clientState == 1)
                        return "Checking FF Logs…";
                    if (clientState == 3)
                        return "FF Logs client rejected";
                    if (clientState == 4)
                        return "FF Logs did not answer";
                    if (status.StartsWith("Parse curve loaded", StringComparison.Ordinal))
                        return "FF Logs connected";
                    if (status.StartsWith("Loading", StringComparison.Ordinal))
                        return "Loading parse…";
                    if (status.StartsWith("FF Logs has no encounter", StringComparison.Ordinal))
                        return "No FF Logs encounter";
                    if (status.StartsWith("FF Logs returned no ranking", StringComparison.Ordinal))
                        return "No FF Logs curve";
                    if (status.StartsWith("API client connected", StringComparison.Ordinal)
                        || status.StartsWith("Add an FF Logs", StringComparison.Ordinal))
                        return "FF Logs connected · parse pending";

                    return status.Length > 48 ? status[..48] : status;
                }
            }
        }

        public void Consider(ParseSnapshot snap, string? clientId, string? clientSecret)
        {
            var id = clientId?.Trim() ?? "";
            var secret = clientSecret?.Trim() ?? "";
            EnsureClient(id, secret);
            var gateState = ClientState;
            if (gateState == 3 || gateState == 4)
                return;
            if (id.Length == 0 || secret.Length == 0)
            {
                SetStatus("Add an FF Logs API client on the Logs tab. The parse stays blank until the curve for this boss is loaded.");
                return;
            }

            if (snap.Rows.Length == 0)
                return;

            if (string.IsNullOrWhiteSpace(snap.ZoneName))
            {
                SetStatus("Waiting for the duty name in the ACT log.");
                return;
            }

            var bossName = string.IsNullOrWhiteSpace(snap.BossName) ? snap.ZoneName.Trim() : snap.BossName.Trim();
            var ladders = PhasePlan.Names(snap.ZoneName);
            if (ladders.Length == 0)
                ladders = new[] { bossName };
            else if (PhasePlan.SingleLadder(snap.ZoneName))
                ladders = new[] { snap.ZoneName.Trim() };

            var jobs = new List<int>();
            foreach (var row in snap.Rows)
            {
                if (row.JobId > 0 && SpecName(row.JobId).Length > 0 && !jobs.Contains(row.JobId))
                    jobs.Add(row.JobId);
            }

            if (jobs.Count == 0)
            {
                SetStatus("Waiting for a job id in the ACT log.");
                return;
            }

            jobs.Sort();
            var yours = 0;
            foreach (var row in snap.Rows)
            {
                if (row.You && row.JobId > 0)
                    yours = row.JobId;
            }

            if (yours > 0 && jobs.Remove(yours))
                jobs.Insert(0, yours);
            var rankedSize = snap.PartySize >= 24 ? 24 : snap.PartySize >= 8 ? 8 : 0;
            var bossKey = string.Join("/", ladders);
            var key = snap.ZoneName + "|" + bossKey + "|" + rankedSize + "|" + string.Join(",", jobs);
            if (key == attemptKey && DateTimeOffset.UtcNow < retryAfter)
                return;

            var missing = false;
            foreach (var ladder in ladders)
            {
                foreach (var job in jobs)
                {
                    if (!table.TryFresh(snap.ZoneName, ladder, job, TimeSpan.FromHours(12), out _))
                        missing = true;
                }
            }

            if (!missing)
            {
                attemptKey = key;
                retryAfter = DateTimeOffset.UtcNow.AddHours(12);
                SetStatus(ladders.Length > 1
                    ? "Parse curve loaded for " + snap.ZoneName.Trim() + "."
                    : "Parse curve loaded for " + bossName + ".");
                return;
            }

            if (Interlocked.CompareExchange(ref fetching, 1, 0) != 0)
                return;

            var zone = snap.ZoneName;
            var label = ladders.Length > 1 ? snap.ZoneName.Trim() : bossName;
            SetStatus("Loading the FF Logs curve for " + label + ".");
            Task.Run(() => Fetch(zone, ladders, rankedSize, jobs, id, secret, key));
        }

        private async Task Fetch(string zone, IReadOnlyList<string> bosses, int partySize, List<int> jobs, string clientId, string clientSecret, string key)
        {
            try
            {
                var access = await Token(clientId, clientSecret);
                MarkConnected();
                EncounterMatch.TryFind(zone, out var knownId, out var knownDifficulty);
                var loaded = 0;
                var wanted = 0;
                string? missed = null;
                var targets = new List<(string Boss, int Encounter, bool Split)>();
                if (PhasePlan.SingleLadder(zone))
                {
                    if (knownId == 0)
                        missed = zone;
                    else
                        targets.Add((zone, knownId, false));
                }
                else
                {
                    var parentOnly = bosses.Count > 0;
                    foreach (var boss in bosses)
                    {
                        var catalogId = 0;
                        try
                        {
                            catalogId = await FindEncounter(access, zone, boss);
                        }
                        catch (Exception) when (knownId != 0)
                        {
                        }

                        // A part is its own ladder only when the catalog has an
                        // encounter that is not the whole fight. A failed lookup
                        // is not that answer.
                        if (catalogId != 0 && catalogId != knownId)
                        {
                            targets.Add((boss, catalogId, true));
                            parentOnly = false;
                            continue;
                        }

                        if (catalogId == 0 || catalogId != knownId)
                            parentOnly = false;
                    }

                    if (targets.Count == 0 && parentOnly && knownId != 0)
                        targets.Add((zone, knownId, false));
                    else if (targets.Count == 0 && bosses.Count > 0)
                        missed = bosses[0];
                }

                foreach (var target in targets)
                {
                    var boss = target.Boss;
                    var encounter = target.Encounter;
                    var split = target.Split;

                    var difficulty = knownDifficulty != 0 ? knownDifficulty : DifficultyFor(zone);
                    foreach (var job in jobs)
                    {
                        wanted++;
                        if (table.TryFresh(zone, boss, job, TimeSpan.FromHours(12), out _))
                        {
                            loaded++;
                            continue;
                        }

                        var spec = SpecName(job);
                        var points = await Curve(access, encounter, spec, partySize, difficulty, partial =>
                        {
                            Store(zone, boss, job, partial);
                            SetStatus("Parse curve loaded for " + boss.Trim() + ".");
                        });
                        if (!split && (points == null || points.Count == 0) && knownId != 0 && knownId != encounter)
                        {
                            encounter = knownId;
                            difficulty = knownDifficulty != 0 ? knownDifficulty : DifficultyFor(zone);
                            points = await Curve(access, encounter, spec, partySize, difficulty, partial =>
                            {
                                Store(zone, boss, job, partial);
                                SetStatus("Parse curve loaded for " + boss.Trim() + ".");
                            });
                        }

                        if (points == null || points.Count == 0)
                            continue;

                        Store(zone, boss, job, points);
                        loaded++;
                        SetStatus("Parse curve loaded for " + boss.Trim() + ".");
                    }
                }

                if (!string.IsNullOrWhiteSpace(cachePath))
                    table.Save(cachePath);

                var label = bosses.Count > 1 ? zone.Trim() : (bosses.Count > 0 ? bosses[0].Trim() : zone.Trim());
                attemptKey = key;
                retryAfter = missed == null && loaded >= wanted && wanted > 0
                    ? DateTimeOffset.UtcNow.AddHours(12)
                    : loaded > 0
                        ? DateTimeOffset.UtcNow.AddMinutes(10)
                        : DateTimeOffset.UtcNow.AddMinutes(2);
                if (loaded > 0)
                    SetStatus("Parse curve loaded for " + label + ".");
                else if (missed != null)
                    SetStatus("FF Logs has no encounter matching " + missed.Trim() + " in " + zone.Trim() + ".");
                else
                    SetStatus("FF Logs returned no ranking curve for " + label + ".");
            }
            catch (LogsFailure ex)
            {
                if (ex.Kind == LogsFailure.Reason.Rejected)
                    SetClient(3, ex.Message);
                else
                    NoteTemporary(ex.Message);

                attemptKey = key;
                retryAfter = DateTimeOffset.UtcNow.AddSeconds(ex.Kind == LogsFailure.Reason.Rejected ? 120 : 20);
            }
            catch (Exception ex)
            {
                var line = TimedOut(ex) ? "FF Logs did not answer in time. Trying again." : ex.Message;
                NoteTemporary(line);
                attemptKey = key;
                retryAfter = DateTimeOffset.UtcNow.AddSeconds(20);
            }
            finally
            {
                Interlocked.Exchange(ref fetching, 0);
            }
        }

        private void EnsureClient(string id, string secret)
        {
            var start = false;
            lock (gate)
            {
                if (id.Length == 0 || secret.Length == 0)
                {
                    checkedId = "";
                    checkedSecret = "";
                    token = "";
                    tokenUntil = default;
                    clientState = 0;
                    clientLine = "API client not set.";
                    return;
                }

                if (id == checkedId && secret == checkedSecret && clientState == 1)
                    return;
                if (id == checkedId && secret == checkedSecret && clientState == 3 && DateTimeOffset.UtcNow < clientRetry)
                    return;
                if (id == checkedId && secret == checkedSecret && clientState == 4 && DateTimeOffset.UtcNow < clientRetry)
                    return;
                if (id == checkedId && secret == checkedSecret && clientState == 2 && DateTimeOffset.UtcNow < tokenUntil)
                    return;

                checkedId = id;
                checkedSecret = secret;
                token = "";
                tokenUntil = default;
                clientState = 1;
                clientLine = "Checking the API client...";
                start = probing == 0;
                if (start)
                    probing = 1;
            }

            if (start)
                Task.Run(Probe);
        }

        private async Task Probe()
        {
            try
            {
                string id;
                string secret;
                lock (gate)
                {
                    id = checkedId;
                    secret = checkedSecret;
                }

                if (id.Length == 0 || secret.Length == 0)
                    return;

                await Token(id, secret);
                lock (gate)
                {
                    if (id != checkedId || secret != checkedSecret)
                        return;

                    clientState = 2;
                    clientLine = "API client connected.";
                    if (status.StartsWith("Add an FF Logs", StringComparison.Ordinal) || status.StartsWith("Checking", StringComparison.Ordinal))
                        status = "API client connected. The parse fills in during a ranked fight.";
                }
            }
            catch (LogsFailure ex)
            {
                if (ex.Kind == LogsFailure.Reason.Rejected)
                    SetClient(3, ex.Message);
                else
                    NoteTemporary(ex.Message);
            }
            catch (Exception ex)
            {
                NoteTemporary(TimedOut(ex) ? "FF Logs did not answer in time. Trying again." : ex.Message);
            }
            finally
            {
                var again = false;
                lock (gate)
                {
                    probing = 0;
                    again = clientState == 1 && checkedId.Length > 0 && checkedSecret.Length > 0;
                    if (again)
                        probing = 1;
                }

                if (again)
                    _ = Probe();
            }
        }

        private void MarkConnected()
        {
            lock (gate)
            {
                if (checkedId.Length == 0)
                    return;

                clientState = 2;
                clientLine = "API client connected.";
                if (status.StartsWith("Add an FF Logs", StringComparison.Ordinal) || status.StartsWith("Checking", StringComparison.Ordinal))
                    status = "API client connected. The parse fills in during a ranked fight.";
            }
        }

        private void NoteTemporary(string line)
        {
            lock (gate)
            {
                if (clientState != 2)
                {
                    clientState = 4;
                    clientLine = line;
                    clientRetry = DateTimeOffset.UtcNow.AddSeconds(20);
                }

                status = line;
            }
        }

        private void SetClient(int state, string line)
        {
            lock (gate)
            {
                clientState = state;
                clientLine = line;
                status = line;
                if (state == 3)
                    clientRetry = DateTimeOffset.UtcNow.AddMinutes(2);
            }
        }

        private async Task<string> Token(string clientId, string clientSecret)
        {
            if (token.Length > 0 && DateTimeOffset.UtcNow < tokenUntil)
                return token;

            LogsFailure? last = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await RequestToken(clientId, clientSecret);
                }
                catch (LogsFailure ex) when (ex.Kind != LogsFailure.Reason.Rejected && attempt == 0)
                {
                    last = ex;
                    await Task.Delay(1500);
                }
                catch (Exception ex) when (attempt == 0 && ex is not LogsFailure && (TimedOut(ex) || ex is HttpRequestException))
                {
                    last = new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs did not answer in time. Trying again.");
                    await Task.Delay(1500);
                }
            }

            throw last ?? new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs did not answer in time. Trying again.");
        }

        private async Task<string> RequestToken(string clientId, string clientSecret)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.fflogs.com/oauth/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("grant_type=client_credentials"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw FailureFor(response.StatusCode, body, true);

            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("access_token", out var access))
                throw new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs did not return an access token. Trying again.");

            token = access.GetString() ?? "";
            var seconds = 3600;
            if (json.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var parsed) && parsed > 120)
                seconds = parsed;

            tokenUntil = DateTimeOffset.UtcNow.AddSeconds(seconds - 60);
            return token;
        }

        private static LogsFailure FailureFor(System.Net.HttpStatusCode status, string body, bool tokenCall)
        {
            if ((int)status == 429 || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                return new LogsFailure(LogsFailure.Reason.RateLimit, "FF Logs is busy. Trying again.");

            var invalid = body.Contains("invalid_client", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Client authentication failed", StringComparison.OrdinalIgnoreCase);
            if (tokenCall && (status == System.Net.HttpStatusCode.Unauthorized || invalid))
                return new LogsFailure(LogsFailure.Reason.Rejected, "FF Logs rejected this client. Check the id and secret on the Logs tab.");

            if (status == System.Net.HttpStatusCode.Unauthorized)
                return new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs session expired. Trying again.");

            return new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs did not answer (" + (int)status + "). Trying again.");
        }

        private static bool TimedOut(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is TaskCanceledException || current is TimeoutException)
                    return true;
                if (current.Message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private async Task<int> FindEncounter(string access, string zoneName, string bossName)
        {
            if (zones.Count == 0)
                await LoadZones(access);

            var ready = PickLoaded(zoneName, bossName);
            if (ready != 0)
                return ready;

            foreach (var zone in zones)
            {
                if (zone.Encounters.Count > 0 || zone.Id == 0 || !NeedsEncounters(zone.Name, zoneName))
                    continue;

                await LoadEncounters(access, zone);
                ready = PickLoaded(zoneName, bossName);
                if (ready != 0)
                    return ready;
            }

            return 0;
        }

        private int PickLoaded(string zoneName, string bossName)
        {
            var matched = new List<EncounterMatch.ZoneNames>();
            foreach (var zone in zones)
            {
                var names = new EncounterMatch.ZoneNames { Name = zone.Name };
                foreach (var encounter in zone.Encounters)
                    names.Encounters.Add(new EncounterMatch.EncounterName(encounter.Id, encounter.Name));
                matched.Add(names);
            }

            return EncounterMatch.Pick(zoneName, bossName, matched);
        }

        private static bool NeedsEncounters(string zone, string duty)
        {
            if (EncounterMatch.Score(zone, duty) >= 3)
                return true;

            return zone.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Trial", StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadZones(string access)
        {
            const string expansions = """
                query {
                  worldData {
                    expansions {
                      zones {
                        id
                        name
                        encounters { id name }
                      }
                    }
                  }
                }
                """;
            try
            {
                using var json = await Graph(access, expansions);
                if (ReadExpansions(json))
                    return;
            }
            catch (Exception)
            {
            }

            const string query = """
                query {
                  worldData {
                    zones {
                      id
                      name
                      encounters { id name }
                    }
                  }
                }
                """;
            using var flat = await Graph(access, query);
            if (!flat.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("worldData", out var world)
                || !world.TryGetProperty("zones", out var list))
                throw new InvalidOperationException("FF Logs did not return its zone list.");

            zones.Clear();
            ReadZoneArray(list);
            if (zones.Count == 0)
                throw new InvalidOperationException("FF Logs did not return its zone list.");
        }

        private bool ReadExpansions(JsonDocument json)
        {
            if (!json.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("worldData", out var world)
                || !world.TryGetProperty("expansions", out var expansions)
                || expansions.ValueKind != JsonValueKind.Array)
                return false;

            zones.Clear();
            foreach (var expansion in expansions.EnumerateArray())
            {
                if (expansion.TryGetProperty("zones", out var list) && list.ValueKind == JsonValueKind.Array)
                    ReadZoneArray(list);
            }

            return zones.Count > 0;
        }

        private void ReadZoneArray(JsonElement list)
        {
            foreach (var zone in list.EnumerateArray())
            {
                var info = new ZoneInfo
                {
                    Id = zone.TryGetProperty("id", out var id) ? ReadId(id) : 0,
                    Name = zone.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                };
                if (zone.TryGetProperty("encounters", out var encounters) && encounters.ValueKind == JsonValueKind.Array)
                {
                    foreach (var encounter in encounters.EnumerateArray())
                    {
                        info.Encounters.Add(new EncounterInfo
                        {
                            Id = encounter.TryGetProperty("id", out var encounterId) ? ReadId(encounterId) : 0,
                            Name = encounter.TryGetProperty("name", out var encounterName) ? encounterName.GetString() ?? "" : "",
                        });
                    }
                }

                zones.Add(info);
            }
        }

        private static int ReadId(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
                return value;
            if (element.ValueKind == JsonValueKind.String
                && int.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return 0;
        }

        private static int DifficultyFor(string zone)
        {
            if (zone.Contains("Savage", StringComparison.OrdinalIgnoreCase))
                return 101;
            if (zone.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Extreme", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Unreal", StringComparison.OrdinalIgnoreCase)
                || zone.Contains("Ballad", StringComparison.OrdinalIgnoreCase))
                return 100;

            return 0;
        }

        private async Task<List<PercentileScale.Point>?> Curve(string access, int encounterId, string spec, int partySize, int difficulty, Action<List<PercentileScale.Point>>? publish)
        {
            var first = await Page(access, encounterId, spec, partySize, 1, 0, difficulty);
            if (first.Rankings.Count == 0 && difficulty != 0)
                first = await Page(access, encounterId, spec, 0, 1, 0, 0);
            if (first.Rankings.Count == 0)
                first = await Page(access, encounterId, spec, 0, 1, -1, 0);
            if (first.Rankings.Count == 0)
                return null;

            var population = await Population(access, encounterId, spec, first);
            if (population < 1)
                return null;

            var amounts = new Dictionary<int, double>();
            Remember(first, amounts);
            var pageSize = Math.Max(1, first.Rankings.Count);
            var pages = new List<int>();
            foreach (var mark in Marks)
            {
                var rank = PercentileScale.RankFor(mark, population);
                var page = ((rank - 1) / pageSize) + 1;
                if (page > 1 && !pages.Contains(page))
                    pages.Add(page);
            }

            if (pages.Count > 0)
            {
                try
                {
                    await RememberPages(access, encounterId, spec, first.UsedSize, first.Partition, first.Difficulty, pages, amounts);
                }
                catch (Exception)
                {
                }
            }

            var points = BuildPoints(amounts, population);
            if (points == null || !Spread(points))
                return null;

            publish?.Invoke(points);
            return points;
        }

        private async Task<int> Population(string access, int encounterId, string spec, RankPage first)
        {
            if (first.Total > first.Rankings.Count)
                return first.Total;
            if (!first.HasMore)
                return MaxRank(first);

            var last = first;
            var low = 1;
            var guard = 0;
            while (guard++ < 16)
            {
                var probe = await Probe(access, encounterId, spec, first, low * 2);
                if (probe.Rankings.Count == 0)
                    break;

                last = probe;
                low *= 2;
                if (probe.Total > probe.Rankings.Count)
                    return probe.Total;
                if (!probe.HasMore)
                    return MaxRank(probe);
            }

            var high = low * 2;
            while (low + 1 < high && guard++ < 28)
            {
                var mid = (low + high) / 2;
                var probe = await Probe(access, encounterId, spec, first, mid);
                if (probe.Rankings.Count == 0)
                {
                    high = mid;
                    continue;
                }

                last = probe;
                low = mid;
                if (probe.Total > probe.Rankings.Count)
                    return probe.Total;
                if (!probe.HasMore)
                    return MaxRank(probe);
            }

            return MaxRank(last);
        }

        private async Task<RankPage> Probe(string access, int encounterId, string spec, RankPage first, int page)
        {
            try
            {
                return await Page(access, encounterId, spec, first.UsedSize, page, first.Partition, first.Difficulty);
            }
            catch (InvalidOperationException)
            {
                return new RankPage();
            }
        }

        private static int MaxRank(RankPage page)
        {
            var max = 0;
            foreach (var row in page.Rankings)
            {
                if (row.Rank > max)
                    max = row.Rank;
            }

            if (max > 0)
                return max;

            return Math.Max(0, (page.Page - 1) * page.Rankings.Count) + page.Rankings.Count;
        }

        private static bool Spread(List<PercentileScale.Point> points)
        {
            var lowest = 100;
            var lowDps = 0d;
            var highDps = 0d;
            foreach (var point in points)
            {
                if (point.Percentile <= 0 || point.Dps <= 0)
                    continue;

                if (point.Percentile < lowest)
                {
                    lowest = point.Percentile;
                    lowDps = point.Dps;
                }

                if (point.Dps > highDps)
                    highDps = point.Dps;
            }

            return lowest <= 25 && highDps > lowDps * 1.02;
        }

        private static List<PercentileScale.Point>? BuildPoints(Dictionary<int, double> amounts, int count)
        {
            var points = new List<PercentileScale.Point> { new(0, 0) };
            foreach (var mark in Marks)
            {
                var rank = PercentileScale.RankFor(mark, count);
                if (!amounts.TryGetValue(rank, out var dps) || dps <= 0)
                    continue;

                points.Add(new PercentileScale.Point(mark, dps));
            }

            points.Sort((left, right) => left.Dps.CompareTo(right.Dps));
            return points.Count > 1 ? points : null;
        }

        private void Store(string zone, string boss, int job, List<PercentileScale.Point> points)
        {
            table.Upsert(new RankingTable.Entry
            {
                ZoneName = zone.Trim(),
                Boss = boss.Trim(),
                JobId = job,
                CurveVersion = RankingTable.CurveVersion,
                FetchedAt = DateTimeOffset.UtcNow,
                Points = points,
            });
        }

        private async Task RememberPages(string access, int encounterId, string spec, int partySize, int partition, int difficulty, List<int> pages, Dictionary<int, double> amounts)
        {
            var sizeArgument = partySize > 0 ? ", size: " + partySize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var partitionArgument = partition == -1 ? ", partition: -1" : "";
            var difficultyArgument = difficulty > 0 ? ", difficulty: " + difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var query = new StringBuilder();
            query.Append("query { worldData { encounter(id: ").Append(encounterId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(") {");
            foreach (var page in pages)
            {
                query.Append(" p").Append(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
                query.Append(": characterRankings(specName: \"").Append(spec).Append("\", metric: rdps, page: ");
                query.Append(page.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(difficultyArgument).Append(partitionArgument).Append(sizeArgument).Append(')');
            }

            query.Append(" } } }");
            try
            {
                using var json = await Graph(access, query.ToString());
                if (!json.RootElement.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("worldData", out var world)
                    || !world.TryGetProperty("encounter", out var encounter)
                    || encounter.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("FF Logs rankings could not be loaded.");

                foreach (var page in pages)
                {
                    var alias = "p" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!encounter.TryGetProperty(alias, out var rankings))
                        continue;

                    var pageData = new RankPage { Page = page, UsedSize = partySize, Partition = partition, Difficulty = difficulty };
                    if (rankings.ValueKind == JsonValueKind.String)
                    {
                        using var inner = JsonDocument.Parse(rankings.GetString() ?? "{}");
                        ReadRankings(inner.RootElement, pageData);
                    }
                    else
                    {
                        ReadRankings(rankings, pageData);
                    }

                    Remember(pageData, amounts);
                }
            }
            catch (Exception)
            {
                foreach (var page in pages)
                    Remember(await Page(access, encounterId, spec, partySize, page, partition, difficulty), amounts);
            }
        }

        private static void Remember(RankPage page, Dictionary<int, double> amounts)
        {
            for (var i = 0; i < page.Rankings.Count; i++)
            {
                var row = page.Rankings[i];
                var rank = row.Rank > 0 ? row.Rank : ((page.Page - 1) * page.Rankings.Count) + i + 1;
                amounts[rank] = row.Amount;
            }
        }

        private async Task<RankPage> Page(string access, int encounterId, string spec, int partySize, int page, int partition, int difficulty)
        {
            var sizeArgument = partySize > 0 ? ", size: " + partySize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var partitionArgument = partition == -1 ? ", partition: -1" : "";
            var difficultyArgument = difficulty > 0 ? ", difficulty: " + difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var query = $$"""
                query {
                  worldData {
                    encounter(id: {{encounterId}}) {
                      characterRankings(specName: "{{spec}}", metric: rdps, page: {{page}}{{difficultyArgument}}{{partitionArgument}}{{sizeArgument}})
                    }
                  }
                }
                """;
            JsonDocument json;
            try
            {
                json = await Graph(access, query);
            }
            catch (InvalidOperationException) when (partySize > 0 || difficulty > 0)
            {
                return await Page(access, encounterId, spec, 0, page, partition, 0);
            }

            using (json)
            {
            var pageData = new RankPage { Page = page, UsedSize = partySize, Partition = partition, Difficulty = difficulty };
            if (!json.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("worldData", out var world)
                || !world.TryGetProperty("encounter", out var encounter)
                || encounter.ValueKind == JsonValueKind.Null
                || !encounter.TryGetProperty("characterRankings", out var rankings))
                return pageData;

            if (rankings.ValueKind == JsonValueKind.String)
            {
                using var inner = JsonDocument.Parse(rankings.GetString() ?? "{}");
                ReadRankings(inner.RootElement, pageData);
            }
            else
            {
                ReadRankings(rankings, pageData);
            }

            if (pageData.Rankings.Count == 0 && partySize > 0)
                return await Page(access, encounterId, spec, 0, page, partition, difficulty);

            return pageData;
            }
        }

        private static void ReadRankings(JsonElement rankings, RankPage page)
        {
            if (rankings.ValueKind != JsonValueKind.Object)
                return;

            var stated = ReadJsonInt(rankings, "count");
            var total = ReadJsonInt(rankings, "total");
            if (total <= 0)
                total = ReadJsonInt(rankings, "totalRankings");
            if (total <= stated)
                total = stated;
            page.Count = stated;
            if (rankings.TryGetProperty("hasMorePages", out var more))
                page.HasMore = more.ValueKind == JsonValueKind.True;
            if (rankings.TryGetProperty("page", out var pageNumber) && pageNumber.TryGetInt32(out var parsedPage) && parsedPage > 0)
                page.Page = parsedPage;
            if (!rankings.TryGetProperty("rankings", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return;

            foreach (var row in rows.EnumerateArray())
            {
                var amount = 0d;
                if (row.TryGetProperty("amount", out var amountValue))
                    amount = ReadDouble(amountValue);
                else if (row.TryGetProperty("total", out var totalValue))
                    amount = ReadDouble(totalValue);

                var rank = 0;
                if (row.TryGetProperty("rank", out var rankValue))
                {
                    if (rankValue.TryGetInt32(out var parsedRank))
                        rank = parsedRank;
                    else if (rankValue.TryGetDouble(out var parsedDouble) && parsedDouble > 0)
                        rank = (int)parsedDouble;
                }

                page.Rankings.Add(new RankRow { Rank = rank, Amount = amount });
            }

            // count is the page length when it is no larger than the rows returned.
            // The population is larger whenever another page exists.
            if (total > page.Rankings.Count)
                page.Total = total;
            else if (!page.HasMore && page.Rankings.Count > 0)
                page.Total = MaxRank(page);
        }

        private static int ReadJsonInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
                return 0;
            if (value.TryGetInt32(out var parsed))
                return parsed;
            if (value.TryGetDouble(out var number) && number > 0)
                return (int)number;

            return 0;
        }

        private static double ReadDouble(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
                return value;

            return element.ValueKind == JsonValueKind.String
                && double.TryParse(element.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private async Task<JsonDocument> Graph(string access, string query)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.fflogs.com/api/v2/client");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["query"] = query });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    lock (gate)
                    {
                        token = "";
                        tokenUntil = default;
                    }
                }

                throw FailureFor(response.StatusCode, body, false);
            }

            var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var message = "FF Logs rankings could not be loaded.";
                var first = errors[0];
                if (first.TryGetProperty("message", out var text))
                    message = text.GetString() ?? message;

                json.Dispose();
                if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                    throw new LogsFailure(LogsFailure.Reason.RateLimit, "FF Logs is busy. Trying again.");
                if (message.Contains("invalid_client", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Client authentication failed", StringComparison.OrdinalIgnoreCase))
                    throw new LogsFailure(LogsFailure.Reason.Rejected, "FF Logs rejected this client. Check the id and secret on the Logs tab.");

                throw new LogsFailure(LogsFailure.Reason.Temporary, "FF Logs rankings could not be loaded. Trying again.");
            }

            return json;
        }

        private async Task LoadEncounters(string access, ZoneInfo zone)
        {
            var query = $$"""
                query {
                  worldData {
                    zone(id: {{zone.Id}}) {
                      encounters { id name }
                    }
                  }
                }
                """;
            try
            {
                using var json = await Graph(access, query);
                if (!json.RootElement.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("worldData", out var world)
                    || !world.TryGetProperty("zone", out var body)
                    || body.ValueKind != JsonValueKind.Object
                    || !body.TryGetProperty("encounters", out var encounters)
                    || encounters.ValueKind != JsonValueKind.Array)
                    return;

                zone.Encounters.Clear();
                foreach (var encounter in encounters.EnumerateArray())
                {
                    zone.Encounters.Add(new EncounterInfo
                    {
                        Id = encounter.TryGetProperty("id", out var encounterId) ? ReadId(encounterId) : 0,
                        Name = encounter.TryGetProperty("name", out var encounterName) ? encounterName.GetString() ?? "" : "",
                    });
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        public static string SpecName(int jobId)
        {
            return jobId switch
            {
                19 => "Paladin",
                21 => "Warrior",
                32 => "DarkKnight",
                37 => "Gunbreaker",
                24 => "WhiteMage",
                28 => "Scholar",
                33 => "Astrologian",
                40 => "Sage",
                20 => "Monk",
                22 => "Dragoon",
                30 => "Ninja",
                34 => "Samurai",
                39 => "Reaper",
                41 => "Viper",
                23 => "Bard",
                31 => "Machinist",
                38 => "Dancer",
                25 => "BlackMage",
                27 => "Summoner",
                35 => "RedMage",
                42 => "Pictomancer",
                36 => "BlueMage",
                _ => "",
            };
        }

        private void SetStatus(string message)
        {
            lock (gate)
                status = message;
        }

        private sealed class LogsFailure : Exception
        {
            public enum Reason
            {
                Rejected,
                RateLimit,
                Temporary,
            }

            public LogsFailure(Reason kind, string message) : base(message)
            {
                Kind = kind;
            }

            public Reason Kind { get; }
        }

        private sealed class ZoneInfo
        {
            public int Id;
            public string Name = "";
            public List<EncounterInfo> Encounters = new();
        }

        private sealed class EncounterInfo
        {
            public int Id;
            public string Name = "";
        }

        private sealed class RankPage
        {
            public int Page = 1;
            public int Count;
            public int Total;
            public int UsedSize;
            public int Partition;
            public int Difficulty;
            public bool HasMore;
            public List<RankRow> Rankings = new();
        }

        private sealed class RankRow
        {
            public int Rank;
            public double Amount;
        }
    }
}
