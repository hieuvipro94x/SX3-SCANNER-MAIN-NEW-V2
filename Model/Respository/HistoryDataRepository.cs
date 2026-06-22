using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;

namespace SX3_SCANER.Model.Respository
{
    internal sealed class HistoryDataRepository
    {
        internal const string ScanHistorySource = "ScanHistoryView";
        internal const string BoxProductSource = "BoxProduct";
        private const int MaxSearchLimit = 50000;

        private static readonly string[] AllowedSources =
        {
            ScanHistorySource,
            BoxProductSource
        };

        internal List<string> GetAvailableSources()
        {
            var sources = new List<string>();

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                foreach (string source in AllowedSources)
                {
                    if (TableExists(connection, source))
                    {
                        sources.Add(source);
                    }
                }
            }

            return sources;
        }

        internal ObservableCollection<HistoryDataRow> Search(
            string source,
            string keyword,
            string boxName,
            string partNumber,
            string sealNo,
            string scanMessage,
            bool? result,
            DateTime? fromDate,
            DateTime? toDate,
            int limit)
        {
            string validatedSource = ValidateSource(source);
            int safeLimit = Math.Max(1, Math.Min(limit, MaxSearchLimit));

            List<HistoryDataRow> rows = (
                validatedSource == ScanHistorySource
                    ? SearchScanHistory(
                        keyword,
                        boxName,
                        partNumber,
                        sealNo,
                        scanMessage,
                        result,
                        fromDate,
                        toDate,
                        safeLimit)
                    : SearchBoxProduct(
                        keyword,
                        boxName,
                        partNumber,
                        sealNo,
                        result,
                        fromDate,
                        toDate,
                        safeLimit)).ToList();

            rows = SortNewestFirst(rows);

            return new ObservableCollection<HistoryDataRow>(rows);
        }

        private static List<HistoryDataRow> SortNewestFirst(
            IEnumerable<HistoryDataRow> rows)
        {
            List<HistoryDataRow> sortedRows =
                (rows ?? Enumerable.Empty<HistoryDataRow>())
                .OrderByDescending(row => row.ScanTime ?? DateTime.MinValue)
                .ThenByDescending(row => row.SortSequence > 0 ? row.SortSequence : row.ID)
                .ThenByDescending(row => row.ID)
                .ToList();

            for (int index = 0; index < sortedRows.Count; index++)
            {
                sortedRows[index].RowIndex = index + 1;
            }

            return sortedRows;
        }

        private static IEnumerable<HistoryDataRow> SearchScanHistory(
            string keyword,
            string boxName,
            string partNumber,
            string sealNo,
            string scanMessage,
            bool? result,
            DateTime? fromDate,
            DateTime? toDate,
            int limit)
        {
            ObservableCollection<ScanHistory> histories =
                new ScanHistoryRepository().SearchHistory(
                    keyword,
                    boxName,
                    partNumber,
                    sealNo,
                    scanMessage,
                    result,
                    fromDate,
                    toDate,
                    limit);

            return histories.Select(history => new HistoryDataRow
            {
                ID = history.ID,
                SortSequence = history.ID,
                ScanTime = history.ScanTime,
                DataSource = ScanHistorySource,
                BoxName = history.BoxName,
                ProductPartNumber = history.ProductPartNumber,
                ProductPartName = history.ProductPartName,
                SealNo = history.SealNo,
                LotNo = history.LotNo,
                ScanData = history.ScanData,
                ScanResult = history.ScanResult,
                ScanMessage = history.ScanMessage,
                ScanWorker = history.ScanWorker,
                ResultText = history.ResultText,
                BoxTypeText = history.BoxTypeText,
                BoxTypeDB = history.BoxType,
                IsOddBox = history.IsPartialBox
            });
        }

        private static IEnumerable<HistoryDataRow> SearchBoxProduct(
            string keyword,
            string boxName,
            string partNumber,
            string sealNo,
            bool? result,
            DateTime? fromDate,
            DateTime? toDate,
            int limit)
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                if (!TableExists(connection, BoxProductSource))
                {
                    return new List<HistoryDataRow>();
                }

                HashSet<string> columns = GetColumns(connection, BoxProductSource);
                bool hasScanHistory = TableExists(
                    connection,
                    ScanHistorySource);
                string sql = hasScanHistory
                    ? @"SELECT bp.*,
                               latest.RelatedScanTime,
                               latest.RelatedLastHistoryID
                        FROM [BoxProduct] bp
                        LEFT JOIN (
                            SELECT BoxName,
                                   MAX(ScanTime) AS RelatedScanTime,
                                   MAX(ID) AS RelatedLastHistoryID
                            FROM [ScanHistoryView]
                            GROUP BY BoxName
                        ) latest
                          ON latest.BoxName = bp.BoxName
                          OR (latest.BoxName IS NULL AND bp.BoxName IS NULL)
                        WHERE 1=1"
                    : "SELECT bp.* FROM [BoxProduct] bp WHERE 1=1";
                if (hasScanHistory)
                {
                    columns.Add("RelatedScanTime");
                    columns.Add("RelatedLastHistoryID");
                }
                var parameters = new List<SQLiteParameter>();

                AddExactTextFilter(
                    ref sql,
                    parameters,
                    columns,
                    NormalizeFilter(boxName),
                    "@BoxName",
                    "BoxName");

                AddExactTextFilter(
                    ref sql,
                    parameters,
                    columns,
                    NormalizeFilter(partNumber),
                    "@PartNumber",
                    "ProductPartNumber",
                    "PartNumber");
                AddExactTextFilter(
                    ref sql,
                    parameters,
                    columns,
                    NormalizeFilter(sealNo),
                    "@SealNo",
                    "BoxSealNo",
                    "SealNo");

                string resultColumn = FirstExisting(
                    columns,
                    "BoxComplete",
                    "ScanResult",
                    "Result");
                if (result.HasValue && resultColumn != null)
                {
                    sql += " AND bp.[" + resultColumn + "] = @Result";
                    parameters.Add(new SQLiteParameter(
                        "@Result",
                        result.Value ? 1 : 0));
                }

                string normalizedKeyword = NormalizeFilter(keyword);
                if (normalizedKeyword != null)
                {
                    // Ô tìm nhanh lịch sử chỉ lọc theo mã hàng/ProductPartNumber.
                    // Hỗ trợ cả tên cột ProductPartNumber và PartNumber tùy bảng.
                    string[] candidates =
                    {
                        "ProductPartNumber",
                        "PartNumber"
                    };
                    List<string> searchable =
                        candidates.Where(columns.Contains).Distinct().ToList();

                    if (searchable.Count > 0)
                    {
                        sql += " AND (" +
                            string.Join(
                                " OR ",
                                searchable.Select(column =>
                                    "COALESCE(bp.[" + column +
                                    "], '') COLLATE NOCASE LIKE @Keyword ESCAPE '\\'")) +
                            ")";
                        parameters.Add(new SQLiteParameter(
                            "@Keyword",
                            "%" + EscapeLikeValue(normalizedKeyword) + "%"));
                    }
                }

                string dateColumn = FirstExisting(
                    columns,
                    "BoxDate",
                    "ScanLabelDate",
                    "CreatedAt",
                    "CreateTime",
                    "ScanTime",
                    "DateTime",
                    "Time");
                if (fromDate.HasValue && dateColumn != null)
                {
                    sql += " AND bp.[" + dateColumn + "] >= @FromDate";
                    parameters.Add(new SQLiteParameter("@FromDate", fromDate.Value.Date));
                }

                if (toDate.HasValue && dateColumn != null)
                {
                    sql += " AND bp.[" + dateColumn + "] < @ToDateExclusive";
                    parameters.Add(new SQLiteParameter(
                        "@ToDateExclusive",
                        toDate.Value.Date.AddDays(1)));
                }

                if (hasScanHistory)
                {
                    sql += " ORDER BY RelatedScanTime DESC, RelatedLastHistoryID DESC";
                    if (columns.Contains("ID"))
                    {
                        sql += ", bp.[ID] DESC";
                    }
                }
                else
                {
                    string orderColumn = FirstExisting(
                        columns,
                        "ScanTime",
                        "CreatedAt",
                        "CreateTime",
                        "BoxTime",
                        "DateTime",
                        "Time",
                        "ID");
                    if (orderColumn != null)
                    {
                        sql += " ORDER BY bp.[" + orderColumn + "] DESC";
                        if (!string.Equals(
                            orderColumn,
                            "ID",
                            StringComparison.OrdinalIgnoreCase) &&
                            columns.Contains("ID"))
                        {
                            sql += ", bp.[ID] DESC";
                        }
                    }
                }

                sql += " LIMIT @Limit";
                parameters.Add(new SQLiteParameter("@Limit", limit));
                Debug.WriteLine("BoxProduct history SQL: " + sql);

                var rows = new List<HistoryDataRow>();
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddRange(parameters.ToArray());
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rows.Add(MapBoxProduct(reader, columns));
                        }
                    }
                }

                return rows;
            }
        }

        private static HistoryDataRow MapBoxProduct(
            SQLiteDataReader reader,
            HashSet<string> columns)
        {
            bool? scanResult = ReadNullableBool(
                reader,
                columns,
                "BoxComplete",
                "ScanResult",
                "Result");
            bool isPartialBox = ReadNullableBool(
                reader,
                columns,
                "IsPartialBox",
                "IsOddBox") ?? false;
            string boxType = ReadString(reader, columns, "BoxType");
            bool isCancelled = string.Equals(
                boxType,
                "CANCELLED",
                StringComparison.OrdinalIgnoreCase);
            if (string.Equals(
                boxType,
                "PARTIAL",
                StringComparison.OrdinalIgnoreCase))
            {
                isPartialBox = true;
            }

            string resultText = isCancelled
                ? "CANCELLED"
                : scanResult.HasValue
                ? (scanResult.Value ? "PASS" : "NG")
                : string.Empty;

            return new HistoryDataRow
            {
                ID = ReadInt(reader, columns, "ID"),
                SortSequence = ReadInt(reader, columns, "RelatedLastHistoryID", "ID"),
                ScanTime = ReadNullableDateTime(
                    reader,
                    columns,
                    "RelatedScanTime",
                    "ScanTime",
                    "CreatedAt",
                    "CreateTime",
                    "BoxTime",
                    "DateTime",
                    "Time"),
                DataSource = BoxProductSource,
                BoxName = ReadString(reader, columns, "BoxName"),
                ProductPartNumber = ReadString(
                    reader,
                    columns,
                    "ProductPartNumber",
                    "PartNumber"),
                ProductPartName = ReadString(
                    reader,
                    columns,
                    "ProductPartName",
                    "PartName"),
                SealNo = ReadString(reader, columns, "BoxSealNo", "SealNo"),
                LotNo = ReadString(reader, columns, "LotNo"),
                ScanData = ReadString(
                    reader,
                    columns,
                    "ScanData",
                    "ScannedQRCode",
                    "QRData"),
                ScanResult = scanResult,
                ScanMessage = resultText,
                ScanWorker = ReadString(
                    reader,
                    columns,
                    "BoxWorker",
                    "ScanWorker",
                    "Worker"),
                ResultText = resultText,
                BoxTypeText = BuildBoxTypeText(boxType, isPartialBox),
                BoxTypeDB = boxType,
                IsOddBox = isPartialBox
            };
        }

        private static void AddContainsTextFilter(
            ref string sql,
            List<SQLiteParameter> parameters,
            HashSet<string> columns,
            string value,
            string parameterName,
            params string[] candidates)
        {
            if (value == null)
            {
                return;
            }

            string column = FirstExisting(columns, candidates);
            if (column == null)
            {
                return;
            }

            sql += " AND COALESCE(bp.[" + column + "], '') COLLATE NOCASE LIKE " +
                parameterName + " ESCAPE '\\'";
            parameters.Add(new SQLiteParameter(
                parameterName,
                "%" + EscapeLikeValue(value) + "%"));
        }

        private static void AddExactTextFilter(
            ref string sql,
            List<SQLiteParameter> parameters,
            HashSet<string> columns,
            string value,
            string parameterName,
            params string[] candidates)
        {
            if (value == null)
            {
                return;
            }

            string column = FirstExisting(columns, candidates);
            if (column == null)
            {
                sql += " AND 1=0";
                return;
            }

            sql += " AND bp.[" + column +
                "] = " + parameterName + " COLLATE NOCASE";
            parameters.Add(new SQLiteParameter(parameterName, value));
        }

        private static string ValidateSource(string source)
        {
            string normalized = string.IsNullOrWhiteSpace(source)
                ? ScanHistorySource
                : source.Trim();
            string validated = AllowedSources.FirstOrDefault(item =>
                string.Equals(
                    item,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

            if (validated == null)
            {
                throw new InvalidOperationException(
                    "Nguồn dữ liệu không hợp lệ: " + normalized);
            }

            return validated;
        }

        private static bool TableExists(
            SQLiteConnection connection,
            string tableName)
        {
            using (var command = new SQLiteCommand(
                @"SELECT COUNT(1)
                  FROM sqlite_master
                  WHERE type IN ('table', 'view')
                    AND name = @Name COLLATE NOCASE",
                connection))
            {
                command.Parameters.AddWithValue("@Name", tableName);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static HashSet<string> GetColumns(
            SQLiteConnection connection,
            string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = new SQLiteCommand(
                "PRAGMA table_info([" + tableName + "])",
                connection))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add(Convert.ToString(reader["name"]));
                }
            }

            return columns;
        }

        private static string FirstExisting(
            HashSet<string> columns,
            params string[] candidates)
        {
            return candidates.FirstOrDefault(columns.Contains);
        }

        private static string ReadString(
            SQLiteDataReader reader,
            HashSet<string> columns,
            params string[] candidates)
        {
            string column = FirstExisting(columns, candidates);
            if (column == null)
            {
                return string.Empty;
            }

            object value = reader[column];
            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value);
        }

        private static int ReadInt(
            SQLiteDataReader reader,
            HashSet<string> columns,
            params string[] candidates)
        {
            int result;
            return int.TryParse(
                ReadString(reader, columns, candidates),
                out result)
                ? result
                : 0;
        }

        private static bool? ReadNullableBool(
            SQLiteDataReader reader,
            HashSet<string> columns,
            params string[] candidates)
        {
            string column = FirstExisting(columns, candidates);
            if (column == null ||
                reader[column] == null ||
                reader[column] == DBNull.Value)
            {
                return null;
            }

            string value = Convert.ToString(reader[column]);
            int number;
            if (int.TryParse(value, out number))
            {
                return number != 0;
            }

            bool boolean;
            return bool.TryParse(value, out boolean)
                ? (bool?)boolean
                : null;
        }

        private static DateTime? ReadNullableDateTime(
            SQLiteDataReader reader,
            HashSet<string> columns,
            params string[] candidates)
        {
            string value = ReadString(reader, columns, candidates);
            DateTime date;
            return DateTime.TryParse(value, out date)
                ? (DateTime?)date
                : null;
        }

        private static string BuildBoxTypeText(
            string boxType,
            bool isPartialBox)
        {
            if (string.Equals(
                boxType,
                "CANCELLED",
                StringComparison.OrdinalIgnoreCase))
            {
                return "ĐÃ HỦY";
            }

            if (isPartialBox ||
                string.Equals(
                    boxType,
                    "PARTIAL",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "THÙNG LẺ";
            }

            if (string.Equals(
                boxType,
                "FULL",
                StringComparison.OrdinalIgnoreCase))
            {
                return "THÙNG ĐỦ";
            }

            return boxType ?? string.Empty;
        }

        private static string NormalizeFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(
                    value.Trim(),
                    "All",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return value.Trim();
        }

        private static string EscapeLikeValue(string value)
        {
            return value
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
        }
    }
}
