using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BardSongTracker
{
    /// <summary>
    /// Plays the embedded kuru kuru MP3 when a Bard song is about to expire.
    /// Uses its own independent audio session via WASAPI so the volume
    /// does NOT interfere with FFXIV's game audio.
    /// </summary>
    public class SoundAlert : IDisposable
    {
        private DateTime lastAlertTime = DateTime.MinValue;
        private string? tempMp3Path;
        private bool isPlaying = false;
        private readonly object playLock = new();
        private float volume = 0.3f;

        public SoundAlert()
        {
            ExtractMp3Resource();
        }

        /// <summary>
        /// Set the playback volume (0.0 to 1.0).
        /// This is applied on the audio stream itself, not on the device,
        /// so it won't affect the game's volume.
        /// </summary>
        public void SetVolume(float vol)
        {
            volume = Math.Clamp(vol, 0f, 1f);
        }

        /// <summary>
        /// Play the alert sound if enough time has passed since the last alert.
        /// </summary>
        public void PlayAlert(float remainingTime, bool ignoreCooldown = false)
        {
            float cooldown = 3.0f;
            var elapsed = (DateTime.Now - lastAlertTime).TotalSeconds;

            if ((ignoreCooldown || elapsed >= cooldown) && !isPlaying)
            {
                lastAlertTime = DateTime.Now;
                Task.Run(PlayMp3Async);
            }
        }

        /// <summary>
        /// Play the MP3 file asynchronously using WASAPI with independent volume.
        /// The volume is applied at the sample level (VolumeSampleProvider),
        /// creating a completely separate audio stream from FFXIV.
        /// </summary>
        private void PlayMp3Async()
        {
            lock (playLock)
            {
                if (isPlaying)
                    return;
                isPlaying = true;
            }

            try
            {
                if (tempMp3Path == null || !File.Exists(tempMp3Path))
                {
                    ExtractMp3Resource();
                    if (tempMp3Path == null)
                        return;
                }

                using var audioFile = new AudioFileReader(tempMp3Path);

                // Apply volume at the sample level — this is independent of Windows/game volume
                var volumeProvider = new VolumeSampleProvider(audioFile)
                {
                    Volume = volume
                };

                // Use WasapiOut which creates its own audio session in Windows mixer
                // This makes it show up as a separate app in Volume Mixer
                using var outputDevice = new WasapiOut(
                    NAudio.CoreAudioApi.AudioClientShareMode.Shared,
                    latency: 100);

                // Set device volume to max — we control volume via VolumeSampleProvider
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                // Wait for playback to finish
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch
            {
                // Silently fail if audio playback fails
            }
            finally
            {
                lock (playLock)
                {
                    isPlaying = false;
                }
            }
        }

        /// <summary>
        /// Extract the embedded MP3 resource to a temporary file.
        /// </summary>
        private void ExtractMp3Resource()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "BardSongTracker.Resources.alert.mp3";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return;

                tempMp3Path = Path.Combine(Path.GetTempPath(), "BardSongTracker_alert.mp3");

                using var fileStream = File.Create(tempMp3Path);
                stream.CopyTo(fileStream);
            }
            catch
            {
                tempMp3Path = null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (tempMp3Path != null && File.Exists(tempMp3Path))
                    File.Delete(tempMp3Path);
            }
            catch { }
        }
    }
}
