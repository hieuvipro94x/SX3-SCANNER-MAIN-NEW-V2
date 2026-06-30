using System;

namespace SX3_SCANER.Helper
{
    internal static class ScanValidationService
    {
        internal static bool IsPartNoCommaSerialFormat(string input)
        {
            return !string.IsNullOrWhiteSpace(input) && input.Contains(",");
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

        internal static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Kh\u00F4ng \u0111\u1ECDc \u0111\u01B0\u1EE3c"
                : value;
        }
    }
}
