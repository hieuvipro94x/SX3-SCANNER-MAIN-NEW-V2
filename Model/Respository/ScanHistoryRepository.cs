using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics;
using SX3_SCANER.Helper;
using SX3_SCANER.Model.Respository;

namespace SX3_SCANER.Model
{
    internal partial class ScanHistoryRepository
    {
        internal const string HistoryTableName = "ScanHistoryView";

        private static string _connectionString;

        public ScanHistoryRepository()
        {
            _connectionString = DatabaseRepository.ConnectionString;
        }

        internal static void CreateTableIfNotExists()
        {
            _connectionString = DatabaseRepository.ConnectionString;
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS ScanHistoryView (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScanTime DATETIME,
                        BoxName TEXT,
                        ProductPartNumber TEXT,
                        ProductPartName TEXT,
                        SealNo TEXT,
                        LotNo TEXT,
                        ScanData TEXT,
                        ScanResult INTEGER,
                        ScanMessage TEXT,
                        ScanWorker TEXT,
                        BoxType TEXT NOT NULL DEFAULT 'OPEN',
                        IsPartialBox INTEGER NOT NULL DEFAULT 0,
                        BoxDate TEXT,
                        ScanLabelDate TEXT,
                        ActualQty INTEGER NOT NULL DEFAULT 0,
                        TargetQty INTEGER NOT NULL DEFAULT 0
                    );";
                using (SQLiteCommand command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                EnsureColumn(connection, "BoxType", "TEXT NOT NULL DEFAULT 'OPEN'");
                EnsureColumn(connection, "IsPartialBox", "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(connection, "BoxDate", "TEXT");
                EnsureColumn(connection, "ScanLabelDate", "TEXT");
                EnsureColumn(connection, "ActualQty", "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(connection, "TargetQty", "INTEGER NOT NULL DEFAULT 0");
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string columnName, string definition)
        {
            using (SQLiteCommand command = new SQLiteCommand("PRAGMA table_info(ScanHistoryView)", connection))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader["name"].ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            using (SQLiteCommand command = new SQLiteCommand(
                "ALTER TABLE ScanHistoryView ADD COLUMN " + columnName + " " + definition,
                connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public ObservableCollection<ScanHistory> GetAllScanHistory()
        {
            ObservableCollection<ScanHistory> scanHistoryItems = new ObservableCollection<ScanHistory>();
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                string selectQuery = "SELECT " + ScanResultMapper.BuildSelectColumns(connection) + " FROM ScanHistoryView";
                using (SQLiteCommand command = new SQLiteCommand(selectQuery, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            scanHistoryItems.Add(ScanResultMapper.Read(reader));
                        }
                    }
                }
            }
            return scanHistoryItems;
        }


        public DashboardScanStats GetDashboardScanStats(DateTime businessDate)
        {
            DateTime date = businessDate.Date;
            string sealNo = date.ToString("yyMMdd");

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT
                        COALESCE(SUM(CASE WHEN ScanResult = 1 THEN 1 ELSE 0 END), 0) AS PassScan,
                        COALESCE(SUM(CASE WHEN ScanResult = 0 THEN 1 ELSE 0 END), 0) AS FailScan
                    FROM ScanHistoryView
                    WHERE
                        -- Æ¯u tiĂªn thá»‘ng kĂª theo NGĂ€Y BOX.
                        -- Náº¿u má»™t thĂ¹ng chÆ°a Ä‘á»§ sá»‘ lÆ°á»£ng vĂ  sang ngĂ y hĂ´m sau má»›i scan tiáº¿p,
                        -- cĂ¡c tem scan thĂªm váº«n cĂ³ cĂ¹ng BoxDate nĂªn váº«n Ä‘Æ°á»£c cá»™ng vĂ o tá»•ng cá»§a phiĂªn thĂ¹ng.
                        date(BoxDate) = date(@BusinessDate)

                        -- Fallback cho dá»¯ liá»‡u cÅ© chÆ°a cĂ³ BoxDate.
                        OR (
                            (BoxDate IS NULL OR TRIM(CAST(BoxDate AS TEXT)) = '')
                            AND (
                                date(ScanLabelDate) = date(@BusinessDate)
                                OR (
                                    (ScanLabelDate IS NULL OR TRIM(CAST(ScanLabelDate AS TEXT)) = '')
                                    AND (
                                        date(ScanTime) = date(@BusinessDate)
                                        OR SealNo = @SealNo
                                        OR BoxName LIKE @TodayPrefix
                                    )
                                )
                            )
                        )";

                command.Parameters.AddWithValue("@BusinessDate", date);
                command.Parameters.AddWithValue("@SealNo", sealNo);
                command.Parameters.AddWithValue("@TodayPrefix", "P" + sealNo + "%");

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        int pass = Convert.ToInt32(reader["PassScan"]);
                        int fail = Convert.ToInt32(reader["FailScan"]);

                        return new DashboardScanStats
                        {
                            // Tá»•ng scan = PASS + NG.
                            // KhĂ´ng dĂ¹ng COUNT(1) Ä‘á»ƒ trĂ¡nh lá»‡ch náº¿u sau nĂ y cĂ³ báº£n ghi loáº¡i khĂ¡c.
                            Total = Math.Max(0, pass) + Math.Max(0, fail),
                            Pass = pass,
                            Fail = fail
                        };
                    }
                }
            }

            return new DashboardScanStats();
        }

        public ObservableCollection<ScanHistory> GetByBoxName(string boxname)
        {
            ObservableCollection<ScanHistory> scanHistoryItems = new ObservableCollection<ScanHistory>();
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                string selectQuery = "SELECT " +
                    ScanResultMapper.BuildSelectColumns(connection) +
                    " FROM ScanHistoryView WHERE BoxName = @BoxName ORDER BY ID DESC";
                using (SQLiteCommand command = new SQLiteCommand(selectQuery, connection))
                {
                    command.Parameters.AddWithValue("@BoxName", boxname);
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            scanHistoryItems.Add(ScanResultMapper.Read(reader));
                        }
                    }
                }
            }
            return scanHistoryItems;
        }

        public void InsertScanHistory(ScanHistory scanHistory)
        {
            try
            {
                using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                {
                    InsertScanHistory(scanHistory, connection, transaction);
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Loi insert ScanHistory vao " + DatabaseRepository.DatabasePath +
                    ". Result=" + (scanHistory.ScanResult ? "PASS" : "NG") +
                    ", BoxName=" + scanHistory.BoxName +
                    ", PartNumber=" + scanHistory.ProductPartNumber +
                    ", ScanData=" + scanHistory.ScanData +
                    ". Chi tiet: " + ex);
                throw;
            }
        }

        internal void InsertScanHistory(
            ScanHistory scanHistory,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (scanHistory == null)
                throw new ArgumentNullException(nameof(scanHistory));
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            const string insertQuery = @"
                INSERT INTO ScanHistoryView (ScanTime, BoxDate, ScanLabelDate, BoxName, ProductPartNumber, ProductPartName, SealNo, LotNo, ScanData, ScanResult, ScanMessage, ScanWorker, BoxType, IsPartialBox, ActualQty, TargetQty)
                VALUES (@ScanTime, @BoxDate, @ScanLabelDate, @BoxName, @ProductPartNumber, @ProductPartName, @SealNo, @LotNo, @ScanData, @ScanResult, @ScanMessage, @ScanWorker, @BoxType, @IsPartialBox, @ActualQty, @TargetQty)";
            using (SQLiteCommand command = new SQLiteCommand(insertQuery, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@ScanTime",
                    scanHistory.ScanTime.HasValue
                        ? (object)scanHistory.ScanTime.Value
                        : DBNull.Value);
                command.Parameters.AddWithValue(
                    "@BoxDate",
                    scanHistory.BoxDate.HasValue
                        ? (object)scanHistory.BoxDate.Value.Date
                        : DBNull.Value);
                command.Parameters.AddWithValue(
                    "@ScanLabelDate",
                    scanHistory.ScanLabelDate.HasValue
                        ? (object)scanHistory.ScanLabelDate.Value.Date
                        : DBNull.Value);
                command.Parameters.AddWithValue("@BoxName", scanHistory.BoxName);
                command.Parameters.AddWithValue("@ProductPartNumber", scanHistory.ProductPartNumber);
                command.Parameters.AddWithValue("@ProductPartName", scanHistory.ProductPartName);
                command.Parameters.AddWithValue("@SealNo", scanHistory.SealNo);
                command.Parameters.AddWithValue("@LotNo", scanHistory.LotNo);
                command.Parameters.AddWithValue("@ScanData", scanHistory.ScanData);
                command.Parameters.AddWithValue("@ScanResult", scanHistory.ScanResult ? 1 : 0);
                command.Parameters.AddWithValue("@ScanMessage", scanHistory.ScanMessage);
                command.Parameters.AddWithValue("@ScanWorker", scanHistory.ScanWorker);
                command.Parameters.AddWithValue("@BoxType", scanHistory.BoxType);
                command.Parameters.AddWithValue("@IsPartialBox", scanHistory.IsPartialBox ? 1 : 0);
                command.Parameters.AddWithValue("@ActualQty", scanHistory.ActualQty);
                command.Parameters.AddWithValue("@TargetQty", scanHistory.TargetQty);
                command.ExecuteNonQuery();
            }
        }

        public void UpdateBoxDateByBoxName(string boxName, DateTime boxDate)
        {
            if (string.IsNullOrWhiteSpace(boxName))
                return;

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE ScanHistoryView
                    SET BoxDate = @BoxDate
                    WHERE BoxName = @BoxName";
                command.Parameters.AddWithValue("@BoxDate", boxDate.Date);
                command.Parameters.AddWithValue("@BoxName", boxName);
                command.ExecuteNonQuery();
            }
        }

        public bool ScanDataExists(string scanData)
        {
            if (string.IsNullOrWhiteSpace(scanData))
                return false;

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT 1
                    FROM ScanHistoryView
                    WHERE ScanResult = 1
                      AND ScanData = @ScanData COLLATE NOCASE
                    LIMIT 1";
                command.Parameters.AddWithValue("@ScanData", scanData.Trim());
                return command.ExecuteScalar() != null;
            }
        }

        internal static bool IsUniqueScanDataViolation(SQLiteException exception)
        {
            if (exception == null)
                return false;

            string details = exception.ToString();
            return details.IndexOf("DUPLICATE_SCAN_DATA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                details.IndexOf("UX_ScanHistoryView_PassScanData", StringComparison.OrdinalIgnoreCase) >= 0 ||
                details.IndexOf(
                    "UNIQUE constraint failed: ScanHistoryView.ScanData",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsDuplicatePassLotViolation(SQLiteException exception)
        {
            return exception != null &&
                exception.ToString().IndexOf(
                    "DUPLICATE_PASS_LOT",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void UpdateScanHistory(ScanHistory scanHistory)
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                string updateQuery = @"
                    UPDATE ScanHistoryView
                    SET ScanTime = @ScanTime, BoxName = @BoxName, ProductPartNumber = @ProductPartNumber, ProductPartName = @ProductPartName, SealNo = @SealNo, LotNo = @LotNo,
                        ScanData = @ScanData, ScanResult = @ScanResult, ScanMessage = @ScanMessage, ScanWorker = @ScanWorker,
                        BoxType = @BoxType, IsPartialBox = @IsPartialBox
                    WHERE ID = @ID";
                using (SQLiteCommand command = new SQLiteCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@ScanTime",
                        scanHistory.ScanTime.HasValue
                            ? (object)scanHistory.ScanTime.Value
                            : DBNull.Value);
                    command.Parameters.AddWithValue("@BoxName", scanHistory.BoxName);
                    command.Parameters.AddWithValue("@ProductPartNumber", scanHistory.ProductPartNumber);
                    command.Parameters.AddWithValue("@ProductPartName", scanHistory.ProductPartName);
                    command.Parameters.AddWithValue("@SealNo", scanHistory.SealNo);
                    command.Parameters.AddWithValue("@LotNo", scanHistory.LotNo);
                    command.Parameters.AddWithValue("@ScanData", scanHistory.ScanData);
                    command.Parameters.AddWithValue("@ScanResult", scanHistory.ScanResult ? 1 : 0);
                    command.Parameters.AddWithValue("@ScanMessage", scanHistory.ScanMessage);
                    command.Parameters.AddWithValue("@ScanWorker", scanHistory.ScanWorker);
                    command.Parameters.AddWithValue("@BoxType", scanHistory.BoxType);
                    command.Parameters.AddWithValue("@IsPartialBox", scanHistory.IsPartialBox ? 1 : 0);
                    command.Parameters.AddWithValue("@ID", scanHistory.ID);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public void DeleteScanHistory(int id)
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                string deleteQuery = "DELETE FROM ScanHistoryView WHERE ID = @ID";
                using (SQLiteCommand command = new SQLiteCommand(deleteQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@ID", id);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public void DeleteByBoxName(string boxName)
        {
            if (string.IsNullOrWhiteSpace(boxName))
            {
                return;
            }

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            using (SQLiteCommand command = new SQLiteCommand(
                "DELETE FROM ScanHistoryView WHERE BoxName = @BoxName",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@BoxName", boxName);
                command.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        public ObservableCollection<ScanHistory> GetNotComplete(string boxname)
        {
            ObservableCollection<ScanHistory> notCompleteScans = new ObservableCollection<ScanHistory>();
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                string query = "SELECT " + ScanResultMapper.BuildSelectColumns(connection) + " FROM ScanHistoryView WHERE BoxName = @BoxName";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BoxName", boxname);
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            notCompleteScans.Add(ScanResultMapper.Read(reader));
                        }
                    }
                }
            }
            return notCompleteScans;
        }

        public bool CheckExist(string productPartNumber, string sealno, string lotno)
        {
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT 1
                    FROM ScanHistoryView
                    WHERE ProductPartNumber = @ProductPartNumber COLLATE NOCASE
                      AND SealNo = @SealNo
                      AND LotNo = @LotNo
                      AND ScanResult = 1
                    LIMIT 1";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProductPartNumber", productPartNumber);
                    command.Parameters.AddWithValue("@SealNo", sealno);
                    command.Parameters.AddWithValue("@LotNo", lotno);
                    return command.ExecuteScalar() != null;
                }
            }
        }

        public List<string> GetDistinctBoxNames()
        {
            List<string> boxNames = new List<string>() { "All" };
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT BoxName
                    FROM ScanHistoryView
                    WHERE BoxName IS NOT NULL
                      AND TRIM(BoxName) <> ''
                    GROUP BY BoxName COLLATE NOCASE
                    ORDER BY MAX(ID) DESC, BoxName DESC";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string boxName = reader["BoxName"].ToString();
                            if (!string.IsNullOrEmpty(boxName))
                            {
                                boxNames.Add(boxName);
                            }
                        }
                    }
                }
            }
            return boxNames;
        }

        public List<string> GetDistinctSealNos()
        {
            List<string> sealNos = new List<string>() { "All" };
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT SealNo
                    FROM ScanHistoryView
                    WHERE SealNo IS NOT NULL
                      AND TRIM(SealNo) <> ''
                    GROUP BY SealNo
                    ORDER BY MAX(ID) DESC, SealNo DESC";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string SealNo = reader["SealNo"].ToString();
                            if (!string.IsNullOrEmpty(SealNo))
                            {
                                sealNos.Add(SealNo);
                            }
                        }
                    }
                }
            }
            return sealNos;
        }

        public List<string> GetDistinctProductNumbers()
        {
            List<string> productNumbers = new List<string>() { "All" };
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT DISTINCT ProductPartNumber FROM ScanHistoryView WHERE ProductPartNumber IS NOT NULL AND ProductPartNumber <> '' ORDER BY ProductPartNumber";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string productNumber = reader["ProductPartNumber"].ToString();
                            if (!string.IsNullOrEmpty(productNumber))
                            {
                                productNumbers.Add(productNumber);
                            }
                        }
                    }
                }
            }
            return productNumbers;
        }

        public List<string> GetDistinctNGMessage()
        {
            List<string> productNumbers = new List<string>() { "All" };
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT DISTINCT ScanMessage
                    FROM ScanHistoryView
                    WHERE ScanResult = 0
                      AND ScanMessage IS NOT NULL
                      AND TRIM(ScanMessage) <> ''
                    ORDER BY ScanMessage COLLATE NOCASE";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string scanMessage = reader["ScanMessage"].ToString();
                            if (!string.IsNullOrEmpty(scanMessage) && !productNumbers.Contains(scanMessage))
                            {
                                productNumbers.Add(scanMessage);
                            }
                        }
                    }
                }
            }
            return productNumbers;
        }

        public List<string> GetPartNumberSuggestions(string keyword, int limit = 30)
        {
            List<string> partNumbers = new List<string>();
            int safeLimit = Math.Max(1, Math.Min(limit, 100));
            string searchText = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            {
                string query;
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    query = @"
                        SELECT ProductPartNumber
                        FROM ScanHistoryView
                        WHERE ProductPartNumber IS NOT NULL AND ProductPartNumber <> ''
                        GROUP BY ProductPartNumber
                        ORDER BY MAX(ID) DESC
                        LIMIT 20";
                }
                else
                {
                    query = @"
                        SELECT DISTINCT ProductPartNumber
                        FROM ScanHistoryView
                        WHERE ProductPartNumber IS NOT NULL
                          AND ProductPartNumber <> ''
                          AND ProductPartNumber COLLATE NOCASE LIKE @Keyword ESCAPE '\'
                        ORDER BY ProductPartNumber
                        LIMIT @Limit";
                }

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        command.Parameters.AddWithValue("@Limit", safeLimit);
                        command.Parameters.AddWithValue("@Keyword", "%" + EscapeLikeValue(searchText) + "%");
                    }

                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string partNumber = reader["ProductPartNumber"].ToString();
                            if (!string.IsNullOrWhiteSpace(partNumber))
                            {
                                partNumbers.Add(partNumber);
                            }
                        }
                    }
                }
            }

            return partNumbers;
        }

    }
}
