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

        // Hai file wav phai duoc set Build Action = Embedded Resource trong file .csproj.
        // Resource name mac dinh = DefaultNamespace + ten thu muc + ten file.
        private const string OkResourceName = "SX3_SCANER.Sounds.OK.wav";
        private const string NgResourceName = "SX3_SCANER.Sounds.NG.wav";

        private static byte[] _okSoundBytes;
        private static byte[] _ngSoundBytes;
        private static SoundPlayer _okPlayer;
        private static SoundPlayer _ngPlayer;

        static ScanSoundService()
        {
            // Nap san am thanh de tranh delay lan dau khi quet tem.
            Task.Run(() => PreloadSounds());
        }

        public static void PlayOk()
        {
            PlayCachedSound(ref _okPlayer, ref _okSoundBytes, OkResourceName, "OK.wav");
        }

        public static void PlayNg()
        {
            PlayCachedSound(ref _ngPlayer, ref _ngSoundBytes, NgResourceName, "NG.wav");
        }

        private static void PreloadSounds()
        {
            try
            {
                lock (SoundLock)
                {
                    if (_okSoundBytes == null)
                    {
                        _okSoundBytes = LoadResourceBytes(OkResourceName);
                    }

                    if (_ngSoundBytes == null)
                    {
                        _ngSoundBytes = LoadResourceBytes(NgResourceName);
                    }

                    if (_okPlayer == null)
                    {
                        _okPlayer = CreatePlayer(_okSoundBytes);
                    }

                    if (_ngPlayer == null)
                    {
                        _ngPlayer = CreatePlayer(_ngSoundBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("[ScanSound] Cannot preload embedded sounds. " + ex.Message);
            }
        }

        private static void PlayCachedSound(
            ref SoundPlayer player,
            ref byte[] soundBytes,
            string resourceName,
            string fileName)
        {
            try
            {
                lock (SoundLock)
                {
                    if (soundBytes == null)
                    {
                        soundBytes = LoadResourceBytes(resourceName);
                    }

                    if (player == null)
                    {
                        player = CreatePlayer(soundBytes);
                    }

                    // Khong dung queue + PlaySync nua vi scan nhanh se bi don hang gay delay.
                    // Stop am cu va phat am moi ngay lap tuc.
                    player.Stop();

                    if (player.Stream != null && player.Stream.CanSeek)
                    {
                        player.Stream.Position = 0;
                    }

                    player.Play();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[ScanSound] Cannot play embedded sound " + fileName + ". " + ex.Message);
            }
        }

        private static SoundPlayer CreatePlayer(byte[] soundBytes)
        {
            // Giu MemoryStream song ben trong SoundPlayer; khong dispose stream o day.
            var stream = new MemoryStream(soundBytes, false);
            var player = new SoundPlayer(stream);
            player.Load();
            return player;
        }

        private static byte[] LoadResourceBytes(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream resourceStream = assembly.GetManifestResourceStream(resourceName);

            if (resourceStream == null)
            {
                string availableResources = string.Join(", ", assembly.GetManifestResourceNames());
                throw new InvalidOperationException(
                    "Embedded sound resource not found: " + resourceName +
                    ". Available resources: " + availableResources);
            }

            using (resourceStream)
            using (var memoryStream = new MemoryStream())
            {
                resourceStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }
    }
}
