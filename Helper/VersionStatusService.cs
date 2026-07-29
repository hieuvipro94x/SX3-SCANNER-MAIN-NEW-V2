using Newtonsoft.Json;
using SX3_SCANER.Model.Respository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace SX3_SCANER.Helper
{
    internal sealed class VersionStatusReport
    {
        public string MachineName { get; set; }
        public string WindowsUser { get; set; }
        public string AppVersion { get; set; }
        public string LatestKnownVersion { get; set; }
        public bool IsLatestKnownVersion { get; set; }
        public bool UpdateCheckSucceeded { get; set; }
        public string UpdateStatus { get; set; }
        public string LastSeen { get; set; }
        public string AppPath { get; set; }
        public string DatabasePath { get; set; }
    }

    internal sealed class VersionStatusSummary
    {
        public int TotalMachines { get; set; }
        public int LatestMachines { get; set; }
        public int OutdatedMachines { get; set; }
        public int UnknownMachines { get; set; }
        public string LatestVersion { get; set; }
        public string DetailsText { get; set; }
    }

    internal static class VersionStatusService
    {
        private const string VersionStatusEnabledKey = "VersionStatusEnabled";
        private const string VersionStatusDirectoryKey = "VersionStatusDirectory";
        private const string DefaultVersionStatusDirectory =
            @"\\192.168.10.150\public\DB\SX3VersionStatus";
        private static readonly object SyncRoot = new object();

        internal static void EnsureDefaultSettings()
        {
            AppConfigHelper.EnsureCreate(VersionStatusEnabledKey, "0");
            AppConfigHelper.EnsureCreate(
                VersionStatusDirectoryKey,
                DefaultVersionStatusDirectory);
            NormalizeConfiguredDirectory();
        }

        internal static bool IsEnabled()
        {
            string value = AppConfigHelper.Read(VersionStatusEnabledKey);
            return value != null &&
                (value.Trim() == "1" ||
                 value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        internal static void SetEnabled(bool enabled)
        {
            AppConfigHelper.Modify(VersionStatusEnabledKey, enabled ? "1" : "0");
        }

        internal static string GetDirectory()
        {
            EnsureDefaultSettings();
            return NormalizeDirectory(AppConfigHelper.Read(VersionStatusDirectoryKey));
        }

        internal static void SetDirectory(string directory)
        {
            AppConfigHelper.Modify(VersionStatusDirectoryKey, NormalizeDirectory(directory));
        }

        internal static VersionStatusReport ReportCurrentMachine(
            string latestKnownVersion,
            bool updateCheckSucceeded,
            string updateStatus)
        {
            EnsureDefaultSettings();
            if (!IsEnabled())
                return null;

            string directory = GetDirectory();
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            string appVersion = currentVersion == null ? string.Empty : currentVersion.ToString(3);
            string machineName = Environment.MachineName;
            string statusPath = Path.Combine(directory, MakeSafeFileName(machineName) + ".json");
            string normalizedLatest = ResolveLatestKnownVersion(
                statusPath,
                latestKnownVersion,
                appVersion);

            var report = new VersionStatusReport
            {
                MachineName = machineName,
                WindowsUser = GetWindowsUser(),
                AppVersion = appVersion,
                LatestKnownVersion = normalizedLatest,
                IsLatestKnownVersion = IsLatest(appVersion, normalizedLatest),
                UpdateCheckSucceeded = updateCheckSucceeded,
                UpdateStatus = updateStatus ?? string.Empty,
                LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                AppPath = Assembly.GetExecutingAssembly().Location,
                DatabasePath = DatabaseRepository.DatabasePath
            };

            lock (SyncRoot)
            {
                Directory.CreateDirectory(directory);
                string path = statusPath;
                string temporaryPath = path + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonConvert.SerializeObject(report, Formatting.Indented));

                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }

            return report;
        }

        internal static VersionStatusSummary ReadSummary()
        {
            EnsureDefaultSettings();
            string directory = GetDirectory();
            var summary = new VersionStatusSummary
            {
                LatestVersion = string.Empty,
                DetailsText = string.Empty
            };

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                summary.DetailsText = "Chưa có thư mục theo dõi version hoặc thư mục chưa tồn tại.";
                return summary;
            }

            List<VersionStatusReport> reports = new List<VersionStatusReport>();
            foreach (string file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    VersionStatusReport report = JsonConvert.DeserializeObject<VersionStatusReport>(
                        File.ReadAllText(file));
                    if (report != null && !string.IsNullOrWhiteSpace(report.MachineName))
                        reports.Add(report);
                }
                catch (Exception ex)
                {
                    StartupManager.Log("Khong doc duoc version status " + file + ": " + ex.Message);
                }
            }

            summary.TotalMachines = reports.Count;
            if (reports.Count == 0)
            {
                summary.DetailsText = "Chưa có máy nào gửi trạng thái version.";
                return summary;
            }

            string latestVersion = reports
                .Select(x => string.IsNullOrWhiteSpace(x.LatestKnownVersion) ? x.AppVersion : x.LatestKnownVersion)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderByDescending(ParseVersionOrZero)
                .FirstOrDefault() ?? string.Empty;

            summary.LatestVersion = latestVersion;
            summary.LatestMachines = reports.Count(x => IsLatest(x.AppVersion, latestVersion));
            summary.OutdatedMachines = reports.Count(x => !string.IsNullOrWhiteSpace(x.AppVersion) && !IsLatest(x.AppVersion, latestVersion));
            summary.UnknownMachines = reports.Count(x => string.IsNullOrWhiteSpace(x.AppVersion));

            summary.DetailsText = string.Join(
                Environment.NewLine,
                reports
                    .OrderBy(x => x.MachineName)
                    .Select(x => FormatReportLine(x, latestVersion)));
            return summary;
        }

        private static void NormalizeConfiguredDirectory()
        {
            string configured = AppConfigHelper.Read(VersionStatusDirectoryKey);
            string normalized = NormalizeDirectory(configured);
            if (!string.Equals((configured ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                AppConfigHelper.Modify(VersionStatusDirectoryKey, normalized);
            }
        }

        private static string NormalizeDirectory(string directory)
        {
            string value = (directory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, @"C:\SX3VersionStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, @"\\ADMIN-PC\SX3VersionStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, @"\\192.168.10.150\SX3VersionStatus", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultVersionStatusDirectory;
            }

            return value;
        }

        private static string ResolveLatestKnownVersion(
            string statusPath,
            string latestKnownVersion,
            string appVersion)
        {
            if (!string.IsNullOrWhiteSpace(latestKnownVersion))
                return latestKnownVersion.Trim().TrimStart('v', 'V');

            try
            {
                if (File.Exists(statusPath))
                {
                    VersionStatusReport previous = JsonConvert.DeserializeObject<VersionStatusReport>(
                        File.ReadAllText(statusPath));
                    if (previous != null && !string.IsNullOrWhiteSpace(previous.LatestKnownVersion))
                        return previous.LatestKnownVersion.Trim().TrimStart('v', 'V');
                }
            }
            catch
            {
            }

            return appVersion;
        }
        private static string FormatReportLine(VersionStatusReport report, string latestVersion)
        {
            bool isLatest = IsLatest(report.AppVersion, latestVersion);
            string state = isLatest ? "OK" : "CẦN UPDATE";
            return report.MachineName +
                " | V" + Safe(report.AppVersion) +
                " | " + state +
                " | " + Safe(report.WindowsUser) +
                " | " + Safe(report.LastSeen) +
                (string.IsNullOrWhiteSpace(report.UpdateStatus) ? string.Empty : " | " + report.UpdateStatus);
        }

        private static bool IsLatest(string appVersion, string latestVersion)
        {
            Version app = ParseVersionOrZero(appVersion);
            Version latest = ParseVersionOrZero(latestVersion);
            return app.CompareTo(latest) >= 0;
        }

        private static Version ParseVersionOrZero(string value)
        {
            Version version;
            if (Version.TryParse((value ?? string.Empty).Trim().TrimStart('v', 'V'), out version))
                return version;
            return new Version(0, 0, 0);
        }

        private static string GetWindowsUser()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && !string.IsNullOrWhiteSpace(identity.Name))
                    return identity.Name;
            }
            catch
            {
            }

            return Environment.UserName;
        }

        private static string MakeSafeFileName(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return safe;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
