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

        // Nguồn chính để kiểm tra update. File này tránh lỗi GitHub API 403 rate limit.
        // Tạo file tại repo sx3-scanner-release/main/version.json.
        private const string UpdateManifestUrl =
            "https://raw.githubusercontent.com/hieuvipro94x/sx3-scanner-release/main/version.json";

        // Chỉ dùng fallback khi version.json chưa có hoặc lỗi dữ liệu.
        private const string LatestReleaseApiUrl =
            "https://api.github.com/repos/hieuvipro94x/sx3-scanner-release/releases/latest";

        private const string UserAgent = "SX3Scanner-Updater";
        private const string EnabledSetting = "UpdateCheckOnStartup";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ManualRateLimitBackoff =
    TimeSpan.FromMinutes(15);

        private static readonly object StartupCheckLock = new object();
        private static bool _startupCheckStarted;
        private static bool _startupCheckFinished;
        private static GitHubReleaseUpdateInfo _startupCheckResult;

        private DateTime? _manualRateLimitBackoffUntilUtc;

        internal bool LastCheckSucceeded { get; private set; }

        internal string LastStatusMessage { get; private set; }

        internal async Task<GitHubReleaseUpdateInfo> CheckForUpdateAsync(bool showErrors)
        {
            if (!showErrors && !IsStartupCheckEnabled())
            {
                LastCheckSucceeded = true;
                LastStatusMessage = "Tự động cập nhật đã tắt.";
                LogUpdate("Startup update check skipped because UpdateCheckOnStartup=false.");
                return null;
            }

            if (!showErrors && !TryBeginStartupCheck())
            {
                LastCheckSucceeded = true;
                LastStatusMessage = _startupCheckResult == null
                    ? "Đã kiểm tra cập nhật lúc khởi động."
                    : "Có bản mới: V" + _startupCheckResult.Version;
                return _startupCheckResult;
            }

            try
            {
                LastCheckSucceeded = false;
                LastStatusMessage = "Không thể kiểm tra cập nhật.";

                GitHubReleaseUpdateInfo updateInfo = null;

                try
                {
                    updateInfo = await CheckManifestAsync();
                }
                catch (Exception manifestException)
                {
                    LogUpdate("Manifest check failed, fallback to GitHub API. " + manifestException.Message);
                    updateInfo = await CheckGitHubReleaseApiAsync(showErrors);
                }

                LastCheckSucceeded = true;

                if (updateInfo == null || !updateInfo.IsUpdateAvailable)
                {
                    LastStatusMessage = "Không có bản mới.";
                    SaveStartupResult(showErrors, null);
                    return null;
                }

                LastStatusMessage = "Có bản mới: V" + updateInfo.Version;
                SaveStartupResult(showErrors, updateInfo);
                return updateInfo;
            }
            catch (TaskCanceledException ex)
            {
                string message = ex.CancellationToken.IsCancellationRequested
                    ? "Đã hủy kiểm tra cập nhật."
                    : "Máy chủ cập nhật phản hồi quá thời gian.";
                return HandleCheckError(message, showErrors, ex);
            }
            catch (HttpRequestException ex)
            {
                return HandleCheckError(
                    "Không có Internet hoặc không kết nối được máy chủ cập nhật.",
                    showErrors,
                    ex);
            }
            catch (JsonException ex)
            {
                return HandleCheckError(
                    "Dữ liệu cập nhật trả về không hợp lệ.",
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
                    "Không thể kiểm tra cập nhật lúc này.",
                    showErrors,
                    ex);
            }
            finally
            {
                if (!showErrors)
                {
                    FinishStartupCheck();
                }
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
            string temporaryPath = installerPath + ".download";
            TryDelete(temporaryPath);

            using (var client = CreateHttpClient(DownloadTimeout, true))
            using (HttpResponseMessage response = await client.GetAsync(
                info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Không tải được bản cập nhật lúc này. Vui lòng thử lại sau.");
                }

                using (Stream source = await response.Content.ReadAsStreamAsync())
                using (var target = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await source.CopyToAsync(target);
                }
            }

            var downloadedFile = new FileInfo(temporaryPath);
            if (!downloadedFile.Exists || downloadedFile.Length <= 0)
            {
                TryDelete(temporaryPath);
                throw new InvalidOperationException(
                    "File cập nhật tải về không tồn tại hoặc có dung lượng bằng 0.");
            }

            if (info.FileSize > 0 && downloadedFile.Length != info.FileSize)
            {
                TryDelete(temporaryPath);
                throw new InvalidOperationException(
                    "Dung lượng file tải về không khớp với thông tin bản cập nhật.");
            }

            TryDelete(installerPath);
            File.Move(temporaryPath, installerPath);
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
                LastStatusMessage =
                    "Không tải được bản cập nhật lúc này. Vui lòng thử lại sau.";
                LogError("update-download-run", ex);
                Debug.WriteLine(ex.ToString());
                ShowError(LastStatusMessage);
                return false;
            }
        }

        internal static Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            Version parsedVersion;

            var informationalVersion =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informationalVersion != null &&
                TryParseVersion(informationalVersion.InformationalVersion, out parsedVersion))
            {
                return parsedVersion;
            }

            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVersion != null && TryParseVersion(fileVersion.Version, out parsedVersion))
            {
                return parsedVersion;
            }

            Version assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion != null)
            {
                return assemblyVersion;
            }

            throw new InvalidOperationException("Không xác định được phiên bản hiện tại của ứng dụng.");
        }

        private static bool TryBeginStartupCheck()
        {
            lock (StartupCheckLock)
            {
                if (_startupCheckFinished || _startupCheckStarted)
                {
                    return false;
                }

                _startupCheckStarted = true;
                return true;
            }
        }

        private static void FinishStartupCheck()
        {
            lock (StartupCheckLock)
            {
                _startupCheckStarted = false;
                _startupCheckFinished = true;
            }
        }

        private static void SaveStartupResult(bool showErrors, GitHubReleaseUpdateInfo info)
        {
            if (showErrors)
            {
                return;
            }

            lock (StartupCheckLock)
            {
                _startupCheckResult = info;
            }
        }

        private async Task<GitHubReleaseUpdateInfo> CheckManifestAsync()
        {
            string requestUrl = UpdateManifestUrl + "?t=" +
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using (var client = CreateHttpClient(RequestTimeout, false))
            using (HttpResponseMessage response = await client.GetAsync(requestUrl))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Manifest HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);
                }

                string json = await response.Content.ReadAsStringAsync();
                UpdateManifest manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                return BuildUpdateInfo(manifest);
            }
        }

        private async Task<GitHubReleaseUpdateInfo> CheckGitHubReleaseApiAsync(bool showErrors)
        {
            if (_manualRateLimitBackoffUntilUtc.HasValue &&
                DateTime.UtcNow < _manualRateLimitBackoffUntilUtc.Value)
            {
                throw new InvalidOperationException(
                    "GitHub API đang bị giới hạn, bỏ qua đến " +
                    _manualRateLimitBackoffUntilUtc.Value.ToString("O"));
            }

            using (var client = CreateHttpClient(RequestTimeout, true))
            using (HttpResponseMessage response = await client.GetAsync(LatestReleaseApiUrl))
            {
                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    (int)response.StatusCode == 429)
                {
                    _manualRateLimitBackoffUntilUtc =
                        GetRateLimitBackoffUntilUtc(response);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("GitHub chưa có bản phát hành nào.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (!showErrors)
                    {
                        return null;
                    }

                    throw new InvalidOperationException(
                        "Không thể kiểm tra cập nhật lúc này.");
                }

                string json = await response.Content.ReadAsStringAsync();
                GitHubRelease release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                return BuildUpdateInfo(release);
            }
        }

        private static DateTime GetRateLimitBackoffUntilUtc(
            HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    return response.Headers.RetryAfter.Date.Value.UtcDateTime;
                }

                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return DateTime.UtcNow.Add(
                        response.Headers.RetryAfter.Delta.Value);
                }
            }

            IEnumerable<string> resetValues;
            if (response.Headers.TryGetValues(
                    "X-RateLimit-Reset",
                    out resetValues))
            {
                long unixSeconds;
                if (long.TryParse(
                    resetValues.FirstOrDefault(),
                    out unixSeconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(
                        unixSeconds).UtcDateTime.AddSeconds(5);
                }
            }

            return DateTime.UtcNow.Add(ManualRateLimitBackoff);
        }

        private static GitHubReleaseUpdateInfo BuildUpdateInfo(UpdateManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidOperationException("version.json không có dữ liệu.");
            }

            Version latestVersion;
            if (!TryParseVersion(manifest.Version ?? manifest.TagName, out latestVersion))
            {
                throw new InvalidOperationException("version.json thiếu version hợp lệ.");
            }

            Uri downloadUri;
            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl) ||
                !Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("version.json thiếu downloadUrl HTTPS hợp lệ.");
            }

            string fileName = manifest.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = Path.GetFileName(downloadUri.LocalPath);
            }

            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("version.json downloadUrl không phải file .exe.");
            }

            Version currentVersion = GetCurrentVersion();
            bool isUpdateAvailable = IsNewerVersion(latestVersion, currentVersion);

            return new GitHubReleaseUpdateInfo
            {
                Version = latestVersion.ToString(),
                TagName = string.IsNullOrWhiteSpace(manifest.TagName)
                    ? "v" + latestVersion
                    : manifest.TagName.Trim(),
                ReleaseNotes = manifest.ReleaseNotes ?? string.Empty,
                FileName = fileName,
                FileSize = Math.Max(0, manifest.FileSize),
                DownloadUrl = manifest.DownloadUrl.Trim(),
                IsUpdateAvailable = isUpdateAvailable
            };
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

            Version currentVersion = GetCurrentVersion();
            bool isUpdateAvailable = IsNewerVersion(latestVersion, currentVersion);

            return new GitHubReleaseUpdateInfo
            {
                Version = latestVersion.ToString(),
                TagName = release.TagName,
                ReleaseNotes = release.Body ?? string.Empty,
                FileName = asset.Name,
                FileSize = asset.Size,
                DownloadUrl = asset.BrowserDownloadUrl,
                IsUpdateAvailable = isUpdateAvailable
            };
        }

        internal static bool IsNewerVersion(Version latestVersion, Version currentVersion)
        {
            if (latestVersion == null)
            {
                throw new ArgumentNullException("latestVersion");
            }

            if (currentVersion == null)
            {
                throw new ArgumentNullException("currentVersion");
            }

            return latestVersion.CompareTo(currentVersion) > 0;
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
                    !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
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
            LastStatusMessage =
                "Không kiểm tra được cập nhật lúc này. Vui lòng thử lại sau.";
            LogUpdate("Update check failed: " + message);
            if (exception != null)
            {
                Debug.WriteLine(exception.ToString());
            }

            if (showErrors)
            {
                ShowError(LastStatusMessage);
            }

            return null;
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout, bool acceptGitHubJson)
        {
            var client = new HttpClient
            {
                Timeout = timeout
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            if (acceptGitHubJson)
            {
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            }

            return client;
        }

        private static bool IsStartupCheckEnabled()
        {
            bool enabled;
            string configuredValue = ConfigurationManager.AppSettings[EnabledSetting];
            if (!bool.TryParse(configuredValue, out enabled))
            {
                return true;
            }

            return enabled;
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
                Debug.WriteLine("Không xóa được file tạm: " + ex.Message);
            }
        }

        private static void LogError(string key, Exception exception)
        {
            StartupManager.LogOnce(
                key + ":" + exception.GetType().FullName,
                "GitHub updater error: " + exception);
        }

        private static void LogUpdate(string message)
        {
            string logMessage = "GitHub updater: " + message;
            Debug.WriteLine(logMessage);
            StartupManager.Log(logMessage);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "SX3 Scanner - Lỗi cập nhật",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private sealed class UpdateManifest
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("tagName")]
            public string TagName { get; set; }

            [JsonProperty("releaseNotes")]
            public string ReleaseNotes { get; set; }

            [JsonProperty("fileName")]
            public string FileName { get; set; }

            [JsonProperty("fileSize")]
            public long FileSize { get; set; }

            [JsonProperty("downloadUrl")]
            public string DownloadUrl { get; set; }
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
