using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace SX3_SCANER.Helper
{
    internal sealed class GitHubReleaseUpdateService
    {
        internal const string ReleasesPageUrl =
            "https://github.com/hieuvipro94x/sx3-scanner-release/releases";

        private const string LatestReleaseApiUrl =
            "https://api.github.com/repos/hieuvipro94x/sx3-scanner-release/releases/latest";
        private const string UserAgent = "SX3Scanner-Updater";
        private const string EnabledSetting = "UpdateCheckOnStartup";
        private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

        private DateTime? _lastAutomaticCheckUtc;
        private GitHubReleaseUpdateInfo _lastAutomaticResult;

        internal bool LastCheckSucceeded { get; private set; }

        internal string LastStatusMessage { get; private set; }

        internal async Task<GitHubReleaseUpdateInfo> CheckForUpdateAsync(bool showErrors)
        {
            if (!showErrors && !IsStartupCheckEnabled())
            {
                LastCheckSucceeded = true;
                LastStatusMessage = "Tự động cập nhật đã tắt.";
                return null;
            }

            if (!showErrors &&
                _lastAutomaticCheckUtc.HasValue &&
                DateTime.UtcNow - _lastAutomaticCheckUtc.Value < AutomaticCheckInterval)
            {
                return _lastAutomaticResult;
            }

            if (!showErrors)
            {
                _lastAutomaticCheckUtc = DateTime.UtcNow;
                _lastAutomaticResult = null;
            }

            LastCheckSucceeded = false;
            LastStatusMessage = "Không thể kiểm tra cập nhật.";

            try
            {
                GitHubRelease release;
                using (var client = CreateHttpClient(RequestTimeout))
                using (HttpResponseMessage response = await client.GetAsync(LatestReleaseApiUrl))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return HandleCheckError(
                            "GitHub chưa có bản phát hành nào.",
                            showErrors,
                            null);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return HandleCheckError(
                            "GitHub API trả về lỗi " + (int)response.StatusCode + " (" +
                            response.ReasonPhrase + ").",
                            showErrors,
                            null);
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                }

                GitHubReleaseUpdateInfo info = BuildUpdateInfo(release);
                LastCheckSucceeded = true;

                if (!info.IsUpdateAvailable)
                {
                    LastStatusMessage = "Không có bản mới.";
                    return null;
                }

                LastStatusMessage = "Có bản mới: V" + info.Version;
                if (!showErrors)
                {
                    _lastAutomaticResult = info;
                }

                return info;
            }
            catch (TaskCanceledException ex)
            {
                string message = ex.CancellationToken.IsCancellationRequested
                    ? "Đã hủy kiểm tra cập nhật."
                    : "GitHub API phản hồi quá thời gian.";
                return HandleCheckError(message, showErrors, ex);
            }
            catch (HttpRequestException ex)
            {
                return HandleCheckError(
                    "Không có Internet hoặc không kết nối được GitHub.",
                    showErrors,
                    ex);
            }
            catch (JsonException ex)
            {
                return HandleCheckError(
                    "Dữ liệu trả về từ GitHub API không hợp lệ.",
                    showErrors,
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                return HandleCheckError(ex.Message, showErrors, ex);
            }
            catch (Exception ex)
            {
                return HandleCheckError(
                    "Không thể kiểm tra cập nhật từ GitHub.",
                    showErrors,
                    ex);
            }
        }

        internal async Task<string> DownloadUpdateAsync(GitHubReleaseUpdateInfo info)
        {
            ValidateDownloadInfo(info);

            string updateDirectory = Path.Combine(
                Path.GetTempPath(),
                "SX3Scanner",
                "Updates");
            Directory.CreateDirectory(updateDirectory);

            string safeFileName = Path.GetFileName(info.FileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = "SX3ScannerSetup-" + info.Version + ".exe";
            }

            string installerPath = Path.Combine(updateDirectory, safeFileName);

            using (var client = CreateHttpClient(DownloadTimeout))
            using (HttpResponseMessage response = await client.GetAsync(
                info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Tải bản cập nhật thất bại. GitHub trả về lỗi " +
                        (int)response.StatusCode + " (" + response.ReasonPhrase + ").");
                }

                using (Stream source = await response.Content.ReadAsStreamAsync())
                using (var target = new FileStream(
                    installerPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await source.CopyToAsync(target);
                }
            }

            var downloadedFile = new FileInfo(installerPath);
            if (!downloadedFile.Exists || downloadedFile.Length <= 0)
            {
                TryDelete(installerPath);
                throw new InvalidOperationException(
                    "File cập nhật tải về không tồn tại hoặc có dung lượng bằng 0.");
            }

            if (info.FileSize > 0 && downloadedFile.Length != info.FileSize)
            {
                TryDelete(installerPath);
                throw new InvalidOperationException(
                    "Dung lượng file tải về không khớp với asset trên GitHub.");
            }

            return installerPath;
        }

        internal void RunInstallerAndExit(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                throw new FileNotFoundException("Không tìm thấy file cài đặt đã tải.", installerPath);
            }

            if (new FileInfo(installerPath).Length <= 0)
            {
                throw new InvalidOperationException("File cài đặt có dung lượng bằng 0.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }

        internal async Task<bool> DownloadRunAndExitAsync(GitHubReleaseUpdateInfo info)
        {
            try
            {
                string installerPath = await DownloadUpdateAsync(info);
                RunInstallerAndExit(installerPath);
                return true;
            }
            catch (TaskCanceledException ex)
            {
                LastStatusMessage = ex.CancellationToken.IsCancellationRequested
                    ? "Đã hủy tải bản cập nhật."
                    : "Tải bản cập nhật quá thời gian.";
                ShowError(LastStatusMessage);
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastStatusMessage = "Download lỗi: không có Internet hoặc không kết nối được GitHub.";
                LogError("update-download-http", ex);
                ShowError(LastStatusMessage);
                return false;
            }
            catch (Win32Exception ex)
            {
                LastStatusMessage = ex.NativeErrorCode == 1223
                    ? "Người dùng đã hủy hoặc không cấp quyền chạy installer."
                    : "Không có quyền chạy installer.";
                LogError("update-installer-permission", ex);
                ShowError(LastStatusMessage);
                return false;
            }
            catch (Exception ex)
            {
                LastStatusMessage = ex.Message.StartsWith("Tải bản cập nhật", StringComparison.Ordinal)
                    ? ex.Message
                    : "Download lỗi hoặc không thể chạy installer: " + ex.Message;
                LogError("update-download-run", ex);
                ShowError(LastStatusMessage);
                return false;
            }
        }

        internal static Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            Version assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion != null && assemblyVersion.Major > 0)
            {
                return assemblyVersion;
            }

            var informationalVersion =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Version parsedVersion;
            if (informationalVersion != null &&
                TryParseVersion(informationalVersion.InformationalVersion, out parsedVersion))
            {
                return parsedVersion;
            }

            string configuredVersion = ConfigurationManager.AppSettings["CurrentVersion"];
            if (TryParseVersion(configuredVersion, out parsedVersion))
            {
                return parsedVersion;
            }

            throw new InvalidOperationException("Không xác định được phiên bản hiện tại của ứng dụng.");
        }

        private static GitHubReleaseUpdateInfo BuildUpdateInfo(GitHubRelease release)
        {
            if (release == null)
            {
                throw new InvalidOperationException("GitHub API không trả về thông tin release.");
            }

            Version latestVersion;
            if (!TryParseVersion(release.TagName, out latestVersion))
            {
                throw new InvalidOperationException(
                    "Version parse lỗi: tag_name '" + (release.TagName ?? string.Empty) +
                    "' không phải phiên bản hợp lệ.");
            }

            GitHubReleaseAsset asset = SelectInstallerAsset(release.Assets);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy asset .exe trong GitHub Release.");
            }

            Uri downloadUri;
            if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) ||
                !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Asset GitHub không có browser_download_url HTTPS hợp lệ.");
            }

            Version currentVersion = GetCurrentVersion();
            return new GitHubReleaseUpdateInfo
            {
                Version = latestVersion.ToString(),
                TagName = release.TagName,
                ReleaseNotes = release.Body,
                FileName = asset.Name,
                FileSize = asset.Size,
                DownloadUrl = asset.BrowserDownloadUrl,
                IsUpdateAvailable = latestVersion > currentVersion
            };
        }

        private static GitHubReleaseAsset SelectInstallerAsset(
            IEnumerable<GitHubReleaseAsset> assets)
        {
            if (assets == null)
            {
                return null;
            }

            List<GitHubReleaseAsset> exeAssets = assets
                .Where(asset =>
                    asset != null &&
                    !string.IsNullOrWhiteSpace(asset.Name) &&
                    string.Equals(
                        Path.GetExtension(asset.Name),
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            return exeAssets.FirstOrDefault(asset =>
                       asset.Name.StartsWith(
                           "SX3ScannerSetup",
                           StringComparison.OrdinalIgnoreCase))
                   ?? exeAssets.FirstOrDefault();
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
            {
                normalized = normalized.Substring(0, suffixIndex);
            }

            return Version.TryParse(normalized, out version);
        }

        private static void ValidateDownloadInfo(GitHubReleaseUpdateInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            if (string.IsNullOrWhiteSpace(info.FileName) ||
                !string.Equals(
                    Path.GetExtension(info.FileName),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Asset cập nhật không phải file .exe.");
            }

            Uri downloadUri;
            if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Link tải installer không hợp lệ.");
            }
        }

        private GitHubReleaseUpdateInfo HandleCheckError(
            string message,
            bool showErrors,
            Exception exception)
        {
            LastCheckSucceeded = false;
            LastStatusMessage = message;
            if (exception != null)
            {
                LogError("github-update-check", exception);
            }

            if (showErrors)
            {
                ShowError(message);
            }

            return null;
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var client = new HttpClient
            {
                Timeout = timeout
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static bool IsStartupCheckEnabled()
        {
            bool enabled;
            return !bool.TryParse(ConfigurationManager.AppSettings[EnabledSetting], out enabled) ||
                   enabled;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Không xóa được installer lỗi: " + ex.Message);
            }
        }

        private static void LogError(string key, Exception exception)
        {
            StartupManager.LogOnce(
                key + ":" + exception.GetType().FullName,
                "GitHub updater error: " + exception);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "SX3 Scanner - Lỗi cập nhật",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private sealed class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("body")]
            public string Body { get; set; }

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; }
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
