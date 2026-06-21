using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace SX3_SCANER.Model.Respository
{
    internal interface IScanSessionService
    {
        void SaveCurrentSession(ScanSessionState state);
        ScanSessionState LoadSession(string productCode, DateTime sessionDate);
        bool HasSession(string productCode, DateTime sessionDate);
        void RemoveSession(string productCode, DateTime sessionDate);
    }

    internal sealed class ScanSessionService : IScanSessionService
    {
        internal static void CreateTableIfNotExists()
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ScanSessionDrafts (
                        SessionKey TEXT PRIMARY KEY,
                        ProductCode TEXT NOT NULL,
                        BoxCode TEXT NOT NULL DEFAULT '',
                        ScannedCount INTEGER NOT NULL DEFAULT 0,
                        TargetCount INTEGER NOT NULL DEFAULT 0,
                        ScanHistoryJson TEXT NOT NULL DEFAULT '[]',
                        IsInJob INTEGER NOT NULL DEFAULT 0,
                        Worker TEXT NOT NULL DEFAULT '',
                        SessionDate TEXT NOT NULL,
                        BoxDate TEXT,
                        ScanLabelDate TEXT,
                        LastUpdated TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }

            EnsureColumn("BoxDate", "TEXT");
            EnsureColumn("ScanLabelDate", "TEXT");
        }

        public void SaveCurrentSession(ScanSessionState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.ProductCode))
            {
                return;
            }

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                SaveCurrentSession(state, connection, transaction);
                transaction.Commit();
            }
        }

        internal void SaveCurrentSession(
            ScanSessionState state,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.ProductCode))
                return;

            state.SessionDate = state.SessionDate.Date;
            state.SessionKey = BuildSessionKey(state.ProductCode, state.SessionDate);
            state.LastUpdated = DateTime.Now;
            // Scan history is already persisted transactionally in ScanHistoryView.
            // Keep the legacy column for backward compatibility without rewriting the
            // complete box history on every scan.
            const string historyJson = "[]";

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT OR REPLACE INTO ScanSessionDrafts
                        (SessionKey, ProductCode, BoxCode, ScannedCount, TargetCount, ScanHistoryJson, IsInJob, Worker, SessionDate, BoxDate, ScanLabelDate, LastUpdated)
                    VALUES
                        (@SessionKey, @ProductCode, @BoxCode, @ScannedCount, @TargetCount, @ScanHistoryJson, @IsInJob, @Worker, @SessionDate, @BoxDate, @ScanLabelDate, @LastUpdated);";
                AddStateParameters(command, state, historyJson);
                command.ExecuteNonQuery();
            }
        }

        public ScanSessionState LoadSession(string productCode, DateTime sessionDate)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return null;
            }

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ScanSessionDrafts WHERE SessionKey = @SessionKey";
                command.Parameters.AddWithValue("@SessionKey", BuildSessionKey(productCode, sessionDate));

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new ScanSessionState
                    {
                        SessionKey = Convert.ToString(reader["SessionKey"]),
                        ProductCode = Convert.ToString(reader["ProductCode"]),
                        BoxCode = Convert.ToString(reader["BoxCode"]),
                        ScannedCount = Convert.ToInt32(reader["ScannedCount"]),
                        TargetCount = Convert.ToInt32(reader["TargetCount"]),
                        ScanHistoryItems = new List<ScanHistory>(),
                        IsInJob = Convert.ToInt32(reader["IsInJob"]) != 0,
                        Worker = Convert.ToString(reader["Worker"]),
                        SessionDate = DateTime.Parse(Convert.ToString(reader["SessionDate"])),
                        BoxDate = ReadDate(reader, "BoxDate", "SessionDate"),
                        ScanLabelDate = ReadDate(reader, "ScanLabelDate", "SessionDate"),
                        LastUpdated = DateTime.Parse(Convert.ToString(reader["LastUpdated"]))
                    };
                }
            }
        }

        public bool HasSession(string productCode, DateTime sessionDate)
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM ScanSessionDrafts WHERE SessionKey = @SessionKey";
                command.Parameters.AddWithValue("@SessionKey", BuildSessionKey(productCode, sessionDate));
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        public ScanSessionState LoadLatestSession(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return null;

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT *
                    FROM ScanSessionDrafts
                    WHERE ProductCode = @ProductCode COLLATE NOCASE
                    ORDER BY LastUpdated DESC
                    LIMIT 1";
                command.Parameters.AddWithValue("@ProductCode", productCode.Trim());

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new ScanSessionState
                    {
                        SessionKey = Convert.ToString(reader["SessionKey"]),
                        ProductCode = Convert.ToString(reader["ProductCode"]),
                        BoxCode = Convert.ToString(reader["BoxCode"]),
                        ScannedCount = Convert.ToInt32(reader["ScannedCount"]),
                        TargetCount = Convert.ToInt32(reader["TargetCount"]),
                        ScanHistoryItems = new List<ScanHistory>(),
                        IsInJob = Convert.ToInt32(reader["IsInJob"]) != 0,
                        Worker = Convert.ToString(reader["Worker"]),
                        SessionDate = DateTime.Parse(Convert.ToString(reader["SessionDate"])),
                        BoxDate = ReadDate(reader, "BoxDate", "SessionDate"),
                        ScanLabelDate = ReadDate(reader, "ScanLabelDate", "SessionDate"),
                        LastUpdated = DateTime.Parse(Convert.ToString(reader["LastUpdated"]))
                    };
                }
            }
        }

        public void RemoveSession(string productCode, DateTime sessionDate)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return;
            }

            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                RemoveSession(productCode, sessionDate, connection, transaction);
                transaction.Commit();
            }
        }

        internal void RemoveSession(
            string productCode,
            DateTime sessionDate,
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return;

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM ScanSessionDrafts WHERE SessionKey = @SessionKey";
                command.Parameters.AddWithValue(
                    "@SessionKey",
                    BuildSessionKey(productCode, sessionDate));
                command.ExecuteNonQuery();
            }
        }

        internal static string BuildSessionKey(string productCode, DateTime sessionDate)
        {
            return (productCode ?? string.Empty).Trim().ToUpperInvariant() + "|" + sessionDate.ToString("yyyyMMdd");
        }

        private static void AddStateParameters(SQLiteCommand command, ScanSessionState state, string historyJson)
        {
            command.Parameters.AddWithValue("@SessionKey", state.SessionKey);
            command.Parameters.AddWithValue("@ProductCode", state.ProductCode ?? string.Empty);
            command.Parameters.AddWithValue("@BoxCode", state.BoxCode ?? string.Empty);
            command.Parameters.AddWithValue("@ScannedCount", state.ScannedCount);
            command.Parameters.AddWithValue("@TargetCount", state.TargetCount);
            command.Parameters.AddWithValue("@ScanHistoryJson", historyJson);
            command.Parameters.AddWithValue("@IsInJob", state.IsInJob ? 1 : 0);
            command.Parameters.AddWithValue("@Worker", state.Worker ?? string.Empty);
            command.Parameters.AddWithValue("@SessionDate", state.SessionDate.ToString("O"));
            command.Parameters.AddWithValue("@BoxDate", state.BoxDate.ToString("O"));
            command.Parameters.AddWithValue("@ScanLabelDate", state.ScanLabelDate.ToString("O"));
            command.Parameters.AddWithValue("@LastUpdated", state.LastUpdated.ToString("O"));
        }

        private static void EnsureColumn(string columnName, string definition)
        {
            using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(ScanSessionDrafts)";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(
                            Convert.ToString(reader["name"]),
                            columnName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }

                command.CommandText =
                    "ALTER TABLE ScanSessionDrafts ADD COLUMN " +
                    columnName + " " + definition;
                command.ExecuteNonQuery();
            }
        }

        private static DateTime ReadDate(
            SQLiteDataReader reader,
            string columnName,
            string fallbackColumnName)
        {
            DateTime value;
            string text = Convert.ToString(reader[columnName]);
            if (DateTime.TryParse(text, out value))
                return value.Date;

            return DateTime.Parse(Convert.ToString(reader[fallbackColumnName])).Date;
        }
    }
}
