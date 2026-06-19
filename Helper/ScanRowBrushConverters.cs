using System;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media;

namespace SX3_SCANER.Helper
{
    public class ScanRowBackgroundBrushConverter : IValueConverter
    {
        private static readonly Brush PassBrush = CreateBrush("#DCFCE7");
        private static readonly Brush NgBrush = CreateBrush("#FEE2E2");
        private static readonly Brush PartialBrush = CreateBrush("#FEF3C7");
        private static readonly Brush DefaultBrush = Brushes.White;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ScanRowState state = ScanRowBrushHelper.GetState(value);

            switch (state)
            {
                case ScanRowState.Ng:
                    return NgBrush;
                case ScanRowState.Partial:
                    return PartialBrush;
                case ScanRowState.Pass:
                    return PassBrush;
                default:
                    return DefaultBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static Brush CreateBrush(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

    public class ScanRowForegroundBrushConverter : IValueConverter
    {
        private static readonly Brush PassBrush = CreateBrush("#166534");
        private static readonly Brush NgBrush = CreateBrush("#991B1B");
        private static readonly Brush PartialBrush = CreateBrush("#92400E");
        private static readonly Brush DefaultBrush = CreateBrush("#0F172A");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ScanRowState state = ScanRowBrushHelper.GetState(value);

            switch (state)
            {
                case ScanRowState.Ng:
                    return NgBrush;
                case ScanRowState.Partial:
                    return PartialBrush;
                case ScanRowState.Pass:
                    return PassBrush;
                default:
                    return DefaultBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static Brush CreateBrush(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

    internal enum ScanRowState
    {
        Default,
        Pass,
        Partial,
        Ng
    }

    internal static class ScanRowBrushHelper
    {
        public static ScanRowState GetState(object row)
        {
            if (row == null)
            {
                return ScanRowState.Default;
            }

            if (IsNg(row))
            {
                return ScanRowState.Ng;
            }

            if (IsPartial(row))
            {
                return ScanRowState.Partial;
            }

            if (IsPass(row))
            {
                return ScanRowState.Pass;
            }

            return ScanRowState.Default;
        }

        private static bool IsNg(object row)
        {
            if (TryGetValue(row, "ScanResult", out object scanResult) && IsFalse(scanResult))
            {
                return true;
            }

            string resultText = GetString(row, "ResultText");
            if (EqualsIgnoreCase(resultText, "NG"))
            {
                return true;
            }

            string scanMessage = GetString(row, "ScanMessage");
            return StartsWithIgnoreCase(scanMessage, "NG");
        }

        private static bool IsPartial(object row)
        {
            if (TryGetValue(row, "IsPartialBox", out object partial) && IsTrue(partial))
            {
                return true;
            }

            string boxType = GetString(row, "BoxType");
            if (EqualsIgnoreCase(boxType, "PARTIAL") || EqualsIgnoreCase(boxType, "THUNG LE") || EqualsIgnoreCase(boxType, "THÙNG LẺ"))
            {
                return true;
            }

            string vietnameseBoxType = GetString(row, "Loại thùng");
            return EqualsIgnoreCase(vietnameseBoxType, "PARTIAL") || EqualsIgnoreCase(vietnameseBoxType, "THUNG LE") || EqualsIgnoreCase(vietnameseBoxType, "THÙNG LẺ");
        }

        private static bool IsPass(object row)
        {
            if (TryGetValue(row, "ScanResult", out object scanResult) && IsTrue(scanResult))
            {
                return true;
            }

            if (TryGetValue(row, "BoxComplete", out object boxComplete) && IsTrue(boxComplete))
            {
                return true;
            }

            string resultText = GetString(row, "ResultText");
            if (EqualsIgnoreCase(resultText, "PASS"))
            {
                return true;
            }

            string scanMessage = GetString(row, "ScanMessage");
            return EqualsIgnoreCase(scanMessage, "PASS");
        }

        private static string GetString(object row, string name)
        {
            return TryGetValue(row, name, out object value) ? value?.ToString()?.Trim() : null;
        }

        private static bool TryGetValue(object row, string name, out object value)
        {
            value = null;

            DataRowView rowView = row as DataRowView;
            if (rowView != null && rowView.Row.Table.Columns.Contains(name))
            {
                value = rowView.Row[name];
                return value != DBNull.Value;
            }

            PropertyDescriptor descriptor = TypeDescriptor.GetProperties(row)[name];
            if (descriptor != null)
            {
                value = descriptor.GetValue(row);
                return true;
            }

            PropertyInfo property = row.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
            {
                value = property.GetValue(row, null);
                return true;
            }

            return false;
        }

        private static bool IsTrue(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            string text = value?.ToString()?.Trim();
            return EqualsIgnoreCase(text, "true") || text == "1" || EqualsIgnoreCase(text, "PASS") || EqualsIgnoreCase(text, "OK");
        }

        private static bool IsFalse(object value)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            string text = value?.ToString()?.Trim();
            return EqualsIgnoreCase(text, "false") || text == "0" || EqualsIgnoreCase(text, "NG");
        }

        private static bool EqualsIgnoreCase(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithIgnoreCase(string value, string expected)
        {
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
