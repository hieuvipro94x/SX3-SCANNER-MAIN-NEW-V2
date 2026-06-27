using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace SX3_SCANER.Model
{
    internal static class ScanResultMapper
    {
        private static readonly ConcurrentDictionary<string, HashSet<string>>
            TableColumnCache =
                new ConcurrentDictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase);

        internal static string BuildSelectColumns(SQLiteConnection connection)
        {
            return @"
                        ID,
                        ScanTime,
                        BoxName,
                        ProductPartNumber,
                        ProductPartName,
                        SealNo,
                        LotNo,
                        ScanData,
                        ScanResult,
                        ScanMessage,
                        ScanWorker,
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "BoxType", "'OPEN'") + @",
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "IsPartialBox", "0") + @",
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "BoxDate", "NULL") + @",
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "ScanLabelDate", "NULL") + @",
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "ActualQty", "0") + @",
                        " + SelectColumnOrDefault(connection, ScanHistoryRepository.HistoryTableName, "TargetQty", "0");
        }

        internal static ScanHistory Read(SQLiteDataReader reader)
        {
            return new ScanHistory
            {
                ID = SafeInt(reader, "ID"),
                RowIndex = 0,
                ScanTime = SafeDateTime(reader, "ScanTime"),
                BoxName = SafeString(reader, "BoxName"),
                ProductPartNumber = SafeString(reader, "ProductPartNumber"),
                ProductPartName = SafeString(reader, "ProductPartName"),
                SealNo = SafeString(reader, "SealNo"),
                LotNo = SafeString(reader, "LotNo"),
                ScanData = SafeString(reader, "ScanData"),
                ScanResult = SafeBool(reader, "ScanResult"),
                ScanMessage = SafeString(reader, "ScanMessage"),
                ScanWorker = SafeString(reader, "ScanWorker"),
                BoxType = SafeString(reader, "BoxType", "OPEN"),
                IsPartialBox = SafeBool(reader, "IsPartialBox"),
                BoxDate = SafeDateTime(reader, "BoxDate") ?? SafeDateTime(reader, "ScanTime"),
                ScanLabelDate = SafeDateTime(reader, "ScanLabelDate") ?? ParseSealDate(SafeString(reader, "SealNo")),
                ActualQty = SafeInt(reader, "ActualQty"),
                TargetQty = SafeInt(reader, "TargetQty")
            };
        }

        internal static string SelectColumnOrDefault(
            SQLiteConnection connection,
            string tableName,
            string columnName,
            string defaultSql)
        {
            return TableHasColumn(connection, tableName, columnName)
                ? columnName
                : defaultSql + " AS " + columnName;
        }

        internal static bool TableHasColumn(
            SQLiteConnection connection,
            string tableName,
            string columnName)
        {
            return GetTableColumns(connection, tableName).Contains(columnName);
        }

        internal static HashSet<string> GetTableColumns(
            SQLiteConnection connection,
            string tableName)
        {
            string cacheKey = (connection.DataSource ?? string.Empty) + "|" + tableName;
            HashSet<string> cached;
            bool canCache = !string.Equals(
                connection.DataSource,
                ":memory:",
                StringComparison.OrdinalIgnoreCase);
            if (canCache && TableColumnCache.TryGetValue(cacheKey, out cached))
                return cached;

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteCommand command = new SQLiteCommand(
                "PRAGMA table_info([" + tableName.Replace("]", "]]") + "])",
                connection))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add(SafeString(reader, "name"));
                }
            }

            if (canCache)
                TableColumnCache[cacheKey] = columns;
            return columns;
        }

        internal static void InvalidateSchemaCache()
        {
            TableColumnCache.Clear();
        }

        private static string SafeString(
            SQLiteDataReader reader,
            string columnName,
            string defaultValue = "")
        {
            int ordinal;
            if (!TryGetOrdinal(reader, columnName, out ordinal) || reader.IsDBNull(ordinal))
                return defaultValue;

            object value = reader.GetValue(ordinal);
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToString(value);
        }

        private static int SafeInt(SQLiteDataReader reader, string columnName)
        {
            int ordinal;
            if (!TryGetOrdinal(reader, columnName, out ordinal) || reader.IsDBNull(ordinal))
                return 0;

            int result;
            return int.TryParse(Convert.ToString(reader.GetValue(ordinal)), out result)
                ? result
                : 0;
        }

        private static bool SafeBool(SQLiteDataReader reader, string columnName)
        {
            int ordinal;
            if (!TryGetOrdinal(reader, columnName, out ordinal) || reader.IsDBNull(ordinal))
                return false;

            object value = reader.GetValue(ordinal);
            if (value is bool) return (bool)value;

            int numericValue;
            if (int.TryParse(Convert.ToString(value), out numericValue))
                return numericValue != 0;

            bool boolValue;
            return bool.TryParse(Convert.ToString(value), out boolValue) && boolValue;
        }

        private static DateTime? SafeDateTime(SQLiteDataReader reader, string columnName)
        {
            int ordinal;
            if (!TryGetOrdinal(reader, columnName, out ordinal) || reader.IsDBNull(ordinal))
                return null;

            DateTime result;
            return DateTime.TryParse(Convert.ToString(reader.GetValue(ordinal)), out result)
                ? (DateTime?)result
                : null;
        }

        private static bool TryGetOrdinal(
            SQLiteDataReader reader,
            string columnName,
            out int ordinal)
        {
            ordinal = -1;
            if (reader == null || string.IsNullOrWhiteSpace(columnName))
                return false;

            try
            {
                ordinal = reader.GetOrdinal(columnName);
                return ordinal >= 0;
            }
            catch (IndexOutOfRangeException)
            {
            }
            catch (ArgumentException)
            {
            }

            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    ordinal = i;
                    return true;
                }
            }

            return false;
        }

        private static DateTime? ParseSealDate(string sealNo)
        {
            DateTime value;
            return DateTime.TryParseExact(
                sealNo,
                "yyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value)
                    ? (DateTime?)value.Date
                    : null;
        }
    }
}
