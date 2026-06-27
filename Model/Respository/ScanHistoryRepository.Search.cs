using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using SX3_SCANER.Helper;
using SX3_SCANER.Model.Respository;

namespace SX3_SCANER.Model
{
    internal partial class ScanHistoryRepository
    {
        private static readonly bool EnableSqlDiagnostics = false;

        public ObservableCollection<ScanHistory> SearchHistory(
            string keyword,
            string boxName,
            string partNumber,
            string sealNo,
            string scanMessage,
            bool? scanResult,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 500,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetScanned(
                boxname: boxName,
                partnumber: partNumber,
                sealno: sealNo,
                scandata: keyword,
                scanresult: scanResult,
                scanmessage: scanMessage,
                fromDate: fromDate,
                toDate: toDate,
                limit: limit,
                cancellationToken: cancellationToken);
        }

        public ObservableCollection<ScanHistory> GetScanned(
            string boxname = null,
            string partnumber = null,
            string sealno = null,
            string scandata = null,
            bool? scanresult = null,
            string scanmessage = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 500,
            bool useBoxNameContains = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ObservableCollection<ScanHistory> scanHistoryItems = new ObservableCollection<ScanHistory>();

            string normalizedBoxName = NormalizeFilterValue(boxname);
            string normalizedPartNumber = NormalizeFilterValue(partnumber);
            string normalizedSealNo = NormalizeFilterValue(sealno);
            string normalizedScanMessage = NormalizeFilterValue(scanmessage);
            List<string> searchTerms = BuildSearchTerms(scandata);
            bool? directKeywordResult = TryParseScanResultKeyword(scandata);
            int safeLimit = Math.Max(1, Math.Min(limit, 2000));

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                bool hasBoxType = ScanResultMapper.TableHasColumn(
                    connection,
                    HistoryTableName,
                    "BoxType");
                string selectQuery = @"
                    SELECT
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
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "BoxType",
                            "'OPEN'") + @",
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "IsPartialBox",
                            "0") + @",
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "BoxDate",
                            "NULL") + @",
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "ScanLabelDate",
                            "NULL") + @",
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "ActualQty",
                            "0") + @",
                        " + ScanResultMapper.SelectColumnOrDefault(
                            connection,
                            HistoryTableName,
                            "TargetQty",
                            "0") + @"
                    FROM ScanHistoryView
                    WHERE 1=1";

                if (!string.IsNullOrWhiteSpace(normalizedBoxName))
                {
                    selectQuery += useBoxNameContains
                        ? " AND BoxName COLLATE NOCASE LIKE @BoxName ESCAPE '\\'"
                        : " AND BoxName = @BoxName COLLATE NOCASE";
                }

                if (!string.IsNullOrWhiteSpace(normalizedPartNumber))
                {
                    selectQuery += " AND ProductPartNumber = @ProductPartNumber COLLATE NOCASE";
                }

                if (!string.IsNullOrWhiteSpace(normalizedSealNo))
                {
                    selectQuery += " AND SealNo = @SealNo COLLATE NOCASE";
                }

                if (scanresult.HasValue)
                {
                    selectQuery += " AND ScanResult = @ScanResult";
                }

                if (!string.IsNullOrWhiteSpace(normalizedScanMessage))
                {
                    selectQuery += " AND ScanMessage = @ScanMessage COLLATE NOCASE";
                }

                if (fromDate.HasValue)
                {
                    selectQuery += " AND ScanTime >= @FromDate";
                }

                if (toDate.HasValue)
                {
                    selectQuery += " AND ScanTime < @ToDateExclusive";
                }

                if (directKeywordResult.HasValue)
                {
                    selectQuery += " AND ScanResult = @DirectKeywordResult";
                }
                else if (searchTerms.Count > 0)
                {
                    selectQuery += " AND (";
                    for (int i = 0; i < searchTerms.Count; i++)
                    {
                        if (i > 0)
                        {
                            selectQuery += " OR ";
                        }

                        string parameterName = "@Keyword" + i;
                        selectQuery += @"
                            (
                                COALESCE(ProductPartNumber, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                                OR COALESCE(ScanData, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                                OR COALESCE(LotNo, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                                OR COALESCE(BoxName, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                                OR COALESCE(ScanMessage, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                                OR COALESCE(BoxType, '') COLLATE NOCASE LIKE " + parameterName + @" ESCAPE '\'
                            )";
                    }
                    selectQuery += ")";
                }

                // ID is the safest newest key in SQLite because ScanTime may be stored as text
                // with different culture formats on some machines.
                selectQuery += " ORDER BY ID DESC LIMIT @Limit";

                using (SQLiteCommand command = new SQLiteCommand(selectQuery, connection))
                {
                    command.Parameters.AddWithValue("@Limit", safeLimit);

                    if (!string.IsNullOrWhiteSpace(normalizedBoxName))
                    {
                        string boxNameParameter = useBoxNameContains
                            ? "%" + EscapeLikeValue(normalizedBoxName) + "%"
                            : normalizedBoxName;
                        command.Parameters.AddWithValue("@BoxName", boxNameParameter);
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedPartNumber))
                        command.Parameters.AddWithValue("@ProductPartNumber", normalizedPartNumber);
                    if (!string.IsNullOrWhiteSpace(normalizedSealNo))
                        command.Parameters.AddWithValue("@SealNo", normalizedSealNo);
                    if (scanresult.HasValue)
                        command.Parameters.AddWithValue("@ScanResult", scanresult.Value ? 1 : 0);
                    if (!string.IsNullOrWhiteSpace(normalizedScanMessage))
                        command.Parameters.AddWithValue("@ScanMessage", normalizedScanMessage);
                    if (fromDate.HasValue)
                    {
                        command.Parameters.AddWithValue("@FromDate", fromDate.Value.Date);
                    }
                    if (toDate.HasValue)
                    {
                        command.Parameters.AddWithValue(
                            "@ToDateExclusive",
                            toDate.Value.Date.AddDays(1));
                    }

                    if (directKeywordResult.HasValue)
                    {
                        command.Parameters.AddWithValue(
                            "@DirectKeywordResult",
                            directKeywordResult.Value ? 1 : 0);
                    }
                    else for (int i = 0; i < searchTerms.Count; i++)
                    {
                        command.Parameters.AddWithValue(
                            "@Keyword" + i,
                            "%" + EscapeLikeValue(searchTerms[i]) + "%");
                    }

                    WriteSqlDiagnostics(selectQuery, command.Parameters);

                    cancellationToken.ThrowIfCancellationRequested();
                    using (cancellationToken.Register(command.Cancel))
                    using (SQLiteDataReader reader = ExecuteCancellableReader(
                        command,
                        cancellationToken))
                    {
                        while (reader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            scanHistoryItems.Add(ScanResultMapper.Read(reader));
                        }
                    }
                }
            }

            RefreshRowIndex(scanHistoryItems);
            return scanHistoryItems;
        }

        private static SQLiteDataReader ExecuteCancellableReader(
            SQLiteCommand command,
            CancellationToken cancellationToken)
        {
            try
            {
                return command.ExecuteReader();
            }
            catch (SQLiteException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private static string NormalizeFilterValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string trimmed = value.Trim();
            if (string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Tất cả", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Tat ca", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return trimmed;
        }

        private static void RefreshRowIndex(ObservableCollection<ScanHistory> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].RowIndex = i + 1;
            }
        }

        private static List<string> BuildSearchTerms(string keyword)
        {
            var terms = new List<string>();
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim() == "All")
                return terms;

            string original = keyword.Trim();
            AddUnique(terms, original);
            AddUnique(terms, original.ToUpperInvariant());
            AddUnique(terms, original.ToLowerInvariant());
            string normalized = NormalizeSearchText(keyword);
            AddUnique(terms, normalized);

            if (normalized == "PASS" || normalized == "OK")
            {
                AddUnique(terms, "PASS");
                AddUnique(terms, "OK");
            }
            else if (normalized == "NG")
            {
                AddUnique(terms, "NG");
            }
            else if (normalized.Contains("SAI DAI"))
            {
                AddUnique(terms, "NG - Sai \u0111\u1ED9 d\u00E0i");
                AddUnique(terms, "Sai d\u00E0i");
                AddUnique(terms, "Sai dai");
                AddUnique(terms, "LEN");
            }
            else if (normalized.Contains("SAI DAU"))
            {
                AddUnique(terms, "NG - Sai \u0111\u1EA7u m\u00E3 / Prefix");
                AddUnique(terms, "Sai \u0111\u1EA7u");
                AddUnique(terms, "Sai dau");
                AddUnique(terms, "Prefix");
                AddUnique(terms, "PFX");
            }
            else if (normalized.Contains("SAI MA") ||
                     normalized.Contains("TEN SAN PHAM"))
            {
                AddUnique(terms, "NG - Sai m\u00E3 s\u1EA3n ph\u1EA9m / PartName");
                AddUnique(terms, "Lỗi tên sản phẩm");
                AddUnique(terms, "LỖI TÊN SẢN PHẨM");
                // Giữ khả năng tìm các bản ghi cũ đã lưu mojibake; dùng escape để source vẫn là UTF-8 sạch.
                AddUnique(terms, "L\u00E1\u00BB\u2014i t\u0102\u00AAn s\u00E1\u00BA\u00A3n ph\u00E1\u00BA\u00A9m");
                AddUnique(terms, "L\u00E1\u00BB\u2013I T\u0102\u008AN S\u00E1\u00BA\u00A2N PH\u00E1\u00BA\u00A8M");
                AddUnique(terms, "Sai m\u00E3");
                AddUnique(terms, "Sai tên sản phẩm");
                AddUnique(terms, "Sai t\u0102\u00AAn s\u00E1\u00BA\u00A3n ph\u00E1\u00BA\u00A9m");
                AddUnique(terms, "Sai ma");
                AddUnique(terms, "PartName");
                AddUnique(terms, "PNAME");
            }
            else if ((normalized.Contains("TRUNG NGAY") ||
                      normalized.Contains("TRUNG SEAL") ||
                      normalized.Contains("DUP DATE")))
            {
                AddUnique(terms, "NG - Trùng ngày / SealNo");
                AddUnique(terms, "Trùng ngày");
                AddUnique(terms, "NG - Tr\u0102\u00B9ng ng\u0102\u00A0y / SealNo");
                AddUnique(terms, "Tr\u0102\u00B9ng ng\u0102\u00A0y");
                AddUnique(terms, "Trung ngay");
                AddUnique(terms, "Trùng SealNo");
                AddUnique(terms, "Tr\u0102\u00B9ng SealNo");
                AddUnique(terms, "DUP_DATE");
            }
            else if (normalized.Contains("SAI NGAY") || normalized.Contains("SAI SEAL") || normalized.Contains("SAI DATE"))
            {
                AddUnique(terms, "NG - Sai ng\u00E0y/SealNo");
                AddUnique(terms, "Sai ng\u00E0y");
                AddUnique(terms, "Sai ngay");
                AddUnique(terms, "Sai seal");
                AddUnique(terms, "Sai date");
                AddUnique(terms, "DATE");
            }
            else if (normalized.Contains("SAI LOT"))
            {
                AddUnique(terms, "NG - Sai LotNo");
                AddUnique(terms, "Sai lot");
                AddUnique(terms, "LOT");
            }
            else if (normalized.Contains("TRUNG"))
            {
                AddUnique(terms, "NG - Tr\u00F9ng LotNo");
                AddUnique(terms, "Tr\u00F9ng");
                AddUnique(terms, "Trung");
                AddUnique(terms, "DUP");
            }
            else if (normalized.Contains("THUNG LE"))
            {
                AddUnique(terms, "PARTIAL");
                AddUnique(terms, "TH\u00D9NG L\u1EBA");
                AddUnique(terms, "Th\u00F9ng l\u1EBB");
                AddUnique(terms, "Thung le");
            }
            else if (normalized.Contains("THUNG DU"))
            {
                AddUnique(terms, "FULL");
                AddUnique(terms, "TH\u00D9NG \u0110\u1EE6");
                AddUnique(terms, "Th\u00F9ng \u0111\u1EE7");
                AddUnique(terms, "Thung du");
            }

            return terms;
        }

        private static string NormalizeSearchText(string value)
        {
            return ScanHistory.RemoveVietnameseSigns(value ?? string.Empty).Trim().ToUpperInvariant();
        }

        internal static bool? TryParseScanResultKeyword(string value)
        {
            string normalized = NormalizeSearchText(value);
            if (normalized == "PASS" || normalized == "OK")
            {
                return true;
            }

            if (normalized == "NG")
            {
                return false;
            }

            return null;
        }

        private static void AddUnique(List<string> terms, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            string trimmed = value.Trim();
            foreach (string term in terms)
            {
                if (string.Equals(term, trimmed, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            terms.Add(trimmed);
        }

        private static void WriteSqlDiagnostics(
            string sql,
            SQLiteParameterCollection parameters)
        {
            if (!EnableSqlDiagnostics)
            {
                return;
            }

            Debug.WriteLine("Scan history SQL: " + sql);
            foreach (SQLiteParameter parameter in parameters)
            {
                Debug.WriteLine(
                    "Scan history parameter " + parameter.ParameterName +
                    " type=" + parameter.DbType);
            }
        }

        private static string EscapeLikeValue(string value)
        {
            return value
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
        }
        public void UpdateWorkerByBoxName(string boxName, string worker)
        {
            if (string.IsNullOrWhiteSpace(boxName) || string.IsNullOrWhiteSpace(worker))
                return;

            using (SQLiteConnection conn = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = conn.BeginTransaction())
            {
                UpdateWorkerByBoxName(boxName, worker, conn, transaction);
                transaction.Commit();
            }
        }

        internal void UpdateWorkerByBoxName(
            string boxName,
            string worker,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(boxName) || string.IsNullOrWhiteSpace(worker))
                return;

            const string sql = @"
                UPDATE ScanHistoryView
                SET ScanWorker = @ScanWorker
                WHERE BoxName = @BoxName";
            using (SQLiteCommand command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@ScanWorker", worker);
                command.Parameters.AddWithValue("@BoxName", boxName);
                command.ExecuteNonQuery();
            }
        }

        public void SetBoxTypeByBoxName(string boxName, bool isPartial)
        {
            if (string.IsNullOrWhiteSpace(boxName))
                return;

            using (SQLiteConnection conn = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = conn.BeginTransaction())
            {
                SetBoxTypeByBoxName(boxName, isPartial, conn, transaction);
                transaction.Commit();
            }
        }

        internal void SetBoxTypeByBoxName(
            string boxName,
            bool isPartial,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(boxName))
                return;

            const string sql = @"
                UPDATE ScanHistoryView
                SET BoxType = @BoxType,
                    IsPartialBox = @IsPartialBox,
                    ActualQty = @ActualQty,
                    TargetQty = CASE WHEN TargetQty > 0 THEN TargetQty ELSE @TargetQty END
                WHERE BoxName = @BoxName";
            using (SQLiteCommand command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@BoxType", isPartial ? "PARTIAL" : "FULL");
                command.Parameters.AddWithValue("@IsPartialBox", isPartial ? 1 : 0);
                command.Parameters.AddWithValue("@ActualQty", GetPassCount(boxName, connection, transaction));
                command.Parameters.AddWithValue("@TargetQty", GetTargetQty(boxName, connection, transaction));
                command.Parameters.AddWithValue("@BoxName", boxName);
                command.ExecuteNonQuery();
            }
        }

        private static int GetPassCount(
            string boxName,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (SQLiteCommand command = new SQLiteCommand(
                "SELECT COUNT(1) FROM ScanHistoryView WHERE BoxName = @BoxName AND ScanResult = 1",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@BoxName", boxName);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static int GetTargetQty(
            string boxName,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (SQLiteCommand command = new SQLiteCommand(
                "SELECT COALESCE(MAX(BoxQuantity), 0) FROM BoxProduct WHERE BoxName = @BoxName",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@BoxName", boxName);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        internal void CancelBoxByBoxName(
            string boxName,
            string worker,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(boxName))
                return;

            const string sql = @"
                UPDATE ScanHistoryView
                SET BoxType = 'CANCELLED',
                    IsPartialBox = 0,
                    ScanWorker = CASE
                        WHEN @ScanWorker = '' THEN ScanWorker
                        ELSE @ScanWorker
                    END
                WHERE BoxName = @BoxName";
            using (SQLiteCommand command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@ScanWorker", (worker ?? string.Empty).Trim());
                command.Parameters.AddWithValue("@BoxName", boxName);
                command.ExecuteNonQuery();
            }
        }
    }

    internal class DashboardScanStats
    {
        public int Total { get; set; }
        public int Pass { get; set; }
        public int Fail { get; set; }
    }
}
