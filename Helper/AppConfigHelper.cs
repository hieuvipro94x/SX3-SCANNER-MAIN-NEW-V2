using Newtonsoft.Json;
using SX3_SCANER.Model.Respository;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;

namespace SX3_SCANER.Helper
{
    internal static class AppConfigHelper
    {
        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> _cachedRuntimeSettings;

        public static string Read(string key)
        {
            lock (SyncRoot)
            {
                Dictionary<string, string> runtimeSettings = LoadRuntimeSettings();
                if (runtimeSettings.TryGetValue(key, out string runtimeValue))
                {
                    return runtimeValue;
                }
            }

            return ReadStaticAppSetting(key);
        }

        public static void EnsureCreate(string key, string value)
        {
            lock (SyncRoot)
            {
                Dictionary<string, string> settings = LoadRuntimeSettings();
                if (settings.ContainsKey(key))
                {
                    return;
                }

                settings[key] = ReadStaticAppSetting(key) ?? value ?? string.Empty;
                SaveRuntimeSettings(settings);
            }
        }

        public static bool Modify(string key, string value)
        {
            lock (SyncRoot)
            {
                try
                {
                Dictionary<string, string> settings = LoadRuntimeSettings();
                string nextValue = value ?? string.Empty;
                string currentValue;
                if (settings.TryGetValue(key, out currentValue) &&
                    string.Equals(currentValue, nextValue, StringComparison.Ordinal))
                {
                    return true;
                }

                settings[key] = nextValue;
                SaveRuntimeSettings(settings);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Khong luu duoc cau hinh runtime '" + key + "': " + ex);
                    return false;
                }
            }
        }

        private static Dictionary<string, string> LoadRuntimeSettings()
        {
            if (_cachedRuntimeSettings != null)
                return _cachedRuntimeSettings;

            try
            {
                string path = DatabaseRepository.RuntimeConfigPath;
                if (!File.Exists(path))
                {
                    _cachedRuntimeSettings = NewSettingsDictionary();
                    return _cachedRuntimeSettings;
                }

                Dictionary<string, string> settings =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));

                if (settings == null)
                {
                    _cachedRuntimeSettings = NewSettingsDictionary();
                    return _cachedRuntimeSettings;
                }

                _cachedRuntimeSettings = new Dictionary<string, string>(
                    settings,
                    StringComparer.OrdinalIgnoreCase);
                return _cachedRuntimeSettings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Khong doc duoc cau hinh runtime: " + ex);
                _cachedRuntimeSettings = NewSettingsDictionary();
                return _cachedRuntimeSettings;
            }
        }

        private static void SaveRuntimeSettings(Dictionary<string, string> settings)
        {
            string path = DatabaseRepository.RuntimeConfigPath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonConvert.SerializeObject(settings, Formatting.Indented));

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }

        private static Dictionary<string, string> NewSettingsDictionary()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string ReadStaticAppSetting(string key)
        {
            try
            {
                return ConfigurationManager.AppSettings[key];
            }
            catch (ConfigurationErrorsException ex)
            {
                Debug.WriteLine("Khong doc duoc appSettings '" + key + "': " + ex.Message);
                return null;
            }
        }
    }
}
