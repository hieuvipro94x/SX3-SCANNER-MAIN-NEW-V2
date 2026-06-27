using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace SX3_SCANER.Helper
{
    internal sealed class BackupOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string LocalBackupPath { get; set; }
        public string NetworkBackupPath { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    internal static class DataBackupService
    {
        private const string BackupEnabledKey = "BackupEnabled";
        private const string BackupNetworkPathKey = "BackupNetworkPath";
        private const string BackupRetentionDaysKey = "BackupRetentionDays";
        private const string LastDailyBackupDateKey = "LastDailyBackupDate";
        private const string LastBackupAtKey = "LastBackupAt";
        private const string LastBackupStatusKey = "LastBackupStatus";
        private const int DefaultRetentionDays = 30;
        private const int MaxRetentionDays = 3650;
        private static readonly object BackupSync = new object();
        private static bool _isBackupRunning;

        internal static void EnsureDefaultSettings()
        {
            AppConfigHelper.EnsureCreate(BackupEnabledKey, "1");
            AppConfigHelper.EnsureCreate(BackupNetworkPathKey, string.Empty);
            AppConfigHelper.EnsureCreate(BackupRetentionDaysKey, DefaultRetentionDays.ToString());
            AppConfigHelper.EnsureCreate(LastDailyBackupDateKey, string.Empty);
            AppConfigHelper.EnsureCreate(LastBackupAtKey, string.Empty);
            AppConfigHelper.EnsureCreate(LastBackupStatusKey, "Chưa backup dữ liệu.");
        }

        internal static bool IsBackupEnabled()
        {
            string value = AppConfigHelper.Read(BackupEnabledKey);
            return value == null || value.Trim() == "" || value.Trim() == "1" ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetBackupEnabled(bool enabled)
        {
            AppConfigHelper.Modify(BackupEnabledKey, enabled ? "1" : "0");
        }

        internal static string GetNetworkBackupPath()
        {
            return (AppConfigHelper.Read(BackupNetworkPathKey) ?? string.Empty).Trim();
        }

        internal static void SetNetworkBackupPath(string path)
        {
            AppConfigHelper.Modify(BackupNetworkPathKey, (path ?? string.Empty).Trim());
        }

        internal static int GetRetentionDays()
        {
            int days;
            if (!int.TryParse(AppConfigHelper.Read(BackupRetentionDaysKey), out days))
            {
                days = DefaultRetentionDays;
            }

            if (days < 1) days = 1;
            if (days > MaxRetentionDays) days = MaxRetentionDays;
            return days;
        }

        internal static void SetRetentionDays(int days)
        {
            if (days < 1) days = 1;
            if (days > MaxRetentionDays) days = MaxRetentionDays;
            AppConfigHelper.Modify(BackupRetentionDaysKey, days.ToString());
        }

        internal static string GetLastBackupAt()
        {
            return AppConfigHelper.Read(LastBackupAtKey) ?? string.Empty;
        }

        internal static string GetLastBackupStatus()
        {
            return AppConfigHelper.Read(LastBackupStatusKey) ?? string.Empty;
        }

        internal static Task<BackupOperationResult> RunDailyBackupIfDueAsync()
        {
            return Task.Run(() => RunDailyBackupIfDue());
        }

        internal static BackupOperationResult RunDailyBackupIfDue()
        {
            EnsureDefaultSettings();

            if (!IsBackupEnabled())
            {
                return new BackupOperationResult
                {
                    Success = true,
                    Message = "Backup tự động đang tắt.",
                    CompletedAt = DateTime.Now
                };
            }

            string todayKey = DateTime.Today.ToString("yyyyMMdd");
            string lastBackupDate = (AppConfigHelper.Read(LastDailyBackupDateKey) ?? string.Empty).Trim();
            if (string.Equals(lastBackupDate, todayKey, StringComparison.OrdinalIgnoreCase))
            {
                return new BackupOperationResult
                {
                    Success = true,
                    Message = "Hôm nay đã backup rồi.",
                    CompletedAt = DateTime.Now
                };
            }

            BackupOperationResult result = CreateBackup("daily", true);
            if (result.Success)
            {
                AppConfigHelper.Modify(LastDailyBackupDateKey, todayKey);
            }

            return result;
        }

        internal static Task<BackupOperationResult> CreateManualBackupAsync()
        {
            return Task.Run(() => CreateBackup("manual", true));
        }

        internal static BackupOperationResult CreateBackup(string reason, bool includeNetworkBackup)
        {
            lock (BackupSync)
            {
                if (_isBackupRunning)
                {
                    return new BackupOperationResult
                    {
                        Success = false,
                        Message = "Đang có tác vụ backup khác chạy. Vui lòng thử lại sau.",
                        CompletedAt = DateTime.Now
                    };
                }

                _isBackupRunning = true;
            }

            IDisposable databaseActivity = null;
            try
            {
                databaseActivity = DatabaseMaintenanceCoordinator.EnterOperation(
                    "backup database");
                EnsureDefaultSettings();
                string safeReason = MakeSafeFileName(string.IsNullOrWhiteSpace(reason) ? "backup" : reason.Trim());
                string fileName = "SX3_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + safeReason + ".zip";

                string localPath = CreateBackupZip(DatabaseRepository.BackupDirectory, fileName);
                CleanupOldBackups(DatabaseRepository.BackupDirectory, GetRetentionDays());

                string networkPath = string.Empty;
                string networkDirectory = GetNetworkBackupPath();
                string warning = string.Empty;
                if (includeNetworkBackup && !string.IsNullOrWhiteSpace(networkDirectory))
                {
                    try
                    {
                        networkPath = CreateBackupZip(networkDirectory, fileName);
                        CleanupOldBackups(networkDirectory, GetRetentionDays());
                    }
                    catch (Exception ex)
                    {
                        warning = " Backup local OK nhưng backup thư mục mạng lỗi: " + ex.Message;
                        StartupManager.Log("Backup network failed: " + ex);
                    }
                }

                string message = "Backup OK: " + localPath;
                if (!string.IsNullOrWhiteSpace(networkPath))
                {
                    message += " | Network: " + networkPath;
                }
                message += warning;

                AppConfigHelper.Modify(LastBackupAtKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                AppConfigHelper.Modify(LastBackupStatusKey, message);
                StartupManager.Log(message);

                return new BackupOperationResult
                {
                    Success = true,
                    Message = message,
                    LocalBackupPath = localPath,
                    NetworkBackupPath = networkPath,
                    CompletedAt = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                string message = "Backup lỗi: " + ex.Message;
                AppConfigHelper.Modify(LastBackupAtKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                AppConfigHelper.Modify(LastBackupStatusKey, message);
                StartupManager.Log("Backup failed: " + ex);

                return new BackupOperationResult
                {
                    Success = false,
                    Message = message,
                    CompletedAt = DateTime.Now
                };
            }
            finally
            {
                databaseActivity?.Dispose();
                lock (BackupSync)
                {
                    _isBackupRunning = false;
                }
            }
        }

        internal static Task<BackupOperationResult> RestoreBackupAsync(string backupPath)
        {
            return Task.Run(() => RestoreBackup(backupPath));
        }

        internal static BackupOperationResult RestoreBackup(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                return new BackupOperationResult
                {
                    Success = false,
                    Message = "Không tìm thấy file backup cần phục hồi.",
                    CompletedAt = DateTime.Now
                };
            }

            try
            {
                BackupOperationResult emergencyBackup = CreateBackup("before_restore", false);
                if (emergencyBackup == null || !emergencyBackup.Success)
                {
                    return new BackupOperationResult
                    {
                        Success = false,
                        Message = "Khong the tao backup khan cap truoc khi phuc hoi. Da huy phuc hoi de tranh mat du lieu.",
                        CompletedAt = DateTime.Now
                    };
                }

                string extension = Path.GetExtension(backupPath).ToLowerInvariant();
                string tempDirectory = Path.Combine(Path.GetTempPath(), "SX3Restore_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                lock (BackupSync)
                {
                    if (_isBackupRunning)
                    {
                        TryDeleteDirectory(tempDirectory);
                        return new BackupOperationResult
                        {
                            Success = false,
                            Message = "Dang co tac vu backup/restore khac chay. Vui long thu lai sau.",
                            CompletedAt = DateTime.Now
                        };
                    }

                    _isBackupRunning = true;
                }

                try
                {
                    string sourceMainDb = null;
                    string sourceProductDb = null;

                    if (extension == ".zip")
                    {
                        ZipFile.ExtractToDirectory(backupPath, tempDirectory);
                        sourceMainDb = Directory.GetFiles(tempDirectory, DatabaseRepository.DatabaseFileName, SearchOption.AllDirectories).FirstOrDefault();
                        sourceProductDb = Directory.GetFiles(tempDirectory, DatabaseRepository.ProductDatabaseFileName, SearchOption.AllDirectories).FirstOrDefault();
                    }
                    else if (extension == ".db")
                    {
                        sourceMainDb = backupPath;
                    }
                    else
                    {
                        throw new InvalidOperationException("Chỉ hỗ trợ phục hồi file .zip hoặc .db.");
                    }

                    if (string.IsNullOrWhiteSpace(sourceMainDb) || !File.Exists(sourceMainDb))
                    {
                        throw new FileNotFoundException("Trong backup không có database.db.");
                    }

                    ValidateDatabaseFile(sourceMainDb, "database.db trong backup");
                    if (!string.IsNullOrWhiteSpace(sourceProductDb) &&
                        File.Exists(sourceProductDb))
                    {
                        ValidateDatabaseFile(sourceProductDb, "product.db trong backup");
                    }

                    using (DatabaseMaintenanceCoordinator.EnterMaintenance(
                        "restore database",
                        TimeSpan.FromSeconds(60)))
                    {
                        SQLiteConnection.ClearAllPools();
                        string mainRollbackPath = null;
                        string productRollbackPath = null;
                        try
                        {
                            mainRollbackPath = ReplaceDatabaseFile(
                                sourceMainDb,
                                DatabaseRepository.DatabasePath);

                            if (!string.IsNullOrWhiteSpace(sourceProductDb) &&
                                File.Exists(sourceProductDb))
                            {
                                productRollbackPath = ReplaceDatabaseFile(
                                    sourceProductDb,
                                    DatabaseRepository.ProductDatabasePath);
                            }

                            ValidateDatabaseFile(
                                DatabaseRepository.DatabasePath,
                                "database.db sau restore");
                            ValidateDatabaseFile(
                                DatabaseRepository.ProductDatabasePath,
                                "product.db sau restore");
                            ScanResultMapper.InvalidateSchemaCache();
                            TryDeleteFile(mainRollbackPath);
                            TryDeleteFile(productRollbackPath);
                        }
                        catch
                        {
                            RollbackDatabaseFile(
                                DatabaseRepository.DatabasePath,
                                mainRollbackPath);
                            RollbackDatabaseFile(
                                DatabaseRepository.ProductDatabasePath,
                                productRollbackPath);
                            SQLiteConnection.ClearAllPools();
                            throw;
                        }
                    }

                    string message = "Phục hồi database thành công. Vui lòng mở lại app để nạp dữ liệu mới.";
                    AppConfigHelper.Modify(LastBackupStatusKey, message);
                    StartupManager.Log(message);
                    return new BackupOperationResult
                    {
                        Success = true,
                        Message = message,
                        CompletedAt = DateTime.Now
                    };
                }
                finally
                {
                    TryDeleteDirectory(tempDirectory);
                    lock (BackupSync)
                    {
                        _isBackupRunning = false;
                    }
                }
            }
            catch (Exception ex)
            {
                string message = "Phục hồi backup lỗi: " + ex.Message;
                StartupManager.Log("Restore backup failed: " + ex);
                AppConfigHelper.Modify(LastBackupStatusKey, message);
                return new BackupOperationResult
                {
                    Success = false,
                    Message = message,
                    CompletedAt = DateTime.Now
                };
            }
        }

        internal static long GetFileSizeBytes(string path)
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0L;
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(unit == 0 ? "0" : "0.##") + " " + units[unit];
        }

        private static string CreateBackupZip(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Thư mục backup trống.", nameof(directory));
            }

            Directory.CreateDirectory(directory);
            string tempDirectory = Path.Combine(Path.GetTempPath(), "SX3Backup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string mainSnapshot = Path.Combine(tempDirectory, DatabaseRepository.DatabaseFileName);
                string productSnapshot = Path.Combine(tempDirectory, DatabaseRepository.ProductDatabaseFileName);

                SnapshotDatabase(DatabaseRepository.DatabasePath, DatabaseRepository.CreateConnection, mainSnapshot);
                SnapshotDatabase(DatabaseRepository.ProductDatabasePath, DatabaseRepository.CreateProductConnection, productSnapshot);

                string backupPath = GetUniqueFilePath(Path.Combine(directory, fileName));
                using (FileStream stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(mainSnapshot, DatabaseRepository.DatabaseFileName, CompressionLevel.Optimal);
                    archive.CreateEntryFromFile(productSnapshot, DatabaseRepository.ProductDatabaseFileName, CompressionLevel.Optimal);

                    ZipArchiveEntry infoEntry = archive.CreateEntry("backup-info.txt", CompressionLevel.Optimal);
                    using (StreamWriter writer = new StreamWriter(infoEntry.Open()))
                    {
                        writer.WriteLine("SX3 Scanner database backup");
                        writer.WriteLine("CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        writer.WriteLine("MainDatabase=" + DatabaseRepository.DatabasePath);
                        writer.WriteLine("ProductDatabase=" + DatabaseRepository.ProductDatabasePath);
                    }
                }

                return backupPath;
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        private static void SnapshotDatabase(string sourcePath, Func<SQLiteConnection> connectionFactory, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Không tìm thấy database để backup.", sourcePath);
            }

            try
            {
                using (SQLiteConnection source = connectionFactory())
                using (SQLiteConnection destination = new SQLiteConnection("Data Source=" + destinationPath + ";Version=3;"))
                {
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("SQLite BackupDatabase lỗi, chuyển sang copy file sau checkpoint: " + ex.Message);
                using (SQLiteConnection connection = connectionFactory())
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA wal_checkpoint(FULL);";
                    command.ExecuteNonQuery();
                }

                File.Copy(sourcePath, destinationPath, true);
            }
        }

        private static string ReplaceDatabaseFile(
            string sourcePath,
            string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            PrepareDatabaseFileForReplace(destinationPath);
            DeleteSidecarFiles(destinationPath);

            string temporaryDestination = destinationPath + ".restore_tmp";
            if (File.Exists(temporaryDestination))
            {
                File.Delete(temporaryDestination);
            }

            File.Copy(sourcePath, temporaryDestination, true);
            string rollbackPath = null;
            if (File.Exists(destinationPath))
            {
                rollbackPath = destinationPath + ".restore_rollback_" +
                    Guid.NewGuid().ToString("N");
                File.Replace(
                    temporaryDestination,
                    destinationPath,
                    rollbackPath);
            }
            else
            {
                File.Move(temporaryDestination, destinationPath);
            }

            DeleteSidecarFiles(destinationPath);
            SQLiteConnection.ClearAllPools();
            return rollbackPath;
        }

        private static void RollbackDatabaseFile(
            string destinationPath,
            string rollbackPath)
        {
            if (string.IsNullOrWhiteSpace(rollbackPath) ||
                !File.Exists(rollbackPath))
            {
                return;
            }

            DeleteSidecarFiles(destinationPath);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(rollbackPath, destinationPath);
        }

        private static void ValidateDatabaseFile(string path, string displayName)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Không tìm thấy " + displayName + ".", path);

            string connectionString =
                "Data Source=" + path + ";Version=3;Read Only=True;Pooling=False;Default Timeout=5;";
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA integrity_check;";
                    object result = command.ExecuteScalar();
                    if (!string.Equals(
                            Convert.ToString(result),
                            "ok",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            displayName + " không vượt qua integrity_check: " + result);
                    }
                }
            }
        }

        private static void PrepareDatabaseFileForReplace(string databasePath)
        {
            try
            {
                if (!File.Exists(databasePath))
                    return;

                Func<SQLiteConnection> connectionFactory = string.Equals(
                    Path.GetFullPath(databasePath),
                    Path.GetFullPath(DatabaseRepository.ProductDatabasePath),
                    StringComparison.OrdinalIgnoreCase)
                        ? (Func<SQLiteConnection>)DatabaseRepository.CreateProductConnection
                        : DatabaseRepository.CreateConnection;

                using (SQLiteConnection connection = connectionFactory())
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    command.ExecuteNonQuery();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
            }
        }

        private static void DeleteSidecarFiles(string databasePath)
        {
            TryDeleteFile(databasePath + "-wal");
            TryDeleteFile(databasePath + "-shm");
        }

        private static void CleanupOldBackups(string directory, int retentionDays)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                DateTime cutoff = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
                foreach (string file in Directory.GetFiles(directory, "SX3_Backup_*.zip", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetCreationTime(file) < cutoff && File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        StartupManager.Log("Không xóa được backup cũ " + file + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("Cleanup backup lỗi: " + ex);
            }
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;

            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int i = 1; i <= 999; i++)
            {
                string candidate = Path.Combine(directory, name + "_" + i + extension);
                if (!File.Exists(candidate)) return candidate;
            }

            throw new IOException("Không tạo được tên backup không trùng: " + path);
        }

        private static string MakeSafeFileName(string value)
        {
            string safe = value;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }

            return safe.Replace(' ', '_');
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
