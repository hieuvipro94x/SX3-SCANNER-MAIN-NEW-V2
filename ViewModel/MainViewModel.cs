using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using SX3_SCANER.View;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        public enum JobIndex
        {
            SCAN = 0,
            CRUD = 1,
            QUERY = 2,
            MAINTENANCE = 3
        }


        private bool _AdminCRUD;
        private bool _isApplicationReady;
        private bool _isApplicationInitializing;
        private bool _hasStartupError;
        private string _startupDatabaseStatusText = "Đang kiểm tra dữ liệu";

        public bool AdminCRUD
        {
            get { return _AdminCRUD; }
            set { _AdminCRUD = value; OnPropertyChanged(); }
        }

        public bool IsApplicationReady
        {
            get { return _isApplicationReady; }
            private set
            {
                if (_isApplicationReady == value) return;
                _isApplicationReady = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStartupCheckVisible));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                OnPropertyChanged(nameof(CanSwitchProduct));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsApplicationInitializing
        {
            get { return _isApplicationInitializing; }
            private set
            {
                if (_isApplicationInitializing == value) return;
                _isApplicationInitializing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStartupCheckVisible));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasStartupError
        {
            get { return _hasStartupError; }
            private set
            {
                if (_hasStartupError == value) return;
                _hasStartupError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStartupCheckVisible));
            }
        }

        public bool IsStartupCheckVisible
        {
            get { return !IsApplicationReady || HasStartupError; }
        }

        public string StartupDatabaseStatusText
        {
            get { return _startupDatabaseStatusText; }
            private set
            {
                _startupDatabaseStatusText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        private int _TabControlSelectedIndex;

        public int TabControlSelectedIndex
        {
            get { return _TabControlSelectedIndex; }
            set
            {
                if (_TabControlSelectedIndex == value) return;
                _TabControlSelectedIndex = value;
                OnPropertyChanged();
                if (value == (int)JobIndex.SCAN)
                {
                    if (IsApplicationReady)
                    {
                        PartNumberList = new LabelProductInfoRepository().GetAllPartNumber();
                    }
                }
                else if (value == (int)JobIndex.CRUD)
                {
                    if (!IsApplicationReady)
                    {
                        TabControlSelectedIndex = (int)JobIndex.SCAN;
                        return;
                    }

                    AdminCRUD = false;
                    AskPasswordWD askPasswordWD = new AskPasswordWD();
                    bool? result = askPasswordWD.ShowDialog();

                    if (result == true)
                    {
                        SearchProductInfo();
                        AdminCRUD = true;
                        return;
                    }

                    AdminCRUD = false;
                    TabControlSelectedIndex = (int)JobIndex.SCAN;
                }
                else if (value == (int)JobIndex.QUERY)
                {
                    if (!IsApplicationReady)
                    {
                        TabControlSelectedIndex = (int)JobIndex.SCAN;
                        return;
                    }

                    LoadQueryLookupsAsync();
                }
                else if (value == (int)JobIndex.MAINTENANCE)
                {
                    if (!IsApplicationReady)
                    {
                        TabControlSelectedIndex = (int)JobIndex.SCAN;
                        return;
                    }

                    RefreshMaintenanceInfo();
                }
            }
        }

        public class AppConfigStringKey
        {
            public const string Injob = "Injob";
            public const string LastProduct = "LastProduct";
            public const string LastWorker = "LastWorker";
        }

        private void ReadAppConfig()
        {
            BtnSTART_CONTENT = "BẮT ĐẦU";
            CanChangeProductInfo = true;
            InJob = false;

            this.SelectedPartNumber = AppConfigHelper.Read(AppConfigStringKey.LastProduct);
            // Tên công nhân phải được nhập mới khi đóng từng thùng.
            // Không khôi phục tên của công nhân từ lần quét trước.
            this.Worker = string.Empty;
            AppConfigHelper.Modify(AppConfigStringKey.LastWorker, string.Empty);

        }

        private ICommand _STARTCMD;

        public ICommand STARTCMD
        {
            get
            {
                if (_STARTCMD == null)
                {
                    _STARTCMD = new RelayCommand<TextBox>(
                        o => CAN_START()
                    , t => START(t));
                }
                return _STARTCMD;
            }
        }

        private bool CAN_START()
        {
            if (!IsApplicationReady) return false;
            if (string.IsNullOrEmpty(SelectedPartNumber)) return false;
            return SelectedQuantity > 0;
        }

        private void START(TextBox t)
        {
            if (InJob)
            {
                SaveCurrentScanSession(false);
                InJob = false;
                StartupManager.SetStatus("Đã tạm dừng phiên quét mã " + SelectedPartNumber + ".");
                return;
            }

            InJob = true;
            t?.Focus();
            StartScaning(SelectedPartNumber);
            StartupManager.SetStatus("Đang quét mã " + SelectedPartNumber + ".");
        }
        private void EnsureCreateAppConfig()
        {
            AppConfigHelper.EnsureCreate(AppConfigStringKey.Injob, "0");
            AppConfigHelper.EnsureCreate(AppConfigStringKey.LastProduct, "");
            AppConfigHelper.EnsureCreate(AppConfigStringKey.LastWorker, "");
        }

        public async Task InitializeApplicationAsync()
        {
            if (IsApplicationReady || IsApplicationInitializing)
                return;

            IsApplicationInitializing = true;
            HasStartupError = false;
            StartupDatabaseStatusText = "Đang kiểm tra dữ liệu...";
            StartupManager.SetStatus("Đang kiểm tra dữ liệu...");

            try
            {
                await Task.Run(() =>
                {
                    DatabaseInitialize initialize = new DatabaseInitialize();
                    initialize.EnsureCreate();
                });

                StartupDatabaseStatusText = "Database sẵn sàng. Đang tải mã hàng...";
                StartupManager.SetStatus("Đang tải danh sách mã hàng...");

                LoadScanDataAfterDatabaseReady();
                ReadAppConfig();
                RefreshMaintenanceInfo();

                IsApplicationReady = true;
                IsApplicationInitializing = false;
                StartupDatabaseStatusText = "Database sẵn sàng.";

                StartDefaultScanSessionOnLaunch();
            }
            catch (Exception ex)
            {
                IsApplicationInitializing = false;
                HasStartupError = true;
                IsApplicationReady = false;

                StartupManager.LogStartupError(
                    ex,
                    DatabaseRepository.DatabasePath +
                    " | " +
                    DatabaseRepository.ProductDatabasePath);

                string diagnosis = StartupManager.GetDatabaseDiagnosis(ex);
                StartupDatabaseStatusText = "Lỗi database: " + diagnosis;
                StartupManager.SetStatus("Lỗi database: " + diagnosis);

                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không thể chuẩn bị database SX3 SCANER." +
                    Environment.NewLine +
                    "Nguyên nhân: " + diagnosis +
                    Environment.NewLine +
                    "Chi tiết: " + ex.Message,
                    "SX3 SCANER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public MainViewModel()
        {
            DatabaseMaintenanceCoordinator.MaintenanceRequested +=
                CancelDatabaseWorkForMaintenance;
            OpenExportFolderCMD = new RelayCommand<object>(
                null,
                _ => OpenExportFolder());
            EnsureCreateAppConfig();
            InitializeScaningPropeties();
            InitializeOnlineAnnouncement();
            InitializeMaintenanceProperties();
        }
    }

}
