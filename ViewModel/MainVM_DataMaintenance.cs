using Microsoft.Win32;
using SX3_SCANER.Helper;
using SX3_SCANER.Model.Respository;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private bool _isMaintenanceBusy;
        private bool _backupEnabled = true;
        private string _databaseSizeText = "0 MB";
        private string _productDatabaseSizeText = "0 MB";
        private string _databasePathText = string.Empty;
        private string _backupLocalDirectoryText = string.Empty;
        private string _backupNetworkDirectoryText = string.Empty;
        private string _backupRetentionDaysText = "30";
        private string _lastBackupText = "Chưa backup";
        private string _backupStatusText = "Chưa backup dữ liệu.";
        private string _maintenanceStatus = "Sẵn sàng.";

        private ICommand _refreshMaintenanceInfoCMD;
        private ICommand _saveBackupSettingsCMD;
        private ICommand _runBackupNowCMD;
        private ICommand _restoreBackupCMD;

        public bool IsMaintenanceBusy
        {
            get { return _isMaintenanceBusy; }
            set
            {
                if (_isMaintenanceBusy == value) return;
                _isMaintenanceBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool BackupEnabled
        {
            get { return _backupEnabled; }
            set { _backupEnabled = value; OnPropertyChanged(); }
        }

        public string DatabaseSizeText
        {
            get { return _databaseSizeText; }
            set { _databaseSizeText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string ProductDatabaseSizeText
        {
            get { return _productDatabaseSizeText; }
            set { _productDatabaseSizeText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string DatabasePathText
        {
            get { return _databasePathText; }
            set { _databasePathText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string BackupLocalDirectoryText
        {
            get { return _backupLocalDirectoryText; }
            set { _backupLocalDirectoryText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string BackupNetworkDirectoryText
        {
            get { return _backupNetworkDirectoryText; }
            set { _backupNetworkDirectoryText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string BackupRetentionDaysText
        {
            get { return _backupRetentionDaysText; }
            set { _backupRetentionDaysText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string LastBackupText
        {
            get { return _lastBackupText; }
            set { _lastBackupText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string BackupStatusText
        {
            get { return _backupStatusText; }
            set { _backupStatusText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string MaintenanceStatus
        {
            get { return _maintenanceStatus; }
            set { _maintenanceStatus = value ?? string.Empty; OnPropertyChanged(); }
        }

        public ICommand RefreshMaintenanceInfoCMD
        {
            get
            {
                if (_refreshMaintenanceInfoCMD == null)
                {
                    _refreshMaintenanceInfoCMD = new RelayCommand<object>(
                        parameter => !IsMaintenanceBusy,
                        parameter => RefreshMaintenanceInfo());
                }

                return _refreshMaintenanceInfoCMD;
            }
        }

        public ICommand SaveBackupSettingsCMD
        {
            get
            {
                if (_saveBackupSettingsCMD == null)
                {
                    _saveBackupSettingsCMD = new RelayCommand<object>(
                        parameter => !IsMaintenanceBusy,
                        parameter => SaveBackupSettings());
                }

                return _saveBackupSettingsCMD;
            }
        }

        public ICommand RunBackupNowCMD
        {
            get
            {
                if (_runBackupNowCMD == null)
                {
                    _runBackupNowCMD = new RelayCommand<object>(
                        parameter => !IsMaintenanceBusy,
                        parameter => RunBackupNowAsync());
                }

                return _runBackupNowCMD;
            }
        }

        public ICommand RestoreBackupCMD
        {
            get
            {
                if (_restoreBackupCMD == null)
                {
                    _restoreBackupCMD = new RelayCommand<object>(
                        parameter => !IsMaintenanceBusy && !InJob,
                        parameter => RestoreBackupAsync());
                }

                return _restoreBackupCMD;
            }
        }

        private void InitializeMaintenanceProperties()
        {
            DataBackupService.EnsureDefaultSettings();
            RefreshMaintenanceInfo();
            ScheduleDailyBackupCheck();
        }

        internal void RefreshMaintenanceInfo()
        {
            try
            {
                DataBackupService.EnsureDefaultSettings();
                BackupEnabled = DataBackupService.IsBackupEnabled();
                DatabasePathText = DatabaseRepository.DatabasePath;
                BackupLocalDirectoryText = DatabaseRepository.BackupDirectory;
                BackupNetworkDirectoryText = DataBackupService.GetNetworkBackupPath();
                BackupRetentionDaysText = DataBackupService.GetRetentionDays().ToString();
                DatabaseSizeText = DataBackupService.FormatBytes(DataBackupService.GetFileSizeBytes(DatabaseRepository.DatabasePath));
                ProductDatabaseSizeText = DataBackupService.FormatBytes(DataBackupService.GetFileSizeBytes(DatabaseRepository.ProductDatabasePath));

                string last = DataBackupService.GetLastBackupAt();
                LastBackupText = string.IsNullOrWhiteSpace(last) ? "Chưa backup" : last;
                BackupStatusText = DataBackupService.GetLastBackupStatus();
                MaintenanceStatus = "Đã cập nhật thông tin dữ liệu.";
            }
            catch (Exception ex)
            {
                MaintenanceStatus = "Không đọc được thông tin dữ liệu: " + ex.Message;
                StartupManager.Log("Refresh maintenance info failed: " + ex);
            }
        }

        private void SaveBackupSettings()
        {
            try
            {
                int retentionDays;
                if (!int.TryParse(BackupRetentionDaysText, out retentionDays))
                {
                    retentionDays = 30;
                }

                DataBackupService.SetBackupEnabled(BackupEnabled);
                DataBackupService.SetNetworkBackupPath(BackupNetworkDirectoryText);
                DataBackupService.SetRetentionDays(retentionDays);
                RefreshMaintenanceInfo();
                MaintenanceStatus = "Đã lưu cấu hình backup.";
            }
            catch (Exception ex)
            {
                MaintenanceStatus = "Lưu cấu hình backup lỗi: " + ex.Message;
                StartupManager.Log("Save backup settings failed: " + ex);
            }
        }

        private async void RunBackupNowAsync()
        {
            IsMaintenanceBusy = true;
            MaintenanceStatus = "Đang backup dữ liệu...";
            StartupManager.SetStatus("Đang backup dữ liệu SX3 Scanner...");

            try
            {
                BackupOperationResult result = await DataBackupService.CreateManualBackupAsync();
                RefreshMaintenanceInfo();
                MaintenanceStatus = result.Message;
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    result.Message,
                    result.Success ? "Backup dữ liệu" : "Backup lỗi",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MaintenanceStatus = "Backup lỗi: " + ex.Message;
                StartupManager.Log("Run backup now failed: " + ex);
            }
            finally
            {
                IsMaintenanceBusy = false;
                StartupManager.SetStatus("Sẵn sàng");
            }
        }

        private async void RestoreBackupAsync()
        {
            if (InJob)
            {
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Đang có phiên scan. Hãy tạm dừng scan trước khi phục hồi dữ liệu.",
                    "Không thể phục hồi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Chọn file backup SX3 để phục hồi",
                Filter = "SX3 backup (*.zip;*.db)|*.zip;*.db|All files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            MessageBoxResult confirm = SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                "Phục hồi backup sẽ ghi đè database hiện tại. App sẽ tự tạo backup khẩn cấp trước khi ghi đè.\n\nBạn chắc chắn muốn tiếp tục?",
                "Xác nhận phục hồi dữ liệu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            IsMaintenanceBusy = true;
            MaintenanceStatus = "Đang phục hồi dữ liệu...";
            StartupManager.SetStatus("Đang phục hồi database SX3 Scanner...");

            try
            {
                BackupOperationResult result = await DataBackupService.RestoreBackupAsync(dialog.FileName);
                RefreshMaintenanceInfo();
                MaintenanceStatus = result.Message;
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    result.Message + (result.Success ? "\n\nApp sẽ đóng. Hãy mở lại SX3 Scanner." : string.Empty),
                    result.Success ? "Phục hồi dữ liệu" : "Phục hồi lỗi",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

                if (result.Success && Application.Current != null)
                {
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MaintenanceStatus = "Phục hồi lỗi: " + ex.Message;
                StartupManager.Log("Restore backup command failed: " + ex);
            }
            finally
            {
                IsMaintenanceBusy = false;
                StartupManager.SetStatus("Sẵn sàng");
            }
        }

        private async void ScheduleDailyBackupCheck()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                if (InJob)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }

                BackupOperationResult result = await DataBackupService.RunDailyBackupIfDueAsync();
                if (result != null && result.Success)
                {
                    RefreshMaintenanceInfo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Daily backup check failed: " + ex);
                StartupManager.Log("Daily backup check failed: " + ex);
            }
        }
    }
}
