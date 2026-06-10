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
        public int PollSeconds { get; set; } = 30;
        public int RotateSeconds { get; set; } = 10;
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
}
