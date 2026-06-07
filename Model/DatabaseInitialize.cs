using SX3_SCANER.Model.Respository;
using SX3_SCANER.Helper;
using System;
using System.Data.SQLite;
using System.IO;

namespace SX3_SCANER.Model
{
    internal class DatabaseInitialize
    {
        public DatabaseInitialize()
        {
        }

        internal void EnsureCreate()
        {
            StartupManager.SetStatus("Đang kiểm tra thư mục dữ liệu...");
            DatabaseRepository.EnsureDatabaseFiles();
            DatabaseRepository.ValidateDatabasePaths();

            bool isNewDatabase = !File.Exists(DatabaseRepository.MainDatabasePath);
            if (isNewDatabase)
            {
                StartupManager.SetStatus("Đang tạo database.db...");
                StartupManager.Log("Tao database moi tai: " + DatabaseRepository.MainDatabasePath);
                SQLiteConnection.CreateFile(DatabaseRepository.MainDatabasePath);
            }

            StartupManager.SetStatus("Đang kiểm tra database.db...");
            ConfigureDatabase();
            StartupManager.SetStatus("Đang kiểm tra product.db...");
            ConfigureProductDatabase();

            StartupManager.SetStatus("Đang cập nhật cấu trúc dữ liệu...");
            BoxProductRepository.CreateTableIfNotExists();
            LabelProductInfoRepository.CreateTableIfNotExists();
            ScanHistoryRepository.CreateTableIfNotExists();
            ScanSessionService.CreateTableIfNotExists();

            SyncHistoryBoxTypes();
            CreateIndexes();
            StartupManager.SetStatus("Đang kiểm tra tính toàn vẹn dữ liệu...");
            DatabaseRepository.RunIntegrityCheck();
            StartupManager.SetStatus("Hoàn tất khởi động");
        }

        private static void SyncHistoryBoxTypes()
        {
            TryExecute(@"
                UPDATE ScanHistoryView
                SET BoxType = COALESCE(
                        (SELECT BoxType FROM BoxProduct WHERE BoxProduct.BoxName = ScanHistoryView.BoxName LIMIT 1),
                        BoxType),
                    IsPartialBox = COALESCE(
                        (SELECT IsPartialBox FROM BoxProduct WHERE BoxProduct.BoxName = ScanHistoryView.BoxName LIMIT 1),
                        IsPartialBox)
                WHERE EXISTS (
                    SELECT 1 FROM BoxProduct WHERE BoxProduct.BoxName = ScanHistoryView.BoxName
                );");
        }

        private static void ConfigureDatabase()
        {
            using (var connection = DatabaseRepository.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;";
                command.ExecuteNonQuery();
            }
        }

        private static void ConfigureProductDatabase()
        {
            using (var connection = DatabaseRepository.CreateProductConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;";
                command.ExecuteNonQuery();
            }
        }

        private static void CreateIndexes()
        {
            // Các câu lệnh dùng IF NOT EXISTS nên an toàn khi chạy nhiều lần.
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_BoxName ON ScanHistoryView(BoxName);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ProductPartNumber ON ScanHistoryView(ProductPartNumber);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_SealNo ON ScanHistoryView(SealNo);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_Result ON ScanHistoryView(ScanResult);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ScanTime ON ScanHistoryView(ScanTime);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ID_ScanTime ON ScanHistoryView(ID DESC, ScanTime DESC);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ScanData ON ScanHistoryView(ScanData);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ScanMessage ON ScanHistoryView(ScanMessage);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_ScanWorker ON ScanHistoryView(ScanWorker);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_BoxType ON ScanHistoryView(BoxType);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_Product_Seal_Lot ON ScanHistoryView(ProductPartName, SealNo, LotNo);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_ScanHistoryView_PartNumber_Seal_Lot ON ScanHistoryView(ProductPartNumber, SealNo, LotNo);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_BoxProduct_BoxName ON BoxProduct(BoxName);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_BoxProduct_Complete ON BoxProduct(BoxComplete);");
            TryExecute("CREATE INDEX IF NOT EXISTS idx_BoxProduct_Part_Seal ON BoxProduct(ProductPartNumber, BoxSealNo);");
            TryExecuteProduct("CREATE INDEX IF NOT EXISTS idx_LabelProductInfo_PartNumber ON LabelProductInfo(PartNumber);");
            TryExecuteProduct("CREATE INDEX IF NOT EXISTS idx_LabelProductInfo_PartName_PartNumber ON LabelProductInfo(PartName, PartNumber);");
        }

        private static void TryExecute(string sql)
        {
            try
            {
                DatabaseRepository.ExecuteNonQuery(sql);
            }
            catch (SQLiteException)
            {
                // Một số project cũ có tên bảng/cột khác. Không chặn app khởi động vì index chỉ là tối ưu.
            }
            catch (Exception)
            {
                // Giữ tương thích với database cũ; lỗi index không được làm hỏng quá trình chạy máy.
            }
        }

        private static void TryExecuteProduct(string sql)
        {
            try
            {
                using (var connection = DatabaseRepository.CreateProductConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
            catch (SQLiteException)
            {
            }
            catch (Exception)
            {
            }
        }
    }
}
