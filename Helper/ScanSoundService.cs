using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace SX3_SCANER.Helper
{
    internal static class ScanSoundService
    {
        private static readonly object SoundLock = new object();

        private static readonly string OkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Sounds",
            "OK.wav");

        private static readonly string NgPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Sounds",
            "NG.wav");

        private static SoundPlayer _okPlayer;
        private static SoundPlayer _ngPlayer;

        static ScanSoundService()
        {
            // Nap san am thanh de tranh delay lan dau khi quet tem.
            Task.Run(() => PreloadSounds());
        }

        public static void PlayOk()
        {
            PlayCachedSound(ref _okPlayer, OkPath, "OK.wav");
        }

        public static void PlayNg()
        {
            PlayCachedSound(ref _ngPlayer, NgPath, "NG.wav");
        }

        private static void PreloadSounds()
        {
            try
            {
                if (File.Exists(OkPath))
                {
                    _okPlayer = new SoundPlayer(OkPath);
                    _okPlayer.Load();
                }

                if (File.Exists(NgPath))
                {
                    _ngPlayer = new SoundPlayer(NgPath);
                    _ngPlayer.Load();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("[ScanSound] Cannot preload sounds. " + ex.Message);
            }
        }

        private static void PlayCachedSound(ref SoundPlayer player, string path, string fileName)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                lock (SoundLock)
                {
                    if (player == null)
                    {
                        player = new SoundPlayer(path);
                        player.Load();
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
    }
}
