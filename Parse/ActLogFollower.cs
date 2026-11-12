using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BardSongTracker.Parse
{
    /// <summary>
    /// Tails the newest Network_*.log written by the FFXIV ACT plugin.
    /// Those files are the logs FFLogs accepts.
    /// </summary>
    public sealed class ActLogFollower : IDisposable
    {
        private readonly object gate = new();
        private readonly PullMeter meter = new();
        private Thread? thread;
        private CancellationTokenSource? cts;
        private string directory = "";
        private string? logFile;
        private string? error;
        private double idleSeconds = 25;
        private bool enabled;

        public void Configure(bool watch, float idle, string? logDirectory)
        {
            lock (gate)
            {
                enabled = watch;
                directory = logDirectory?.Trim().Trim('"') ?? "";
                idleSeconds = idle < 5 ? 25 : idle;
                error = null;
            }

            if (watch)
                EnsureStarted();
        }

        public void SetRankings(RankingTable table)
        {
            lock (gate)
                meter.UseRankings(table);
        }

        public ParseSnapshot Snapshot(bool keepLastPull)
        {
            lock (gate)
            {
                var clock = DateTimeOffset.Now;
                meter.Advance(clock, idleSeconds);
                return meter.Snapshot(clock, logFile, error, keepLastPull);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void EnsureStarted()
        {
            if (thread is { IsAlive: true })
                return;

            cts = new CancellationTokenSource();
            thread = new Thread(() => Run(cts.Token))
            {
                IsBackground = true,
                Name = "BardSongTracker.ActLog",
            };
            thread.Start();
        }

        private void Stop()
        {
            cts?.Cancel();
        }

        private void Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsEnabled())
                {
                    Thread.Sleep(200);
                    continue;
                }

                try
                {
                    var file = NewestLog();
                    if (file == null)
                    {
                    SetStatus(null, ResolveDirectory() == null
                        ? "ACT log folder not found. Choose it in the Logs tab."
                        : "No Network_*.log in the selected folder.");
                        Thread.Sleep(1000);
                        continue;
                    }

                    Tail(file, token);
                }
                catch (Exception ex)
                {
                    SetStatus(null, ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private void Tail(string file, CancellationToken token)
        {
            SetStatus(file, null);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            RememberJobs(stream);
            stream.Seek(0, SeekOrigin.End);
            var pending = "";
            var switchedAt = DateTime.UtcNow;
            var folder = ResolveDirectory();

            while (!token.IsCancellationRequested && IsEnabled())
            {
                if (!string.Equals(ResolveDirectory(), folder, StringComparison.OrdinalIgnoreCase))
                    return;

                var buffer = new byte[16384];
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    pending += Encoding.UTF8.GetString(buffer, 0, read);
                    var consumed = 0;
                    while (true)
                    {
                        var newline = pending.IndexOf('\n', consumed);
                        if (newline < 0)
                            break;

                        var line = pending[consumed..newline].TrimEnd('\r');
                        consumed = newline + 1;
                        if (line.Length > 0)
                        {
                            lock (gate)
                                meter.Apply(line, DateTimeOffset.Now);
                        }
                    }

                    if (consumed > 0)
                        pending = pending[consumed..];
                    continue;
                }

                if ((DateTime.UtcNow - switchedAt).TotalSeconds > 2)
                {
                    var newer = NewestLog();
                    if (newer != null && !string.Equals(newer, file, StringComparison.OrdinalIgnoreCase))
                        return;

                    switchedAt = DateTime.UtcNow;
                }

                lock (gate)
                    meter.Advance(DateTimeOffset.Now, idleSeconds);

                Thread.Sleep(100);
            }
        }

        private void RememberJobs(FileStream stream)
        {
            var start = LastZoneOffset(stream);
            stream.Seek(start, SeekOrigin.Begin);
            var pending = "";
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                pending += Encoding.UTF8.GetString(buffer, 0, read);
                var consumed = 0;
                while (true)
                {
                    var newline = pending.IndexOf('\n', consumed);
                    if (newline < 0)
                        break;

                    var line = pending[consumed..newline].TrimEnd('\r');
                    consumed = newline + 1;
                    if (line.Length > 2 && IsIdentityLine(line))
                    {
                        lock (gate)
                            meter.Remember(line);
                    }
                }

                if (consumed > 0)
                    pending = pending[consumed..];
            }
        }

        private static bool IsIdentityLine(string line)
        {
            return line.StartsWith("01|", StringComparison.Ordinal)
                || line.StartsWith("02|", StringComparison.Ordinal)
                || line.StartsWith("03|", StringComparison.Ordinal)
                || line.StartsWith("12|", StringComparison.Ordinal)
                || line.StartsWith("261|", StringComparison.Ordinal);
        }

        private static long LastZoneOffset(FileStream stream)
        {
            var length = stream.Length;
            const int window = 1024 * 1024;
            var buffer = new byte[window];
            var pos = length;
            while (pos > 0)
            {
                var take = (int)Math.Min(window, pos);
                var start = pos - take;
                stream.Seek(start, SeekOrigin.Begin);
                var got = stream.Read(buffer, 0, take);
                for (var i = got - 3; i >= 1; i--)
                {
                    if (buffer[i - 1] != (byte)'\n')
                        continue;
                    if (buffer[i] == (byte)'0' && buffer[i + 1] == (byte)'1' && buffer[i + 2] == (byte)'|')
                        return start + i;
                }

                if (start == 0)
                {
                    if (got >= 3 && buffer[0] == (byte)'0' && buffer[1] == (byte)'1' && buffer[2] == (byte)'|')
                        return 0;
                    break;
                }

                pos = start + 3;
            }

            return 0;
        }

        private bool IsEnabled()
        {
            lock (gate)
                return enabled;
        }

        private string? NewestLog()
        {
            var folder = ResolveDirectory();
            if (folder == null)
                return null;

            string? best = null;
            var bestTime = DateTime.MinValue;
            foreach (var candidate in Directory.GetFiles(folder, "Network_*.log"))
            {
                var written = File.GetLastWriteTimeUtc(candidate);
                if (written >= bestTime)
                {
                    bestTime = written;
                    best = candidate;
                }
            }

            return best;
        }

        private string? ResolveDirectory()
        {
            string configured;
            lock (gate)
                configured = directory;

            return ActLogPaths.Resolve(configured);
        }

        private void SetStatus(string? file, string? message)
        {
            lock (gate)
            {
                logFile = file;
                error = string.IsNullOrWhiteSpace(message) ? null : message;
            }
        }
    }
}
