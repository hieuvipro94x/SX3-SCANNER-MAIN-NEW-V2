using System;
using System.Globalization;
using System.Text;

namespace SX3_SCANER.Model
{
    internal class ScanHistory
    {
        public ScanHistory()
        {
            BoxName = string.Empty;
            ProductPartNumber = string.Empty;
            ProductPartName = string.Empty;
            SealNo = string.Empty;
            LotNo = string.Empty;
            ScanData = string.Empty;
            ScanWorker = string.Empty;
            BoxType = "OPEN";
        }

        public int RowIndex { get; set; }

        public int ID { get; set; }

        public DateTime? ScanTime { get; set; }

        public string BoxName { get; set; }

        public string ProductPartNumber { get; set; }

        public string ProductPartName { get; set; }

        public string SealNo { get; set; }

        public string LotNo { get; set; }

        // Maps to ScanHistoryView.ScanData.
        public string ScanData { get; set; }

        public bool ScanResult { get; set; }

        private string _scanMessage = string.Empty;

        // Maps to ScanHistoryView.ScanMessage.
        public string ScanMessage
        {
            get { return _scanMessage; }
            set { _scanMessage = NormalizeScanMessage(value); }
        }

        public string ScanWorker { get; set; }

        public string BoxType { get; set; }

        public bool IsPartialBox { get; set; }

        public string ResultText
        {
            get { return ScanResult ? "PASS" : "NG"; }
        }

        public string ScanErrorMessage
        {
            get { return ScanResult ? string.Empty : ScanMessage; }
        }

        public string ShortErrorMessage
        {
            get { return ScanResult ? string.Empty : ToShortScanMessage(ScanMessage); }
        }

        public string ShortScanMessage
        {
            get { return ToShortScanMessage(ScanMessage); }
        }

        public string Worker
        {
            get { return ScanWorker; }
        }

        public string BoxTypeDB
        {
            get { return BoxType; }
        }

        public bool IsOddBox
        {
            get { return IsPartialBox; }
        }

        public bool IsMixedBox
        {
            get { return IsPartialBox; }
        }

        public string ScannedQRCode
        {
            get { return ScanData; }
        }

        public string QRData
        {
            get { return ScanData; }
        }

        public string BoxTypeText
        {
            get
            {
                if (IsPartialBox ||
                    string.Equals(BoxType, "PARTIAL", StringComparison.OrdinalIgnoreCase))
                {
                    return "THÙNG LẺ";
                }

                if (string.Equals(BoxType, "FULL", StringComparison.OrdinalIgnoreCase))
                {
                    return "THÙNG ĐỦ";
                }

                return BoxType ?? string.Empty;
            }
        }

        public static string NormalizeScanMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string value = message.Trim();
            string normalized = RemoveVietnameseSigns(value).ToUpperInvariant();

            if (normalized == "PASS" || normalized == "OK")
            {
                return "PASS";
            }

            if (normalized == "NG")
            {
                return "NG";
            }

            if (normalized == "LEN")
            {
                return "NG - Sai độ dài";
            }

            if (normalized == "PFX")
            {
                return "NG - Sai đầu mã / Prefix";
            }

            if (normalized == "PNAME")
            {
                return "NG - Sai mã sản phẩm / PartName";
            }

            if (normalized == "DATE")
            {
                return "NG - Sai ngày / SealNo";
            }

            if (normalized == "DATE_DUP" || normalized == "DUP_DATE")
            {
                return "NG - Trùng ngày / SealNo";
            }

            if (normalized == "LOT")
            {
                return "NG - Sai LotNo";
            }

            if (normalized == "DUP")
            {
                return "NG - Trùng LotNo";
            }

            if (normalized == "SFX")
            {
                return "NG - Sai cuối mã / Suffix";
            }

            return value;
        }

        public static string ToShortScanMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string value = message.Trim();
            string normalized = RemoveVietnameseSigns(value).ToUpperInvariant();

            if (normalized == "PASS" || normalized == "OK")
            {
                return string.Empty;
            }

            if (normalized.Contains("DO DAI") ||
                normalized.Contains("SO LUONG KY TU") ||
                normalized.Contains("LENGTH"))
            {
                return "Sai độ dài";
            }

            if (normalized.Contains("TEN SAN PHAM") ||
                normalized.Contains("PARTNAME") ||
                normalized.Contains("PART NAME"))
            {
                return "Sai tên sản phẩm";
            }

            if ((normalized.Contains("NGAY") ||
                 normalized.Contains("SEAL")) &&
                normalized.Contains("TRUNG"))
            {
                return "Trùng ngày / SealNo";
            }

            if (normalized.Contains("NGAY") ||
                normalized.Contains("SEAL"))
            {
                return "Sai ngày / SealNo";
            }

            if (normalized.Contains("LOT"))
            {
                return normalized.Contains("TRUNG") ||
                       normalized.Contains("DA DUOC SCAN") ||
                       normalized.Contains("DA DUOC CHECK")
                    ? "Trùng LotNo"
                    : "Sai LotNo";
            }

            if (normalized.Contains("PREFIX") || normalized.Contains("DAU MA"))
            {
                return "Sai Prefix";
            }

            if (normalized.Contains("SUFFIX") || normalized.Contains("CUOI MA"))
            {
                return "Sai Suffix";
            }

            return value.Length <= 80 ? value : value.Substring(0, 77) + "...";
        }

        internal static string RemoveVietnameseSigns(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (char character in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(
                        character == 'Đ' ? 'D' :
                        character == 'đ' ? 'd' :
                        character);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
