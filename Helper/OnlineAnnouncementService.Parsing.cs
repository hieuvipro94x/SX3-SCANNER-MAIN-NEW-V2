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
    internal sealed partial class OnlineAnnouncementService
    {
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
                                "KhĂ´ng nháº­n Ä‘Æ°á»£c pháº£n há»“i realtime tá»« mĂ¡y chá»§ trong " +
                                Math.Max(1, (int)idleTimeout.TotalSeconds) +
                                " giĂ¢y.");
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
                ? "THĂ”NG BĂO Há»† THá»NG"
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
                    ? "THĂ”NG BĂO Há»† THá»NG"
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
                "THĂƒ",
                "SĂ¡Âº",
                "Ă¡Âº",
                "Ă¡Â»",
                "Ă„â€˜",
                "Ă„\u0090",
                "Ă†Â°",
                "Ă†Â¡",
                "ĂƒÂ´",
                "ĂƒÂ¡",
                "ĂƒÂ¢",
                "ĂƒÂª",
                "ĂƒÂ©",
                "ĂƒÂ¨",
                "Ă°Å¸"
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

            // App.config cÅ© cĂ³ thá»ƒ váº«n trá» tá»›i sx3-announcement.
            // Ă‰p vá» IP mĂ¡y chá»§ Ä‘ang dĂ¹ng Ä‘á»ƒ trĂ¡nh lá»‡ch Ä‘á»‹a chá»‰.
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
