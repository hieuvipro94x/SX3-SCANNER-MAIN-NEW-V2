using System;

namespace SX3_SCANER.Helper
{
    internal static class ScanValidationService
    {
        internal const int MaximumScanLabelAgeDays = 4;
        private const int SxdzYearBase = 2009;
        private const int SxdzDayOffset = 9;

        internal static bool IsScanLabelDateAllowed(
            DateTime scanLabelDate,
            DateTime currentDate)
        {
            return true;
        }

        internal static bool IsQrCode(string input)
        {
            return !string.IsNullOrWhiteSpace(input) && input.Contains(",");
        }

        internal static bool TryParseQrCode(
            string input,
            out string partName,
            out string serial)
        {
            partName = string.Empty;
            serial = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Trim().Split(',');
            if (parts.Length != 2)
                return false;

            partName = parts[0].Trim();
            serial = parts[1].Trim();
            return !string.IsNullOrWhiteSpace(partName) &&
                !string.IsNullOrWhiteSpace(serial);
        }

        internal static bool IsSxdzDataMatrix(string input)
        {
            string partName;
            string serial;
            string dateCode;
            string lotNo;
            DateTime labelDate;
            return TryParseSxdzDataMatrix(
                input,
                out partName,
                out serial,
                out dateCode,
                out labelDate,
                out lotNo);
        }

        internal static bool TryParseSxdzDataMatrix(
            string input,
            out string partName,
            out string serial,
            out string dateCode,
            out DateTime labelDate,
            out string lotNo)
        {
            partName = string.Empty;
            serial = string.Empty;
            dateCode = string.Empty;
            labelDate = default(DateTime);
            lotNo = string.Empty;

            if (!TryParseQrCode(input, out partName, out serial))
                return false;

            serial = serial.Trim().ToUpperInvariant();
            if (serial.Length != 11 ||
                !serial.StartsWith("SQDZ", StringComparison.Ordinal))
            {
                return false;
            }

            dateCode = serial.Substring(4, 3);
            lotNo = serial.Substring(7, 4);
            if (!IsAlphaNumeric(lotNo))
                return false;

            return TryParseSxdzDateCode(dateCode, out labelDate);
        }

        internal static bool TryParseSxdzDateCode(
            string dateCode,
            out DateTime labelDate)
        {
            labelDate = default(DateTime);
            if (string.IsNullOrWhiteSpace(dateCode) || dateCode.Trim().Length != 3)
                return false;

            string normalized = dateCode.Trim().ToUpperInvariant();
            int yearIndex = LetterIndex(normalized[0]);
            int month = DecodeMonthCode(normalized[1]);
            int day = LetterIndex(normalized[2]) + SxdzDayOffset;
            if (yearIndex <= 0 || month <= 0 || day <= 0)
                return false;

            int year = SxdzYearBase + yearIndex;
            if (day > DateTime.DaysInMonth(year, month))
                return false;

            labelDate = new DateTime(year, month, day);
            return true;
        }

        internal static string NormalizeQrProductCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().TrimStart('#').ToUpperInvariant();
        }

        internal static bool TryParseLeadingDate(string value, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 6)
                return false;

            string prefix = value.Trim().Substring(0, 6);
            int year;
            int month;
            int day;
            if (!int.TryParse(prefix.Substring(0, 2), out year) ||
                !int.TryParse(prefix.Substring(2, 2), out month) ||
                !int.TryParse(prefix.Substring(4, 2), out day))
            {
                return false;
            }

            year += 2000;
            if (month < 1 || month > 12 ||
                day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            date = new DateTime(year, month, day);
            return true;
        }

        internal static bool TryParseQrSerial(string value, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string serial = value.Trim();
            if (serial.Length != 10)
                return false;

            for (int i = 0; i < serial.Length; i++)
            {
                if (serial[i] < '0' || serial[i] > '9')
                    return false;
            }

            return TryParseLeadingDate(serial, out date);
        }

        internal static string ExtractSegment(
            string input,
            int startIndex,
            int length)
        {
            if (string.IsNullOrEmpty(input) ||
                startIndex < 0 ||
                length <= 0 ||
                startIndex >= input.Length)
            {
                return string.Empty;
            }

            return input.Substring(
                startIndex,
                Math.Min(length, input.Length - startIndex));
        }

        private static int LetterIndex(char value)
        {
            return value >= 'A' && value <= 'Z'
                ? value - 'A' + 1
                : 0;
        }

        private static int DecodeMonthCode(char value)
        {
            if (value >= '1' && value <= '9')
                return value - '0';

            if (value >= 'A' && value <= 'C')
                return value - 'A' + 10;

            return 0;
        }

        private static bool IsAlphaNumeric(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c < '0' || c > '9') &&
                    (c < 'A' || c > 'Z') &&
                    (c < 'a' || c > 'z'))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Kh\u00F4ng \u0111\u1ECDc \u0111\u01B0\u1EE3c"
                : value;
        }
    }
}
