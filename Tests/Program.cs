using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using SX3_SCANER.Helper;
using SX3_SCANER.Model.Respository;

namespace SX3.Scanner.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--benchmark")
                return RunIndexBenchmark();

            Run("Normalize QR product", () =>
                Equal("ABC123", ScanValidationService.NormalizeQrProductCode(" #abc123 ")));
            Run("Extract bounded segment", () =>
                Equal("CDE", ScanValidationService.ExtractSegment("ABCDE", 2, 10)));
            Run("Session key is normalized", () =>
                Equal("PART-01|20260621", ScanSessionService.BuildSessionKey(
                    " part-01 ", new DateTime(2026, 6, 21, 23, 59, 0))));
            Run("Update version comparison", () =>
            {
                True(UpdateService.IsNewerVersion(new Version(10, 6), new Version(10, 5)));
                True(!UpdateService.IsNewerVersion(new Version(10, 5), new Version(10, 5)));
            });
            Run("Range query uses date index", AssertRangeQueryPlan);

            Console.WriteLine(_failures == 0 ? "All tests passed." : _failures + " test(s) failed.");
            return _failures == 0 ? 0 : 1;
        }

        private static void AssertRangeQueryPlan()
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:;Version=3;"))
            {
                connection.Open();
                Execute(connection, "CREATE TABLE ScanHistoryView (ID INTEGER PRIMARY KEY, ScanTime TEXT, ScanResult INTEGER);");
                Execute(connection, "CREATE INDEX idx_ScanTime_Result ON ScanHistoryView(ScanTime, ScanResult);");
                using (var command = new SQLiteCommand(
                    "EXPLAIN QUERY PLAN SELECT ID FROM ScanHistoryView WHERE ScanTime >= @From AND ScanTime < @To ORDER BY ScanTime DESC LIMIT 500",
                    connection))
                {
                    command.Parameters.AddWithValue("@From", new DateTime(2026, 6, 1));
                    command.Parameters.AddWithValue("@To", new DateTime(2026, 7, 1));
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        bool usesIndex = false;
                        while (reader.Read())
                        {
                            string detail = reader.GetString(3);
                            usesIndex |= detail.IndexOf(
                                "INDEX",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        True(usesIndex);
                    }
                }
            }
        }

        private static int RunIndexBenchmark()
        {
            const int rowCount = 30000;
            long legacy = MeasureInsert(rowCount, true);
            long optimized = MeasureInsert(rowCount, false);
            Console.WriteLine("Rows: " + rowCount);
            Console.WriteLine("Legacy indexes: " + legacy + " ms");
            Console.WriteLine("Optimized indexes: " + optimized + " ms");
            Console.WriteLine("Write improvement: " +
                (legacy <= 0 ? 0 : (legacy - optimized) * 100.0 / legacy).ToString("0.0") + "%");
            return 0;
        }

        private static long MeasureInsert(int rowCount, bool legacy)
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:;Version=3;"))
            {
                connection.Open();
                Execute(connection, "CREATE TABLE ScanHistoryView (ID INTEGER PRIMARY KEY, BoxName TEXT, ProductPartNumber TEXT, SealNo TEXT, LotNo TEXT, ScanData TEXT, ScanResult INTEGER, ScanTime TEXT, BoxDate TEXT, ScanMessage TEXT, ScanWorker TEXT, BoxType TEXT);");
                foreach (string sql in BuildIndexes(legacy)) Execute(connection, sql);
                var watch = Stopwatch.StartNew();
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                using (var command = new SQLiteCommand("INSERT INTO ScanHistoryView(BoxName,ProductPartNumber,SealNo,LotNo,ScanData,ScanResult,ScanTime,BoxDate,ScanMessage,ScanWorker,BoxType) VALUES(@Box,@Part,@Seal,@Lot,@Data,@Result,@Time,@Date,@Message,@Worker,@Type)", connection, transaction))
                {
                    command.Parameters.Add("@Box", System.Data.DbType.String);
                    command.Parameters.Add("@Part", System.Data.DbType.String);
                    command.Parameters.Add("@Seal", System.Data.DbType.String);
                    command.Parameters.Add("@Lot", System.Data.DbType.String);
                    command.Parameters.Add("@Data", System.Data.DbType.String);
                    command.Parameters.Add("@Result", System.Data.DbType.Int32);
                    command.Parameters.Add("@Time", System.Data.DbType.DateTime);
                    command.Parameters.Add("@Date", System.Data.DbType.DateTime);
                    command.Parameters.Add("@Message", System.Data.DbType.String);
                    command.Parameters.Add("@Worker", System.Data.DbType.String);
                    command.Parameters.Add("@Type", System.Data.DbType.String);
                    for (int i = 0; i < rowCount; i++)
                    {
                        command.Parameters[0].Value = "P260621-" + (i / 20);
                        command.Parameters[1].Value = "PART-" + (i % 100);
                        command.Parameters[2].Value = "260621";
                        command.Parameters[3].Value = "LOT-" + (i % 1000);
                        command.Parameters[4].Value = "SCAN-" + i;
                        command.Parameters[5].Value = i % 10 == 0 ? 0 : 1;
                        command.Parameters[6].Value = new DateTime(2026, 6, 21).AddSeconds(i);
                        command.Parameters[7].Value = new DateTime(2026, 6, 21);
                        command.Parameters[8].Value = i % 10 == 0 ? "NG" : "PASS";
                        command.Parameters[9].Value = "WORKER";
                        command.Parameters[10].Value = "OPEN";
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
                watch.Stop();
                return watch.ElapsedMilliseconds;
            }
        }

        private static IEnumerable<string> BuildIndexes(bool legacy)
        {
            var indexes = new List<string>
            {
                "CREATE INDEX i_box_time ON ScanHistoryView(BoxName, ScanTime DESC)",
                "CREATE INDEX i_result_id ON ScanHistoryView(ScanResult, ID DESC)",
                "CREATE INDEX i_time_result ON ScanHistoryView(ScanTime, ScanResult)",
                "CREATE INDEX i_boxdate_result ON ScanHistoryView(BoxDate, ScanResult)",
                "CREATE INDEX i_part_id ON ScanHistoryView(ProductPartNumber, ID DESC)",
                "CREATE INDEX i_seal_id ON ScanHistoryView(SealNo, ID DESC)",
                "CREATE UNIQUE INDEX i_pass_data ON ScanHistoryView(ScanData) WHERE ScanResult=1",
                "CREATE INDEX i_message ON ScanHistoryView(ScanMessage)"
            };
            if (legacy)
            {
                indexes.Add("CREATE INDEX i_box ON ScanHistoryView(BoxName)");
                indexes.Add("CREATE INDEX i_part ON ScanHistoryView(ProductPartNumber)");
                indexes.Add("CREATE INDEX i_seal ON ScanHistoryView(SealNo)");
                indexes.Add("CREATE INDEX i_lot ON ScanHistoryView(LotNo)");
                indexes.Add("CREATE INDEX i_result ON ScanHistoryView(ScanResult)");
                indexes.Add("CREATE INDEX i_time ON ScanHistoryView(ScanTime)");
                indexes.Add("CREATE INDEX i_data ON ScanHistoryView(ScanData)");
                indexes.Add("CREATE INDEX i_data_result ON ScanHistoryView(ScanData,ScanResult)");
            }
            return indexes;
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection)) command.ExecuteNonQuery();
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual);
        }

        private static void True(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Condition is false.");
        }
    }
}
