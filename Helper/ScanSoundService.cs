using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Threading.Tasks;

namespace SX3_SCANER.Helper
{
    internal static class ScanSoundService
    {
        private static readonly object SoundLock = new object();
        private const string OkResourceName = "SX3_SCANER.Sounds.OK.wav";
        private const string NgResourceName = "SX3_SCANER.Sounds.NG.wav";

        private static SoundPlayer _okPlayer;
        private static SoundPlayer _ngPlayer;

        static ScanSoundService()
        {
            // Nap san am thanh de tranh delay lan dau khi quet tem.
            Task.Run(() => PreloadSounds());
        }

        public static void PlayOk()
        {
            PlayCachedSound(ref _okPlayer, OkResourceName, "OK.wav");
        }

        public static void PlayNg()
        {
            PlayCachedSound(ref _ngPlayer, NgResourceName, "NG.wav");
        }

        private static void PreloadSounds()
        {
            try
            {
                lock (SoundLock)
                {
                    _okPlayer = CreatePlayer(OkResourceName);
                    _ngPlayer = CreatePlayer(NgResourceName);
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("[ScanSound] Cannot preload sounds. " + ex.Message);
            }
        }

        private static void PlayCachedSound(
            ref SoundPlayer player,
            string resourceName,
            string fileName)
        {
            try
            {
                lock (SoundLock)
                {
                    if (player == null)
                    {
                        player = CreatePlayer(resourceName);
                    }

                    // Khong dung queue + PlaySync nua vi scan nhanh se bi don hang gay delay.
                    // Stop am cu va phat am moi ngay lap tuc.
                    player.Stop();
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[ScanSound] Cannot play " + fileName + ". " + ex.Message);
            }
        }

        private static SoundPlayer CreatePlayer(string resourceName)
        {
            Stream resourceStream = Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream(resourceName);

            if (resourceStream == null)
            {
                throw new InvalidOperationException(
                    "Embedded sound resource not found: " + resourceName);
            }

            var player = new SoundPlayer(resourceStream);
            player.Load();
            return player;
        }
    }
}
