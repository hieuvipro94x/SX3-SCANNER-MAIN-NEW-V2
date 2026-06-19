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
