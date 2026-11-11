using System;
using System.Collections.Generic;
using System.IO;

namespace BardSongTracker.Parse
{
    internal static class ActLogPaths
    {
        public static string? Resolve(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return Existing(configured.Trim().Trim('"'));

            foreach (var candidate in Defaults())
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static IEnumerable<string> Defaults()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return Path.Combine(appData, "Advanced Combat Tracker", "FFXIVLogs");
            yield return Path.Combine(documents, "Advanced Combat Tracker", "FFXIVLogs");
            yield return Path.Combine(profile, "OneDrive", "Documents", "Advanced Combat Tracker", "FFXIVLogs");
            yield return Path.Combine(profile, "OneDrive", "Documentos", "Advanced Combat Tracker", "FFXIVLogs");
            yield return Path.Combine(appData, "IINACT", "FFXIVLogs");
            yield return Path.Combine(appData, "IINACT", "logs");
            yield return Path.Combine(local, "IINACT", "FFXIVLogs");
        }

        private static string? Existing(string path)
        {
            var nested = Path.Combine(path, "FFXIVLogs");
            if (Directory.Exists(nested) && !ContainsLogs(path) && ContainsLogs(nested))
                return nested;

            return Directory.Exists(path) ? path : null;
        }

        private static bool ContainsLogs(string path)
        {
            try
            {
                return Directory.GetFiles(path, "Network_*.log").Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
