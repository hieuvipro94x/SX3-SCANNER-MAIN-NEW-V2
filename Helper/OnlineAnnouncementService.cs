using Newtonsoft.Json;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SX3_SCANER.Helper
{
    internal sealed class OnlineAnnouncementService : IDisposable
    {
        private enum AnnouncementSource
        {
            Cache,
            Tailscale,
            Realtime
        }

        private const string AnnouncementServerHost = "100.72.125.42";
        private const string LegacyAnnouncementHost = "sx3-announcement";

        private const string DefaultRealtimeUrl =
            "ws://100.72.125.42:5055/ws/announcements";
        private const string DefaultSnapshotUrl =
            "http://100.72.125.42:5055/api/announcements/current";

        private readonly HttpClient _httpClient;
        private readonly Dispatcher _dispatcher;
        private readonly CancellationTokenSource _lifetimeCts =
            new CancellationTokenSource();
        private readonly string _realtimeUrl;
        private readonly string _snapshotUrl;
        private readonly string _cachePath;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _connectionTimeout;

        // Tự kết nối lại khi máy chủ WebSocket/HTTP bị mất kết nối.
        // Có thể chỉnh trong App.config:
        // AnnouncementReconnectInitialSeconds=2
        // AnnouncementReconnectMaximumSeconds=30
        // AnnouncementRealtimeIdleSeconds=90
        private readonly TimeSpan _reconnectInitialDelay;
        private readonly TimeSpan _reconnectMaximumDelay;
        private readonly TimeSpan _realtimeIdleTimeout;

        // Chống bắn thông báo quá dày làm UI nháy/giật.
        // Có thể chỉnh trong App.config: AnnouncementMinimumApplyMilliseconds=180
        private readonly TimeSpan _minimumApplyInterval;

        private readonly object _syncRoot = new object();

        private bool _isStarted;
        private bool _isDisposed;
        private string _lastAnnouncementFingerprint;
        private string _lastDisplayFingerprint;
        private string _lastAnnouncementVersion = string.Empty;
        private string _lastAnnouncementUpdatedAt = string.Empty;
        private bool? _lastAnnouncementEnabled;
        private DateTime _lastAppliedUtc = DateTime.MinValue;
        private AnnouncementServerStatusInfo _currentConnectionStatus;

        public OnlineAnnouncementService()
        {
            _dispatcher = System.Windows.Application.Current?.Dispatcher ??
                Dispatcher.CurrentDispatcher;
            _realtimeUrl = ReadAnnouncementUrlSetting(
                "AnnouncementPrimaryWebSocketUrl",
                DefaultRealtimeUrl);
            _snapshotUrl = ReadAnnouncementUrlSetting(
                "AnnouncementPrimaryHttpUrl",
                DefaultSnapshotUrl);
            _pollInterval = TimeSpan.FromSeconds(
                ReadPositiveIntSetting("AnnouncementPollSeconds", 60));
            _connectionTimeout = TimeSpan.FromSeconds(
                ReadPositiveIntSetting(
                    "AnnouncementHttpTimeoutSeconds",
                    3));
            _reconnectInitialDelay = TimeSpan.FromSeconds(
                ReadPositiveIntSetting(
                    "AnnouncementReconnectInitialSeconds",
                    2));
            _reconnectMaximumDelay = TimeSpan.FromSeconds(
                ReadPositiveIntSetting(
                    "AnnouncementReconnectMaximumSeconds",
                    30));
            _realtimeIdleTimeout = TimeSpan.FromSeconds(
                ReadPositiveIntSetting(
                    "AnnouncementRealtimeIdleSeconds",
                    90));
            _minimumApplyInterval = TimeSpan.FromMilliseconds(
                ReadPositiveIntSetting(
                    "AnnouncementMinimumApplyMilliseconds",
                    180));
            _cachePath = Path.Combine(
                DatabaseRepository.AppDataDirectory,
                "cache",
                "announcement-cache.json");

            _httpClient = new HttpClient
            {
                Timeout = _connectionTimeout
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SX3Scanner");
        }

        public event EventHandler<AnnouncementInfo> AnnouncementChanged;

        // ViewModel/UI có thể bắt event này để đổi text/màu trạng thái ngay khi
        // đang kết nối, đã kết nối hoặc mất kết nối máy chủ.
        public event EventHandler<AnnouncementServerStatusInfo> ConnectionStatusChanged;

        public AnnouncementServerStatusInfo CurrentConnectionStatus
        {
            get { return _currentConnectionStatus; }
        }

        public void Start()
        {
            if (_isDisposed || _isStarted)
                return;

            _isStarted = true;
            _ = RunAsync(_lifetimeCts.Token);
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                await LoadCachedAnnouncementAsync().ConfigureAwait(false);
                await RunRealtimeLoopAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[Announcement] Client loop stopped unexpectedly. " + ex);
            }
        }

        private async Task RunRealtimeLoopAsync(CancellationToken token)
        {
            int reconnectAttempt = 0;

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_realtimeUrl))
                        throw new InvalidOperationException(
                            "Announcement WebSocket URL is empty.");

                    using (var socket = new ClientWebSocket())
                    {
                        ApplyServerStatus(
                            AnnouncementServerStatusInfo.Connecting(
                                "Tailscale WebSocket"));

                        socket.Options.KeepAliveInterval =
                            TimeSpan.FromSeconds(20);

                        using (var connectCts =
                            CancellationTokenSource.CreateLinkedTokenSource(
                                token))
                        {
                            connectCts.CancelAfter(_connectionTimeout);
                            await socket.ConnectAsync(
                                new Uri(_realtimeUrl),
                                connectCts.Token).ConfigureAwait(false);
                        }

                        reconnectAttempt = 0;
                        ApplyServerStatus(
                            AnnouncementServerStatusInfo.Connected(
                                "Tailscale WebSocket"));

                        StartupManager.Log(
                            "[Announcement] Using Tailscale server");

                        // Lấy snapshot ban đầu nhưng không để HTTP ghi đè trạng thái
                        // WebSocket đang Connected trên UI.
                        await LoadSnapshotAsync(
                            token,
                            updateStatus: false).ConfigureAwait(false);

                        while (socket.State == WebSocketState.Open &&
                               !token.IsCancellationRequested)
                        {
                            string json = await ReceiveTextAsync(
                                socket,
                                token,
                                _realtimeIdleTimeout).ConfigureAwait(false);
                            if (json == null)
                                throw new WebSocketException(
                                    "Máy chủ đã đóng kết nối realtime.");

                            StartupManager.Log(
                                "[Announcement] Realtime payload received.");
                            await ProcessAnnouncementJsonAsync(
                                json,
                                saveCache: true,
                                source: AnnouncementSource.Realtime,
                                token: token)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ApplyServerStatus(
                        AnnouncementServerStatusInfo.Failed(
                            "Tailscale WebSocket",
                            "Mất kết nối máy chủ. Đang tự kết nối lại... " +
                            ex.Message));

                    Debug.WriteLine(
                        "[Announcement] WebSocket unavailable, reconnecting. " +
                        ex.Message);
                    StartupManager.Log(
                        "[Announcement] WebSocket unavailable, reconnecting. " +
                        ex.Message);
                }

                // Trong lúc chờ WebSocket kết nối lại, vẫn thử lấy dữ liệu bằng HTTP
                // để UI không bị trống và trạng thái kết nối được cập nhật.
                try
                {
                    await LoadSnapshotAsync(
                        token,
                        updateStatus: true).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    StartupManager.Log(
                        "[Announcement] Snapshot fallback failed. " +
                        ex.Message);
                }

                TimeSpan delay = GetReconnectDelay(reconnectAttempt++);
                ApplyServerStatus(
                    AnnouncementServerStatusInfo.Connecting(
                        "Tự kết nối lại sau " +
                        Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)) +
                        " giây"));

                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task LoadCachedAnnouncementAsync()
        {
            try
            {
                if (!File.Exists(_cachePath))
                    return;

                string json = await Task.Run(
                    () => File.ReadAllText(_cachePath, Encoding.UTF8))
                    .ConfigureAwait(false);
                await ProcessAnnouncementJsonAsync(
                    json,
                    saveCache: false,
                    source: AnnouncementSource.Cache).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[Announcement] Invalid cached encoding, cache ignored. " +
                    ex.Message);
                TryDeleteCache();
            }
        }

        public Task LoadSnapshotAsync()
        {
            return LoadSnapshotAsync(
                _lifetimeCts.Token,
                updateStatus: true);
        }

        private async Task LoadSnapshotAsync(CancellationToken token)
        {
            await LoadSnapshotAsync(token, updateStatus: true)
                .ConfigureAwait(false);
        }

        private async Task LoadSnapshotAsync(
            CancellationToken token,
            bool updateStatus)
        {
            if (_isDisposed || string.IsNullOrWhiteSpace(_snapshotUrl))
                return;

            if (await TryLoadHttpAsync(
                _snapshotUrl,
                AnnouncementSource.Tailscale,
                token,
                updateStatus).ConfigureAwait(false))
            {
                if (updateStatus)
                {
                    ApplyServerStatus(
                        AnnouncementServerStatusInfo.Connected(
                            "Tailscale HTTP"));
                }

                StartupManager.Log(
                    "[Announcement] Using Tailscale server");
                return;
            }

            if (updateStatus)
            {
                ApplyServerStatus(
                    AnnouncementServerStatusInfo.Failed(
                        "Announcement Server",
                        "Không kết nối được máy chủ announcement, đang dùng cache nếu có. Đang tự kết nối lại..."));
            }

            StartupManager.Log(
                "[Announcement] Server unavailable, using cached announcement");
            await LoadCachedAnnouncementAsync().ConfigureAwait(false);
        }

        private async Task<bool> TryLoadHttpAsync(
            string url,
            AnnouncementSource source,
            CancellationToken token,
            bool updateStatus)
        {
            try
            {
                using (HttpResponseMessage response =
                    await _httpClient.GetAsync(url, token)
                        .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (updateStatus)
                        {
                            ApplyServerStatus(
                                AnnouncementServerStatusInfo.Failed(
                                    source.ToString(),
                                    "HTTP " + (int)response.StatusCode));
                        }

                        StartupManager.Log(
                            "[Announcement] HTTP " +
                            (int)response.StatusCode + " from " + url);
                        return false;
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync()
                        .ConfigureAwait(false);
                    string json = Encoding.UTF8.GetString(bytes);

                    AnnouncementInfo parsed;
                    try
                    {
                        parsed = ParseAnnouncement(json);
                    }
                    catch (Exception ex)
                    {
                        LogInvalidPayload(source, ex.Message);
                        return false;
                    }

                    if (ContainsInvalidEncoding(json) ||
                        ContainsInvalidEncoding(parsed))
                    {
                        LogInvalidPayload(source, null);
                        return false;
                    }

                    // Không parse JSON lần 2 nữa. Giảm tải khi polling/realtime dày.
                    return await ProcessAnnouncementAsync(
                        parsed,
                        json,
                        saveCache: true,
                        source: source,
                        token: token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (updateStatus)
                {
                    ApplyServerStatus(
                        AnnouncementServerStatusInfo.Failed(
                            source.ToString(),
                            ex.Message));
                }
                Debug.WriteLine(
                    "[Announcement] HTTP unavailable: " + ex.Message);
                StartupManager.Log(
                    "[Announcement] HTTP unavailable from " + url + ". " +
                    ex.Message);
                return false;
            }
        }

        private async Task<bool> ProcessAnnouncementJsonAsync(
            string json,
            bool saveCache,
            AnnouncementSource source,
            CancellationToken token = default(CancellationToken))
        {
            AnnouncementInfo announcement;
            try
            {
                announcement = ParseAnnouncement(json);
            }
            catch (Exception ex)
            {
                LogInvalidPayload(source, ex.Message);
                return false;
            }

            if (ContainsInvalidEncoding(json) ||
                ContainsInvalidEncoding(announcement))
            {
                LogInvalidPayload(source, null);
                return false;
            }

            return await ProcessAnnouncementAsync(
                announcement,
                json,
                saveCache,
                source,
                token).ConfigureAwait(false);
        }

        private async Task<bool> ProcessAnnouncementAsync(
            AnnouncementInfo announcement,
            string rawJson,
            bool saveCache,
            AnnouncementSource source,
            CancellationToken token)
        {
            string displayFingerprint = BuildDisplayFingerprint(announcement);

            // So sánh theo nội dung hiển thị, không phụ thuộc UpdatedAt/Version.
            // Tránh tình trạng server đổi timestamp nhưng message không đổi làm UI chạy lại animation.
            if (!HasAnnouncementChanged(announcement, displayFingerprint))
            {
                return false;
            }

            await WaitForSmoothApplyWindowAsync(token).ConfigureAwait(false);

            // Sau khi debounce, kiểm tra lại để tránh payload trùng vừa được apply.
            if (!HasAnnouncementChanged(announcement, displayFingerprint))
            {
                return false;
            }

            if (saveCache)
            {
                await SaveCacheAsync(rawJson).ConfigureAwait(false);
            }

            await _dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed)
                    return;

                lock (_syncRoot)
                {
                    _lastAnnouncementFingerprint = displayFingerprint;
                    _lastDisplayFingerprint = displayFingerprint;
                    _lastAnnouncementVersion = announcement.Version;
                    _lastAnnouncementUpdatedAt = announcement.UpdatedAt;
                    _lastAnnouncementEnabled = announcement.Enabled;
                    _lastAppliedUtc = DateTime.UtcNow;
                }

                StartupManager.Log(
                    "[Announcement] Applied " +
                    source.ToString().ToLowerInvariant() +
                    " announcement. Enabled=" + announcement.Enabled +
                    ", Version=" + announcement.Version +
                    ", UpdatedAt=" + announcement.UpdatedAt);

                AnnouncementChanged?.Invoke(this, announcement);
            }, DispatcherPriority.Background).Task.ConfigureAwait(false);

            return true;
        }

        private async Task SaveCacheAsync(string json)
        {
            try
            {
                await Task.Run(() =>
                {
                    string directory = Path.GetDirectoryName(_cachePath);
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(
                        _cachePath,
                        json ?? string.Empty,
                        new UTF8Encoding(false));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[Announcement] Could not save cache. " + ex.Message);
            }
        }

        private async Task WaitForSmoothApplyWindowAsync(CancellationToken token)
        {
            int delayMilliseconds = 0;

            lock (_syncRoot)
            {
                if (_lastAppliedUtc != DateTime.MinValue)
                {
                    TimeSpan elapsed = DateTime.UtcNow - _lastAppliedUtc;
                    if (elapsed < _minimumApplyInterval)
                    {
                        delayMilliseconds =
                            (int)Math.Ceiling(
                                (_minimumApplyInterval - elapsed)
                                .TotalMilliseconds);
                    }
                }
            }

            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, token)
                    .ConfigureAwait(false);
            }
        }

        private bool HasAnnouncementChanged(
            AnnouncementInfo announcement,
            string displayFingerprint)
        {
            lock (_syncRoot)
            {
                if (!_lastAnnouncementEnabled.HasValue)
                    return true;

                if (_lastAnnouncementEnabled.Value != announcement.Enabled)
                    return true;

                return !string.Equals(
                    _lastDisplayFingerprint,
                    displayFingerprint,
                    StringComparison.Ordinal);
            }
        }

        private static string BuildDisplayFingerprint(
            AnnouncementInfo announcement)
        {
            var builder = new StringBuilder();

            AppendPart(builder, announcement.Enabled ? "1" : "0");
            AppendPart(builder, announcement.Level);
            AppendPart(builder, announcement.Mode);
            AppendPart(builder, announcement.Title);
            AppendPart(builder, announcement.Message);
            AppendPart(builder, announcement.BackgroundColor);
            AppendPart(builder, announcement.ForegroundColor);
            AppendPart(builder, announcement.CreatedBy);
            AppendPart(builder, announcement.AutoHideSeconds.ToString());
            AppendPart(builder, announcement.Priority.ToString());
            AppendPart(builder, announcement.RotateSeconds.ToString());
            AppendPart(builder, announcement.RepeatSeconds.ToString());
            AppendPart(builder, announcement.MarqueeDirection);
            AppendPart(builder, announcement.MarqueeSpeed.ToString());
            AppendPart(builder, announcement.MarqueeDelaySeconds.ToString());

            if (announcement.Messages != null)
            {
                AppendPart(builder, announcement.Messages.Count.ToString());
                foreach (AnnouncementMessageInfo message in announcement.Messages)
                {
                    if (message == null)
                    {
                        AppendPart(builder, string.Empty);
                        continue;
                    }

                    AppendPart(builder, message.Level);
                    AppendPart(builder, message.Title);
                    AppendPart(builder, message.Message);
                    AppendPart(builder, message.BackgroundColor);
                    AppendPart(builder, message.ForegroundColor);
                    AppendPart(
                        builder,
                        message.AutoHideSeconds.HasValue
                            ? message.AutoHideSeconds.Value.ToString()
                            : string.Empty);
                }
            }
            else
            {
                AppendPart(builder, "0");
            }

            return BuildFingerprint(builder.ToString());
        }

        private static void AppendPart(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private static AnnouncementInfo ParseAnnouncement(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Announcement JSON is empty.");

            string trimmed = json.TrimStart();
            if (trimmed.StartsWith("<") || trimmed.StartsWith("```"))
                throw new JsonException("Announcement response is not JSON.");

            AnnouncementInfo announcement =
                JsonConvert.DeserializeObject<AnnouncementInfo>(json);
            if (announcement == null)
                throw new JsonException("Announcement JSON is empty.");

            NormalizeAnnouncement(announcement);
            return announcement;
        }

        private static async Task<string> ReceiveTextAsync(
            ClientWebSocket socket,
            CancellationToken token,
            TimeSpan idleTimeout)
        {
            var buffer = new byte[8192];
            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    using (var receiveCts =
                        CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        receiveCts.CancelAfter(idleTimeout);

                        WebSocketReceiveResult result;
                        try
                        {
                            result = await socket.ReceiveAsync(
                                new ArraySegment<byte>(buffer),
                                receiveCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (!token.IsCancellationRequested)
                        {
                            throw new TimeoutException(
                                "Không nhận được phản hồi realtime từ máy chủ trong " +
                                Math.Max(1, (int)idleTimeout.TotalSeconds) +
                                " giây.");
                        }

                        if (result.MessageType == WebSocketMessageType.Close)
                            return null;
                        if (result.MessageType != WebSocketMessageType.Text)
                            continue;

                        stream.Write(buffer, 0, result.Count);
                        if (result.EndOfMessage)
                            return Encoding.UTF8.GetString(stream.ToArray());
                    }
                }
            }
        }

        private TimeSpan GetReconnectDelay(int attempt)
        {
            if (attempt < 0)
                attempt = 0;

            double multiplier = Math.Pow(2, Math.Min(attempt, 6));
            double seconds = _reconnectInitialDelay.TotalSeconds * multiplier;
            seconds = Math.Min(seconds, _reconnectMaximumDelay.TotalSeconds);

            return TimeSpan.FromSeconds(Math.Max(1, seconds));
        }

        private void ApplyServerStatus(AnnouncementServerStatusInfo status)
        {
            if (_isDisposed)
                return;

            if (_dispatcher.CheckAccess())
            {
                ApplyServerStatusOnUiThread(status);
                return;
            }

            _dispatcher.BeginInvoke(
                new Action(() => ApplyServerStatusOnUiThread(status)),
                DispatcherPriority.Background);
        }

        private void ApplyServerStatusOnUiThread(
            AnnouncementServerStatusInfo status)
        {
            if (_isDisposed)
                return;

            _currentConnectionStatus = status;
            StartupManager.SetAnnouncementServerStatus(status);
            ConnectionStatusChanged?.Invoke(this, status);
        }

        private static void NormalizeAnnouncement(AnnouncementInfo announcement)
        {
            announcement.Level = NormalizeLevel(announcement.Level);
            announcement.Mode = string.IsNullOrWhiteSpace(announcement.Mode)
                ? "single"
                : announcement.Mode.Trim().ToLowerInvariant();
            announcement.Title = string.IsNullOrWhiteSpace(announcement.Title)
                ? "THÔNG BÁO HỆ THỐNG"
                : announcement.Title.Trim();
            announcement.Message =
                announcement.Message?.Trim() ?? string.Empty;
            announcement.UpdatedAt =
                announcement.UpdatedAt?.Trim() ?? string.Empty;
            announcement.Version =
                announcement.Version?.Trim() ?? string.Empty;
            announcement.BackgroundColor =
                announcement.BackgroundColor?.Trim() ?? string.Empty;
            announcement.ForegroundColor =
                announcement.ForegroundColor?.Trim() ?? string.Empty;
            announcement.CreatedBy =
                announcement.CreatedBy?.Trim() ?? string.Empty;
            announcement.AutoHideSeconds =
                Math.Max(0, announcement.AutoHideSeconds);
            announcement.Priority = Math.Max(0, announcement.Priority);
            announcement.RotateSeconds =
                announcement.RotateSeconds < 3
                    ? 10
                    : announcement.RotateSeconds;
            announcement.RepeatSeconds =
                Math.Max(0, announcement.RepeatSeconds);
            announcement.MarqueeDirection = string.Equals(
                announcement.MarqueeDirection,
                "leftToRight",
                StringComparison.OrdinalIgnoreCase)
                    ? "leftToRight"
                    : "rightToLeft";
            announcement.MarqueeSpeed =
                announcement.MarqueeSpeed <= 0
                    ? 80
                    : announcement.MarqueeSpeed;
            announcement.MarqueeDelaySeconds =
                Math.Max(0, announcement.MarqueeDelaySeconds);
            NormalizeMessages(announcement);
        }

        private static void NormalizeMessages(AnnouncementInfo announcement)
        {
            if (announcement.Messages == null)
            {
                announcement.Messages =
                    new System.Collections.Generic.List<AnnouncementMessageInfo>();
                return;
            }

            for (int index = announcement.Messages.Count - 1;
                 index >= 0;
                 index--)
            {
                AnnouncementMessageInfo message =
                    announcement.Messages[index];
                if (message == null ||
                    string.IsNullOrWhiteSpace(message.Message))
                {
                    announcement.Messages.RemoveAt(index);
                    continue;
                }

                message.Level = NormalizeLevel(message.Level);
                message.Title = string.IsNullOrWhiteSpace(message.Title)
                    ? "THÔNG BÁO HỆ THỐNG"
                    : message.Title.Trim();
                message.Message = message.Message.Trim();
                message.BackgroundColor =
                    message.BackgroundColor?.Trim() ?? string.Empty;
                message.ForegroundColor =
                    message.ForegroundColor?.Trim() ?? string.Empty;
                if (message.AutoHideSeconds.HasValue)
                {
                    message.AutoHideSeconds = Math.Max(
                        0,
                        message.AutoHideSeconds.Value);
                }
            }
        }

        private static string NormalizeLevel(string level)
        {
            string normalized =
                (level ?? string.Empty).Trim().ToLowerInvariant();
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

        private static bool ContainsInvalidEncoding(
            AnnouncementInfo announcement)
        {
            if (announcement == null)
                return true;

            if (ContainsInvalidEncoding(announcement.Title) ||
                ContainsInvalidEncoding(announcement.Message))
            {
                return true;
            }

            if (announcement.Messages == null)
                return false;

            foreach (AnnouncementMessageInfo message in announcement.Messages)
            {
                if (message != null &&
                    (ContainsInvalidEncoding(message.Title) ||
                     ContainsInvalidEncoding(message.Message)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsInvalidEncoding(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string[] markers =
            {
                "\uFFFD",
                "THÃ",
                "Sáº",
                "áº",
                "á»",
                "Ä‘",
                "Ä\u0090",
                "Æ°",
                "Æ¡",
                "Ã´",
                "Ã¡",
                "Ã¢",
                "Ãª",
                "Ã©",
                "Ã¨",
                "ðŸ"
            };

            foreach (char character in value)
            {
                if (character >= '\u0080' && character <= '\u009F')
                    return true;
            }

            foreach (string marker in markers)
            {
                if (value.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        private void LogInvalidPayload(
            AnnouncementSource source,
            string detail)
        {
            if (source == AnnouncementSource.Cache)
            {
                StartupManager.Log(
                    "[Announcement] Invalid cached encoding, cache ignored." +
                    (string.IsNullOrWhiteSpace(detail)
                        ? string.Empty
                        : " " + detail));
                TryDeleteCache();
                return;
            }

            StartupManager.Log(
                "[Announcement] Invalid " +
                source.ToString().ToLowerInvariant() +
                " encoding, payload ignored." +
                (string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : " " + detail));
        }

        private void TryDeleteCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                    File.Delete(_cachePath);
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "[Announcement] Could not delete invalid cache. " +
                    ex.Message);
            }
        }

        private static string BuildFingerprint(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static string ReadAnnouncementUrlSetting(
            string key,
            string defaultValue)
        {
            string value = ReadSetting(key, defaultValue);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
                return defaultValue;

            // App.config cũ có thể vẫn trỏ tới sx3-announcement.
            // Ép về IP máy chủ đang dùng để tránh lệch địa chỉ.
            if (string.Equals(
                    uri.Host,
                    LegacyAnnouncementHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri)
                {
                    Host = AnnouncementServerHost
                };
                return builder.Uri.ToString();
            }

            return value.Trim();
        }

        private static string ReadSetting(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value)
                ? defaultValue
                : value.Trim();
        }

        private static int ReadPositiveIntSetting(string key, int defaultValue)
        {
            int value;
            return int.TryParse(
                       ConfigurationManager.AppSettings[key],
                       out value) &&
                   value > 0
                ? value
                : defaultValue;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            _httpClient.Dispose();
        }
    }
}
