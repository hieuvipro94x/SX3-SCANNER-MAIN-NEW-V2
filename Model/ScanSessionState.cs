using System;
using System.Collections.Generic;

namespace SX3_SCANER.Model
{
    internal sealed class ScanSessionState
    {
        public string SessionKey { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string BoxCode { get; set; } = string.Empty;
        public int ScannedCount { get; set; }
        public int TargetCount { get; set; }
        public List<ScanHistory> ScanHistoryItems { get; set; } = new List<ScanHistory>();
        public bool IsInJob { get; set; }
        public string Worker { get; set; } = string.Empty;
        public DateTime SessionDate { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
