using System;
using System.IO;
using NAudio.Wave;

namespace Jumpscare
{
    public static class Sounds
    {
        private static IWavePlayer? Player;
        private static AudioFileReader? Reader;

        public static void Play(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Plugin.Log.Warning($"Sound file not found: {path}");
                    return;
                }

                Stop();

                Reader = new AudioFileReader(path);
                Player = new WaveOutEvent();

                Player.PlaybackStopped += (_, _) => Stop();

                Player.Init(Reader);
                Player.Play();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to play sound: {ex}");
                Stop();
            }
        }

        public static void Stop()
        {
            try
            {
                Player?.Stop();
            }
            catch
            {
                // prevents errors silently while disposing
            }

            Player?.Dispose();
            Reader?.Dispose();

            Player = null;
            Reader = null;
        }
    }
}
