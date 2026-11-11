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
        private RankingTable rankings = new();
        private string directory = "";
        private string? logFile;
        private string? error;
        private double idleSeconds = 25;
        private bool enabled;

        public void Configure(bool watch, string? logDirectory, string? rankingsPath, float idle)
        {
            lock (gate)
            {
                enabled = watch;
                directory = logDirectory?.Trim() ?? "";
                idleSeconds = idle < 5 ? 25 : idle;
                rankings = RankingTable.Load(rankingsPath);
                error = rankings.Error;
            }

            if (watch)
                EnsureStarted();
        }

        public ParseSnapshot Snapshot()
        {
            lock (gate)
            {
                var clock = DateTimeOffset.Now;
                meter.Advance(clock, idleSeconds);
                return meter.Snapshot(clock, rankings, logFile, error);
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
                            ? "ACT log folder not found."
                            : "No Network_*.log in the ACT folder.");
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
            SetStatus(file, rankings.Error);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(0, SeekOrigin.End);
            var pending = "";
            var switchedAt = DateTime.UtcNow;

            while (!token.IsCancellationRequested && IsEnabled())
            {
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

            if (!string.IsNullOrWhiteSpace(configured))
                return Directory.Exists(configured) ? configured : null;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var candidates = new[]
            {
                Path.Combine(appData, "Advanced Combat Tracker", "FFXIVLogs"),
                Path.Combine(documents, "Advanced Combat Tracker", "FFXIVLogs"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
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
