using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using SX3_SCANER.Helper;

namespace SX3_SCANER.Model.Respository
{
    internal static class DatabaseRepository
    {
        public const string DatabaseFileName = "database.db";
        public const string ProductDatabaseFileName = "product.db";
        private const string CompanyDirectoryName = "JBZVN";
        private const string ProductDirectoryName = "SX3 Scanner";
        private const string LocalDataDirectoryName = "SX3_SCANER";

        public static string ApplicationDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    LocalDataDirectoryName);
            }
        }

        private static string PreviousProgramDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    CompanyDirectoryName,
                    ProductDirectoryName);
            }
        }

        public static string AppDataDirectory
        {
            get { return ApplicationDataDirectory; }
        }

        public static string DatabasePath
        {
            get { return Path.Combine(ApplicationDataDirectory, DatabaseFileName); }
        }

        public static string MainDatabasePath
        {
            get { return DatabasePath; }
        }

        public static string ProductDatabasePath
        {
            get { return Path.Combine(ApplicationDataDirectory, ProductDatabaseFileName); }
        }

        public static string BackupDirectory
        {
            get { return Path.Combine(ApplicationDataDirectory, "Backup"); }
        }

        public static string RuntimeConfigPath
        {
            get { return Path.Combine(ApplicationDataDirectory, "config", "runtime-settings.json"); }
        }

        public static string UpdateCacheDirectory
        {
            get { return Path.Combine(ApplicationDataDirectory, "cache", "updates"); }
        }

        public static string ConnectionString
        {
            get
            {
                return "Data Source=" + DatabasePath + ";Version=3;Foreign Keys=True;Journal Mode=WAL;Default Timeout=5;";
            }
        }

        public static string ProductConnectionString
        {
            get
            {
                return "Data Source=" + ProductDatabasePath + ";Version=3;Foreign Keys=True;Journal Mode=WAL;Default Timeout=5;";
            }
        }

        public static SQLiteConnection CreateConnection()
        {
            EnsureDatabaseFiles();

            SQLiteConnection connection = null;
            try
            {
                connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
                    command.ExecuteNonQuery();
                }
                return connection;
            }
            catch (SQLiteException ex)
            {
                if (connection != null)
                {
                    connection.Dispose();
                }

                StartupManager.SetStatus("Lỗi database: không mở được database.db.");
                StartupManager.Log("Loi mo database tai: " + DatabasePath + ". Chi tiet: " + ex);
                throw;
            }
        }

        public static SQLiteConnection CreateProductConnection()
        {
            EnsureDatabaseFiles();

            SQLiteConnection connection = null;
            try
            {
                connection = new SQLiteConnection(ProductConnectionString);
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
                    command.ExecuteNonQuery();
                }
                return connection;
            }
            catch (SQLiteException ex)
            {
                if (connection != null)
                {
                    connection.Dispose();
                }

                StartupManager.SetStatus("Lỗi database: không mở được product.db.");
                StartupManager.Log("Loi mo product database tai: " + ProductDatabasePath + ". Chi tiet: " + ex);
                throw;
            }
        }

        public static bool DatabaseExists()
        {
            return File.Exists(DatabasePath);
        }

        public static void EnsureApplicationDataDirectory()
        {
            Directory.CreateDirectory(ApplicationDataDirectory);
            RestoreMissingFilesFromPreviousProgramData();
            StartupManager.LogOnce("appdata-path", "AppData directory: " + ApplicationDataDirectory);
            StartupManager.LogOnce("main-db-path", "database.db path dang dung: " + DatabasePath);
            StartupManager.LogOnce("product-db-path", "product.db path dang dung: " + ProductDatabasePath);
        }

        private static void RestoreMissingFilesFromPreviousProgramData()
        {
            if (!Directory.Exists(PreviousProgramDataDirectory))
            {
                return;
            }

            RestoreDatabaseFamilyIfMissing(DatabaseFileName);
            RestoreDatabaseFamilyIfMissing(ProductDatabaseFileName);
        }

        private static void RestoreDatabaseFamilyIfMissing(string databaseFileName)
        {
            string destinationDatabasePath =
                Path.Combine(ApplicationDataDirectory, databaseFileName);

            if (File.Exists(destinationDatabasePath))
            {
                StartupManager.LogOnce(
                    "keep-local-data:" + databaseFileName,
                    "Giu nguyen file du lieu hien co, khong ghi de: " +
                    destinationDatabasePath);
                return;
            }

            string sourceDatabasePath =
                Path.Combine(PreviousProgramDataDirectory, databaseFileName);

            if (!File.Exists(sourceDatabasePath))
            {
                return;
            }

            foreach (string suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                string fileName = databaseFileName + suffix;
                string sourcePath = Path.Combine(PreviousProgramDataDirectory, fileName);
                string destinationPath = Path.Combine(ApplicationDataDirectory, fileName);

                if (File.Exists(sourcePath) && !File.Exists(destinationPath))
                {
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                    StartupManager.Log(
                        "Da phuc hoi file con thieu tu ProgramData sang LocalAppData: " +
                        sourcePath + " -> " + destinationPath);
                }
            }
        }

        public static void EnsureDatabaseFiles()
        {
            EnsureApplicationDataDirectory();

            EnsureDatabaseFile(DatabaseFileName, DatabasePath);
            EnsureDatabaseFile(ProductDatabaseFileName, ProductDatabasePath);
        }

        public static void ValidateDatabasePaths()
        {
            string expectedDirectory = Path.GetFullPath(ApplicationDataDirectory);
            ValidatePathInAppData(DatabaseFileName, DatabasePath, expectedDirectory);
            ValidatePathInAppData(ProductDatabaseFileName, ProductDatabasePath, expectedDirectory);

        }

        private static void ValidatePathInAppData(string fileName, string path, string expectedDirectory)
        {
            string fullPath = Path.GetFullPath(path);
            string fullDirectory = Path.GetFullPath(Path.GetDirectoryName(fullPath));

            if (!string.Equals(fullDirectory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    fileName + " dang tro sai thu muc. Path=" + fullPath + ", ExpectedDirectory=" + expectedDirectory);
            }
        }

        public static void BackupExistingDatabaseFiles(string reason)
        {
            BackupDatabaseFile(DatabasePath, reason);
            BackupDatabaseFile(ProductDatabasePath, reason);
        }

        public static void BackupDatabaseFile(string databasePath, string reason)
        {
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                return;
            }

            CheckpointDatabaseBeforeBackup(databasePath);
            Directory.CreateDirectory(BackupDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = Path.GetFileNameWithoutExtension(databasePath) + "_" + timestamp + Path.GetExtension(databasePath);
            string destinationPath = GetUniqueBackupPath(Path.Combine(BackupDirectory, fileName));

            File.Copy(databasePath, destinationPath, overwrite: false);
            StartupManager.Log("Da backup database truoc migration/schema update. Reason=" + reason + ". " + databasePath + " -> " + destinationPath);
        }

        private static void CheckpointDatabaseBeforeBackup(string databasePath)
        {
            Func<SQLiteConnection> connectionFactory = string.Equals(
                Path.GetFullPath(databasePath),
                Path.GetFullPath(ProductDatabasePath),
                StringComparison.OrdinalIgnoreCase)
                    ? (Func<SQLiteConnection>)CreateProductConnection
                    : CreateConnection;

            using (SQLiteConnection connection = connectionFactory())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA wal_checkpoint(FULL);";
                command.ExecuteNonQuery();
            }
        }

        private static string GetUniqueBackupPath(string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            string directory = Path.GetDirectoryName(destinationPath);
            string name = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);

            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, name + "_" + i + extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("Khong the tao ten file backup duy nhat: " + destinationPath);
        }

        public static void RunIntegrityCheck()
        {
            RunIntegrityCheck(DatabaseFileName, CreateConnection);
            RunIntegrityCheck(ProductDatabaseFileName, CreateProductConnection);
        }

        private static void RunIntegrityCheck(string fileName, Func<SQLiteConnection> connectionFactory)
        {
            using (var connection = connectionFactory())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check;";
                string result = Convert.ToString(command.ExecuteScalar());

                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    StartupManager.SetStatus("Lỗi database: dữ liệu không toàn vẹn.");
                    StartupManager.Log(fileName + " integrity_check failed: " + result);
                    throw new InvalidOperationException(fileName + " integrity_check failed: " + result);
                }

                StartupManager.LogOnce(
                    "integrity-check:" + fileName,
                    fileName + " integrity_check: ok");
            }
        }

        private static bool EnsureDatabaseFile(string fileName, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                StartupManager.LogOnce(
                    string.Equals(fileName, DatabaseFileName, StringComparison.OrdinalIgnoreCase)
                        ? "main-db-exists"
                        : "product-db-exists",
                    fileName + " da ton tai trong AppData, dung file hien co va khong ghi de.");
                return true;
            }

            string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(templatePath))
            {
                StartupManager.LogOnce(
                    "database-template-missing:" + fileName,
                    fileName + " chua co trong AppData va khong tim thay database mau tai: " + templatePath);
                return false;
            }

            if (string.Equals(
                Path.GetFullPath(templatePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
            {
                StartupManager.LogOnce(
                    "database-template-same-path:" + fileName,
                    fileName + " template va file dich trung nhau, khong copy: " + destinationPath);
                return File.Exists(destinationPath);
            }

            try
            {
                File.Copy(templatePath, destinationPath, overwrite: false);
                StartupManager.Log("Da copy " + fileName + " lan dau tu " + templatePath + " sang " + destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                StartupManager.Log("Loi copy " + fileName + " lan dau tu " + templatePath + " sang " + destinationPath + ". Chi tiet: " + ex);
                throw;
            }
        }

        public static List<string> GetListTableName()
        {
            var tableNames = new List<string>();
            using (var connection = CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type='table'
  AND name NOT LIKE 'sqlite_%'
ORDER BY name";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tableNames.Add(Convert.ToString(reader["name"]));
                    }
                }
            }
            return tableNames;
        }

        public static bool TableExists(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return false;
            using (var connection = CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name";
                command.Parameters.AddWithValue("@name", tableName.Trim());
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        public static DataTable GetTableSchema(string tableName)
        {
            string safeTableName = EnsureSafeExistingTableName(tableName);
            using (var connection = CreateConnection())
            using (var command = connection.CreateCommand())
            using (var adapter = new SQLiteDataAdapter(command))
            {
                command.CommandText = $"PRAGMA table_info([{safeTableName}])";
                var schemaTable = new DataTable();
                adapter.Fill(schemaTable);
                return schemaTable;
            }
        }

        public static DataTable GetTopOrBottomRows(string tableName, int rowCount = 200, bool getTop = true)
        {
            string safeTableName = EnsureSafeExistingTableName(tableName);
            int safeLimit = Math.Max(1, Math.Min(rowCount, 5000));

            var dataTable = new DataTable();
            using (var connection = CreateConnection())
            using (var command = connection.CreateCommand())
            using (var adapter = new SQLiteDataAdapter(command))
            {
                command.CommandText = getTop
                    ? $"SELECT * FROM [{safeTableName}] LIMIT @limit"
                    : $"SELECT * FROM [{safeTableName}] ORDER BY ROWID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@limit", safeLimit);
                adapter.Fill(dataTable);
            }
            return dataTable;
        }

        public static int ExecuteNonQuery(string sql, params SQLiteParameter[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL command is empty.", nameof(sql));
            using (var connection = CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                if (parameters != null && parameters.Length > 0)
                    command.Parameters.AddRange(parameters);
                return command.ExecuteNonQuery();
            }
        }

        private static string EnsureSafeExistingTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Tên bảng trống.", nameof(tableName));

            string trimmed = tableName.Trim();
            bool validChars = trimmed.All(c => char.IsLetterOrDigit(c) || c == '_');
            if (!validChars)
                throw new ArgumentException("Tên bảng không hợp lệ.", nameof(tableName));

            if (!TableExists(trimmed))
                throw new ArgumentException($"Không tìm thấy bảng: {trimmed}", nameof(tableName));

            return trimmed;
        }
    }
}
