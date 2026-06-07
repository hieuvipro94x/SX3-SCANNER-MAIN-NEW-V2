using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using SX3_SCANER.Helper;
using SX3_SCANER.Model.Respository;

namespace SX3_SCANER.Model
{
    internal class LabelProductInfoRepository
    {
        private static string _connectionString = DatabaseRepository.ProductConnectionString;

        public LabelProductInfoRepository()
        {
            _connectionString = DatabaseRepository.ProductConnectionString;
        }

        internal static void CreateTableIfNotExists()
        {
            DatabaseRepository.EnsureApplicationDataDirectory();
            _connectionString = DatabaseRepository.ProductConnectionString;

            using (SQLiteConnection connection = DatabaseRepository.CreateProductConnection())
            {
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS LabelProductInfo (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Car TEXT,
                        PartNumber TEXT,
                        PartName TEXT,
                        CodeStringForm TEXT,
                        CodePrefix TEXT NOT NULL DEFAULT '',
                        CodeSuffix TEXT NOT NULL DEFAULT '',
                        CodeLength INTEGER,
                        BoxQuantity INTEGER
                    );";
                using (SQLiteCommand command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (SQLiteCommand command = new SQLiteCommand(
                    "UPDATE LabelProductInfo SET CodePrefix = COALESCE(CodePrefix, ''), CodeSuffix = COALESCE(CodeSuffix, '') WHERE CodePrefix IS NULL OR CodeSuffix IS NULL",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public ObservableCollection<LabelProductInfo> GetAllLabelProductInfo()
        {
            ObservableCollection<LabelProductInfo> labelProductInfos = new ObservableCollection<LabelProductInfo>();
            using (SQLiteConnection connection = DatabaseRepository.CreateProductConnection())
            {
                string selectQuery = "SELECT * FROM LabelProductInfo";
                using (SQLiteCommand command = new SQLiteCommand(selectQuery, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            labelProductInfos.Add(new LabelProductInfo
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                Car = reader["Car"].ToString(),
                                PartNumber = reader["PartNumber"].ToString(),
                                PartName = reader["PartName"].ToString(),
                                CodeStringForm = reader["CodeStringForm"].ToString(),
                                CodePrefix = reader["CodePrefix"].ToString(),
                                CodeSuffix = reader["CodeSuffix"].ToString(),
                                CodeLength = Convert.ToInt32(reader["CodeLength"]),
                                BoxQuantity = Convert.ToInt32(reader["BoxQuantity"])
                            });
                        }
                    }
                }
            }
            return labelProductInfos;
        }

        public void INSERTLabelProductInfo(LabelProductInfo labelProductInfo)
        {
            if (labelProductInfo == null)
            {
                throw new ArgumentNullException(nameof(labelProductInfo));
            }

            try
            {
                using (SQLiteConnection connection = DatabaseRepository.CreateProductConnection())
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                {
                    string insertQuery = @"
                        INSERT INTO LabelProductInfo (Car, PartNumber, PartName, CodeStringForm, CodePrefix, CodeSuffix, CodeLength, BoxQuantity)
                        VALUES (@Car, @PartNumber, @PartName, @CodeStringForm, @CodePrefix, @CodeSuffix, @CodeLength, @BoxQuantity)";
                    using (SQLiteCommand command = new SQLiteCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Car", labelProductInfo.Car);
                        command.Parameters.AddWithValue("@PartNumber", labelProductInfo.PartNumber);
                        command.Parameters.AddWithValue("@PartName", labelProductInfo.PartName);
                        command.Parameters.AddWithValue("@CodeStringForm", labelProductInfo.CodeStringForm);
                        command.Parameters.AddWithValue("@CodePrefix", labelProductInfo.CodePrefix ?? string.Empty);
                        command.Parameters.AddWithValue("@CodeSuffix", labelProductInfo.CodeSuffix ?? string.Empty);
                        command.Parameters.AddWithValue("@CodeLength", labelProductInfo.CodeLength);
                        command.Parameters.AddWithValue("@BoxQuantity", labelProductInfo.BoxQuantity);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("Loi insert LabelProductInfo vao " + DatabaseRepository.ProductDatabasePath +
                    ". PartNumber=" + (labelProductInfo != null ? labelProductInfo.PartNumber : string.Empty) +
                    ". Chi tiet: " + ex);
                throw;
            }
        }

        public void UpdateLabelProductInfo(LabelProductInfo labelProductInfo)
        {
            if (labelProductInfo == null)
            {
                throw new ArgumentNullException(nameof(labelProductInfo));
            }

            try
            {
                using (SQLiteConnection connection = DatabaseRepository.CreateProductConnection())
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                {
                    string updateQuery = @"
                        UPDATE LabelProductInfo
                        SET Car = @Car, PartNumber = @PartNumber, PartName = @PartName, CodeStringForm = @CodeStringForm,
                            CodePrefix = @CodePrefix, CodeSuffix = @CodeSuffix, CodeLength = @CodeLength, BoxQuantity = @BoxQuantity
                        WHERE ID = @ID";
                    using (SQLiteCommand command = new SQLiteCommand(updateQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Car", labelProductInfo.Car);
                        command.Parameters.AddWithValue("@PartNumber", labelProductInfo.PartNumber);
                        command.Parameters.AddWithValue("@PartName", labelProductInfo.PartName);
                        command.Parameters.AddWithValue("@CodeStringForm", labelProductInfo.CodeStringForm);
                        command.Parameters.AddWithValue("@CodePrefix", labelProductInfo.CodePrefix ?? string.Empty);
                        command.Parameters.AddWithValue("@CodeSuffix", labelProductInfo.CodeSuffix ?? string.Empty);
                        command.Parameters.AddWithValue("@CodeLength", labelProductInfo.CodeLength);
                        command.Parameters.AddWithValue("@BoxQuantity", labelProductInfo.BoxQuantity);
                        command.Parameters.AddWithValue("@ID", labelProductInfo.ID);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("Loi update LabelProductInfo vao " + DatabaseRepository.ProductDatabasePath +
                    ". ID=" + (labelProductInfo != null ? labelProductInfo.ID.ToString() : string.Empty) +
                    ". Chi tiet: " + ex);
                throw;
            }
        }

        public void DeleteLabelProductInfo(int id)
        {
            try
            {
                using (SQLiteConnection connection = DatabaseRepository.CreateProductConnection())
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                {
                    string deleteQuery = "DELETE FROM LabelProductInfo WHERE ID = @ID";
                    using (SQLiteCommand command = new SQLiteCommand(deleteQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@ID", id);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                StartupManager.Log("Loi delete LabelProductInfo trong " + DatabaseRepository.ProductDatabasePath +
                    ". ID=" + id + ". Chi tiet: " + ex);
                throw;
            }
        }

        public bool CheckIfExist(string partname, string partnumber, int selfID = -1)
        {
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query;
                if (selfID == -1) // Nếu selfID không được cung cấp, kiểm tra sự tồn tại bình thường
                {
                    query = "SELECT COUNT(*) FROM LabelProductInfo WHERE PartName = @PartName OR PartNumber = @PartNumber";
                }
                else // Nếu selfID được cung cấp, loại trừ bản ghi có ID trùng với selfID
                {
                    query = "SELECT COUNT(*) FROM LabelProductInfo WHERE (PartName = @PartName OR PartNumber = @PartNumber) AND ID != @ID";
                }

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@PartName", partname);
                    command.Parameters.AddWithValue("@PartNumber", partnumber);
                    if (selfID != -1)
                    {
                        command.Parameters.AddWithValue("@ID", selfID);
                    }
                    int count = Convert.ToInt32(command.ExecuteScalar());
                    return count > 0;
                }
            }
        }

        public int GetBoxQuantity(string _partnumber)
        {
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT BoxQuantity FROM LabelProductInfo WHERE PartNumber = @PartNumber";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@PartNumber", _partnumber);
                    object result = command.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                    else
                    {
                        // Trả về một giá trị mặc định (ví dụ: 0) nếu không tìm thấy PartNumber
                        return 0;
                    }
                }
            }
        }

        public List<string> GetAllPartNumber()
        {
            List<string> partNumbers = new List<string>();
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT DISTINCT PartNumber FROM LabelProductInfo";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0)) // Kiểm tra giá trị NULL
                            {
                                partNumbers.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            return partNumbers;
        }

        public LabelProductInfo GetWithPartNumber(string pnumber)
        {
            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT * FROM LabelProductInfo WHERE PartNumber = @PartNumber";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@PartNumber", pnumber);
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new LabelProductInfo
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                Car = reader["Car"].ToString(),
                                PartNumber = reader["PartNumber"].ToString(),
                                PartName = reader["PartName"].ToString(),
                                CodeStringForm = reader["CodeStringForm"].ToString(),
                                CodePrefix = reader["CodePrefix"].ToString(),
                                CodeSuffix = reader["CodeSuffix"].ToString(),
                                CodeLength = Convert.ToInt32(reader["CodeLength"]),
                                BoxQuantity = Convert.ToInt32(reader["BoxQuantity"])
                            };
                        }
                        else
                        {
                            return null; // Trả về null nếu không tìm thấy PartNumber
                        }
                    }
                }
            }
        }
    }
}
