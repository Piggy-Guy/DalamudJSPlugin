using System;
using System.IO;
using NAudio.Wave;

namespace Jumpscare
{
    public static class Sounds
    {
        private static IWavePlayer? player;
        private static AudioFileReader? reader;

        /// <summary>
        /// Plays an audio file (WAV, MP3, etc.)
        /// </summary>
        public static void Play(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Plugin.Log.Warning($"Sound file not found: {path}");
                    return;
                }

                Stop(); // stop any currently playing sound

                reader = new AudioFileReader(path);
                player = new WaveOutEvent();

                player.PlaybackStopped += (_, _) => Stop();

                player.Init(reader);
                player.Play();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to play sound: {ex}");
                Stop();
            }
        }

        /// <summary>
        /// Stops playback and releases audio resources
        /// </summary>
        public static void Stop()
        {
            try
            {
                player?.Stop();
            }
            catch
            {
                // ignore stop race conditions
            }

            player?.Dispose();
            reader?.Dispose();

            player = null;
            reader = null;
        }
    }
}
