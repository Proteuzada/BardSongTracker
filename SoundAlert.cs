using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BardSongTracker
{
    /// <summary>
    /// Plays a short alert through its own WASAPI session, so the volume
    /// does not change FFXIV's game audio.
    /// </summary>
    public class SoundAlert : IDisposable
    {
        private DateTime lastAlertTime = DateTime.MinValue;
        private readonly Dictionary<string, string> extracted = new();
        private readonly object playLock = new();
        private string selectedId = AlertSounds.DefaultId;
        private bool isPlaying;
        private float volume = 0.3f;

        public void SetVolume(float vol)
        {
            volume = Math.Clamp(vol, 0f, 1f);
        }

        public void SetSound(string? id)
        {
            selectedId = AlertSounds.Normalize(id);
        }

        /// <summary>
        /// Play the selected alert if enough time has passed since the last one.
        /// </summary>
        public void PlayAlert(float remainingTime, bool ignoreCooldown = false)
        {
            const float cooldown = 3.0f;
            var elapsed = (DateTime.Now - lastAlertTime).TotalSeconds;

            if ((ignoreCooldown || elapsed >= cooldown) && !isPlaying)
            {
                lastAlertTime = DateTime.Now;
                var id = selectedId;
                Task.Run(() => PlayAsync(id));
            }
        }

        private void PlayAsync(string id)
        {
            lock (playLock)
            {
                if (isPlaying)
                    return;
                isPlaying = true;
            }

            try
            {
                var path = EnsureExtracted(id) ?? EnsureExtracted(AlertSounds.DefaultId);
                if (path == null)
                    return;

                using var audioFile = new AudioFileReader(path);
                var volumeProvider = new VolumeSampleProvider(audioFile)
                {
                    Volume = volume
                };

                using var outputDevice = new WasapiOut(
                    NAudio.CoreAudioApi.AudioClientShareMode.Shared,
                    latency: 100);

                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                    System.Threading.Thread.Sleep(50);
            }
            catch
            {
                // Playback can fail if no device is available.
            }
            finally
            {
                lock (playLock)
                {
                    isPlaying = false;
                }
            }
        }

        private string? EnsureExtracted(string id)
        {
            if (extracted.TryGetValue(id, out var existing) && File.Exists(existing))
                return existing;

            try
            {
                var sound = AlertSounds.Find(id);
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(sound.ResourceName);
                if (stream == null)
                    return null;

                var path = Path.Combine(Path.GetTempPath(), "BardSongTracker_" + sound.FileName);
                using (var fileStream = File.Create(path))
                    stream.CopyTo(fileStream);

                extracted[id] = path;
                return path;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var path in extracted.Values)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // The temp file can already be gone.
                }
            }

            extracted.Clear();
        }
    }
}
