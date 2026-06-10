using Newtonsoft.Json;
using SX3_SCANER.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SX3_SCANER.Helper
{
    internal sealed class OnlineAnnouncementService : IDisposable
    {
        private const string AnnouncementApiUrl =
            "https://raw.githubusercontent.com/hieuvipro94x/sx3-scanner-release/main/announcement.json";

        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dispatcher _dispatcher;
        private int _isChecking;
        private bool _isStarted;
        private bool _isDisposed;
        private string _lastAnnouncementFingerprint;

        public OnlineAnnouncementService()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SX3Scanner");
            _httpClient.DefaultRequestHeaders.CacheControl =
                new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        public event EventHandler<AnnouncementInfo> AnnouncementChanged;

        public async void Start()
        {
            if (_isDisposed || _isStarted)
            {
                return;
            }

            _isStarted = true;
            await LoadAnnouncementAsync();

            if (!_isDisposed)
            {
                _refreshTimer.Start();
            }
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            await LoadAnnouncementAsync();
        }

        public async Task LoadAnnouncementAsync()
        {
            if (_isDisposed ||
                Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
            {
                return;
            }

            try
            {
                Debug.WriteLine("[Announcement] Downloading configuration...");

                string requestUrl = AnnouncementApiUrl + "?t=" +
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                Debug.WriteLine(
                    "[Announcement] URL = " + requestUrl);

                StartupManager.Log(
                    "[Announcement] URL = " + requestUrl);

                using (HttpResponseMessage response =
                    await _httpClient.GetAsync(requestUrl).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        LogNetworkUnavailable();
                        return;
                    }

                    string announcementJson =
                        await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

                    Debug.WriteLine(
                        "========== ANNOUNCEMENT RAW BEGIN ==========");

                    Debug.WriteLine(announcementJson);

                    Debug.WriteLine(
                        "========== ANNOUNCEMENT RAW END ==========");

                    StartupManager.Log(
                        "ANNOUNCEMENT RAW BEGIN\r\n" +
                        announcementJson +
                        "\r\nANNOUNCEMENT RAW END");

                    try
                    {
                        string desktopFile =
                            Path.Combine(
                                Environment.GetFolderPath(
                                    Environment.SpecialFolder.Desktop),
                                "announcement_debug.txt");

                        File.WriteAllText(
                            desktopFile,
                            announcementJson);
                    }
                    catch
                    {
                    }

                    string trimmed =
                        announcementJson.TrimStart();

                    if (trimmed.StartsWith("<"))
                    {
                        throw new JsonException(
                            "GitHub returned HTML instead of JSON.");
                    }

                    if (trimmed.StartsWith("```"))
                    {
                        throw new JsonException(
                            "GitHub JSON contains markdown code fences.");
                    }

                    AnnouncementInfo announcement =
                        JsonConvert.DeserializeObject<AnnouncementInfo>(
                            announcementJson);

                    if (announcement == null)
                    {
                        throw new JsonException(
                            "Decoded announcement JSON is empty.");
                    }

                    NormalizeAnnouncement(announcement);

                    if (_isDisposed)
                    {
                        return;
                    }

                    string fingerprint = BuildFingerprint(announcementJson);
                    bool announcementChanged =
                        !string.Equals(
                            _lastAnnouncementFingerprint,
                            fingerprint,
                            StringComparison.Ordinal);

                    await _dispatcher.InvokeAsync(
                        () =>
                        {
                            if (_isDisposed)
                            {
                                return;
                            }

                            _refreshTimer.Interval =
                                TimeSpan.FromSeconds(announcement.PollSeconds);

                            if (!announcementChanged)
                            {
                                return;
                            }

                            Debug.WriteLine(
                                "[Announcement] Configuration changed.");
                            _lastAnnouncementFingerprint = fingerprint;
                            AnnouncementChanged?.Invoke(this, announcement);
                        })
                        .Task.ConfigureAwait(false);
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(
                    "[Announcement] Invalid JSON.");

                Debug.WriteLine(ex);

                StartupManager.Log(
                    "[Announcement] Invalid JSON. " +
                    ex);

                return;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(
                    "[Announcement] Network unavailable.");

                StartupManager.Log(
                    "[Announcement] Network unavailable. " +
                    ex);

                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[Announcement] Unexpected error.");

                StartupManager.Log(
                    "[Announcement] Unexpected error. " +
                    ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        }

        private static void NormalizeAnnouncement(AnnouncementInfo announcement)
        {
            announcement.Level = NormalizeLevel(announcement.Level);
            announcement.Mode =
                string.IsNullOrWhiteSpace(announcement.Mode)
                    ? "single"
                    : announcement.Mode.Trim().ToLowerInvariant();
            announcement.Title =
                string.IsNullOrWhiteSpace(announcement.Title)
                    ? "THÔNG BÁO HỆ THỐNG"
                    : announcement.Title.Trim();
            announcement.Message = announcement.Message?.Trim() ?? string.Empty;
            announcement.UpdatedAt = announcement.UpdatedAt?.Trim() ?? string.Empty;
            announcement.Version = announcement.Version?.Trim() ?? string.Empty;
            announcement.BackgroundColor = announcement.BackgroundColor?.Trim() ?? string.Empty;
            announcement.ForegroundColor = announcement.ForegroundColor?.Trim() ?? string.Empty;
            announcement.CreatedBy = announcement.CreatedBy?.Trim() ?? string.Empty;
            announcement.AutoHideSeconds = Math.Max(0, announcement.AutoHideSeconds);
            announcement.Priority = Math.Max(0, announcement.Priority);
            announcement.PollSeconds = NormalizePollSeconds(announcement.PollSeconds);
            announcement.RotateSeconds = NormalizeRotateSeconds(announcement.RotateSeconds);
            announcement.RepeatSeconds = Math.Max(0, announcement.RepeatSeconds);
            announcement.MarqueeDirection =
                string.Equals(
                    announcement.MarqueeDirection,
                    "leftToRight",
                    StringComparison.OrdinalIgnoreCase)
                    ? "leftToRight"
                    : "rightToLeft";
            announcement.MarqueeSpeed =
                announcement.MarqueeSpeed <= 0 ? 80 : announcement.MarqueeSpeed;
            announcement.MarqueeDelaySeconds =
                Math.Max(0, announcement.MarqueeDelaySeconds);
            NormalizeMessages(announcement);
        }

        private static int NormalizePollSeconds(int pollSeconds)
        {
            return pollSeconds < 5 ? 5 : pollSeconds;
        }

        private static int NormalizeRotateSeconds(int rotateSeconds)
        {
            return rotateSeconds < 3 ? 10 : rotateSeconds;
        }

        private static void NormalizeMessages(AnnouncementInfo announcement)
        {
            if (announcement.Messages == null)
            {
                announcement.Messages = new System.Collections.Generic.List<AnnouncementMessageInfo>();
                return;
            }

            for (int index = announcement.Messages.Count - 1; index >= 0; index--)
            {
                AnnouncementMessageInfo message = announcement.Messages[index];
                if (message == null || string.IsNullOrWhiteSpace(message.Message))
                {
                    announcement.Messages.RemoveAt(index);
                    continue;
                }

                message.Level = NormalizeLevel(message.Level);
                message.Title =
                    string.IsNullOrWhiteSpace(message.Title)
                        ? "THÔNG BÁO HỆ THỐNG"
                        : message.Title.Trim();
                message.Message = message.Message.Trim();
                message.BackgroundColor = message.BackgroundColor?.Trim() ?? string.Empty;
                message.ForegroundColor = message.ForegroundColor?.Trim() ?? string.Empty;
                if (message.AutoHideSeconds.HasValue)
                {
                    message.AutoHideSeconds = Math.Max(0, message.AutoHideSeconds.Value);
                }
            }
        }

        private static string BuildFingerprint(string announcementJson)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(announcementJson ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private void LogNetworkUnavailable()
        {
            Debug.WriteLine("[Announcement] Network unavailable.");
            if (!string.IsNullOrEmpty(_lastAnnouncementFingerprint))
            {
                Debug.WriteLine("[Announcement] Using cached configuration.");
            }
        }

        private static string NormalizeLevel(string level)
        {
            string normalized = (level ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "warning":
                case "error":
                case "success":
                    return normalized;
                default:
                    return "info";
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
            _httpClient.Dispose();
        }
    }
}
