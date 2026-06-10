using Newtonsoft.Json;
using SX3_SCANER.Model;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
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
                Interval = TimeSpan.FromSeconds(30)
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
                string requestUrl = AnnouncementApiUrl + "?t=" +
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                using (HttpResponseMessage response =
                    await _httpClient.GetAsync(requestUrl).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine(
                            "Announcement request failed: " +
                            (int)response.StatusCode + " " + response.ReasonPhrase);
                        return;
                    }

                    string announcementJson =
                        await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

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

                    string fingerprint = BuildFingerprint(announcement);
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

                            _lastAnnouncementFingerprint = fingerprint;
                            AnnouncementChanged?.Invoke(this, announcement);
                        })
                        .Task.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Announcement error: " + ex);
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
            NormalizeMessages(announcement);
        }

        private static int NormalizePollSeconds(int pollSeconds)
        {
            return pollSeconds < 10 ? 30 : pollSeconds;
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

        private static string BuildFingerprint(AnnouncementInfo announcement)
        {
            var fingerprint = new StringBuilder();
            fingerprint.Append(announcement.Enabled).Append('\u001f')
                .Append(announcement.Mode ?? string.Empty).Append('\u001f')
                .Append(announcement.Level ?? string.Empty).Append('\u001f')
                .Append(announcement.Title ?? string.Empty).Append('\u001f')
                .Append(announcement.Message ?? string.Empty).Append('\u001f')
                .Append(announcement.UpdatedAt ?? string.Empty).Append('\u001f')
                .Append(announcement.AutoHideSeconds).Append('\u001f')
                .Append(announcement.ShowCountdown).Append('\u001f')
                .Append(announcement.AllowClose).Append('\u001f')
                .Append(announcement.RotateSeconds).Append('\u001f')
                .Append(announcement.BackgroundColor ?? string.Empty).Append('\u001f')
                .Append(announcement.ForegroundColor ?? string.Empty);

            foreach (AnnouncementMessageInfo message in announcement.Messages)
            {
                fingerprint.Append('\u001e')
                    .Append(message.Level ?? string.Empty).Append('\u001f')
                    .Append(message.Title ?? string.Empty).Append('\u001f')
                    .Append(message.Message ?? string.Empty).Append('\u001f')
                    .Append(message.BackgroundColor ?? string.Empty).Append('\u001f')
                    .Append(message.ForegroundColor ?? string.Empty).Append('\u001f')
                    .Append(message.AutoHideSeconds);
            }

            return fingerprint.ToString();
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
