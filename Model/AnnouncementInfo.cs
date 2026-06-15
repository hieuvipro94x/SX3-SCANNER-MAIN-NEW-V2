using System.Collections.Generic;

namespace SX3_SCANER.Model
{
    internal sealed class AnnouncementInfo
    {
        public bool Enabled { get; set; }

        public string Mode { get; set; } = "single";

        public string Level { get; set; } = "info";

        public string Title { get; set; } = "THÔNG BÁO HỆ THỐNG";

        public string Message { get; set; } = string.Empty;

        public string UpdatedAt { get; set; } = string.Empty;

        public int AutoHideSeconds { get; set; }

        public bool ShowCountdown { get; set; }

        public int PollSeconds { get; set; } = 5;

        public int RotateSeconds { get; set; } = 10;

        public int RepeatSeconds { get; set; } = 600;

        public bool MarqueeEnabled { get; set; }

        public string MarqueeDirection { get; set; } = "rightToLeft";

        public int MarqueeSpeed { get; set; } = 80;

        public int MarqueeDelaySeconds { get; set; } = 10;

        public List<AnnouncementMessageInfo> Messages { get; set; } =
            new List<AnnouncementMessageInfo>();

        public bool ShowPopup { get; set; }

        public bool AllowClose { get; set; }

        public string Version { get; set; } = string.Empty;

        public bool ForceUpdate { get; set; }

        public int Priority { get; set; }

        public string BackgroundColor { get; set; } = string.Empty;

        public string ForegroundColor { get; set; } = string.Empty;

        public string CreatedBy { get; set; } = string.Empty;

        // Trạng thái kết nối máy chủ thông báo online
        public AnnouncementServerStatusInfo ServerStatus { get; set; } =
            AnnouncementServerStatusInfo.Unknown();
    }

    internal sealed class AnnouncementMessageInfo
    {
        public string Level { get; set; } = "info";

        public string Title { get; set; } = "THÔNG BÁO HỆ THỐNG";

        public string Message { get; set; } = string.Empty;

        public string BackgroundColor { get; set; } = string.Empty;

        public string ForegroundColor { get; set; } = string.Empty;

        public int? AutoHideSeconds { get; set; }
    }

    internal sealed class AnnouncementServerStatusInfo
    {
        public bool IsConnected { get; set; }

        public bool IsUsingFallback { get; set; }

        public string Source { get; set; } = string.Empty;

        public string Message { get; set; } = "Chưa kiểm tra máy chủ thông báo";

        public string Error { get; set; } = string.Empty;

        public string CheckedAt { get; set; } = string.Empty;

        public static AnnouncementServerStatusInfo Unknown()
        {
            return new AnnouncementServerStatusInfo
            {
                IsConnected = false,
                IsUsingFallback = false,
                Source = "Unknown",
                Message = "Chưa kiểm tra máy chủ thông báo",
                Error = string.Empty,
                CheckedAt = string.Empty
            };
        }

        public static AnnouncementServerStatusInfo Connecting(string source)
        {
            return new AnnouncementServerStatusInfo
            {
                IsConnected = false,
                IsUsingFallback = false,
                Source = source ?? string.Empty,
                Message = "Đang kết nối máy chủ thông báo...",
                Error = string.Empty,
                CheckedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public static AnnouncementServerStatusInfo Connected(string source)
        {
            return new AnnouncementServerStatusInfo
            {
                IsConnected = true,
                IsUsingFallback = false,
                Source = source ?? string.Empty,
                Message = "Đã kết nối máy chủ!",
                Error = string.Empty,
                CheckedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public static AnnouncementServerStatusInfo FallbackConnected(
            string source)
        {
            return new AnnouncementServerStatusInfo
            {
                IsConnected = true,
                IsUsingFallback = true,
                Source = source ?? string.Empty,
                Message = "Đang dùng máy chủ thông báo dự phòng",
                Error = string.Empty,
                CheckedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public static AnnouncementServerStatusInfo Failed(
            string source,
            string error)
        {
            return new AnnouncementServerStatusInfo
            {
                IsConnected = false,
                IsUsingFallback = false,
                Source = source ?? string.Empty,
                Message = "Không kết nối được máy chủ thông báo",
                Error = error ?? string.Empty,
                CheckedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public string ToDisplayText()
        {
            if (IsConnected && IsUsingFallback)
            {
                return "🟡 " + Message + " - " + Source;
            }

            if (IsConnected)
            {
                return "🟢 " + Message + " - " + Source;
            }

            if (!string.IsNullOrWhiteSpace(Error))
            {
                return "🔴 " + Message + " - " + Source + " | Lỗi: " + Error;
            }

            return "⚪ " + Message + " - " + Source;
        }
    }
}