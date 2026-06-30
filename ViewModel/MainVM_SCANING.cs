using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using SX3_SCANER.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private readonly ScanHistoryRepository _scanHistoryRepository = new ScanHistoryRepository();
        private readonly BoxProductRepository _boxProductRepository = new BoxProductRepository();
        private readonly ScanSessionService _scanSessionService = new ScanSessionService();
        private readonly SemaphoreSlim _scanWriteLock = new SemaphoreSlim(1, 1);
        private readonly HashSet<string> _currentBoxPassScanCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _scanCodeCacheBoxName;
        private bool _isScanCodeCacheInitialized;
        private bool _isScanBusy;
        private string _lastScannedCode = string.Empty;
        private DateTime _lastScannedAt = DateTime.MinValue;
        private static readonly TimeSpan DuplicateScanWindow = TimeSpan.FromMilliseconds(1200);



        private string _BtnSTART_CONTENT;

        public string BtnSTART_CONTENT
        {
            get { return _BtnSTART_CONTENT; }
            set { _BtnSTART_CONTENT = value; OnPropertyChanged(); }
        }

        private bool _CanChangeProductInfo;

        public bool CanChangeProductInfo
        {
            get { return _CanChangeProductInfo; }
            set { _CanChangeProductInfo = value; OnPropertyChanged(); }
        }

        private bool _InJob;

        public bool InJob
        {
            get { return _InJob; }
            set
            {
                if (_InJob == value) return;

                _InJob = value;

                OnPropertyChanged();

                BtnSTART_CONTENT = value ? "TẠM DỪNG" : "BẮT ĐẦU";

                CanChangeProductInfo = !value;

                NotifyCurrentBoxStatusChanged();

                AppConfigHelper.Modify(AppConfigStringKey.Injob, value ? "1" : "0");

                CommandManager.InvalidateRequerySuggested();

                if (value)
                {
                    AppConfigHelper.Modify(AppConfigStringKey.LastProduct, SelectedPartNumber ?? string.Empty);
                }
            }
        }

        private int _CurrentScanProgress;

        public int CurrentScanProgress
        {
            get { return _CurrentScanProgress; }
            set
            {
                if (_CurrentScanProgress == value) return;

                int previousValue = _CurrentScanProgress;
                _CurrentScanProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanQuantityText));
                OnPropertyChanged(nameof(CurrentProgressPercentText));

                bool fullStateChanged = SelectedQuantity > 0 &&
                    (previousValue >= SelectedQuantity) != (value >= SelectedQuantity);
                if (fullStateChanged)
                {
                    OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                    if (!_isScanBusy)
                    {
                        NotifyCurrentBoxStatusChanged();
                        CommandManager.InvalidateRequerySuggested();
                    }
                }
            }
        }

        public bool HasOpenScanSession
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_CurrentBoxName) ||
                    (ScanHistorySource != null && ScanHistorySource.Count > 0);
            }
        }

        public bool CanSwitchProduct
        {
            get { return IsApplicationReady && !_isScanBusy; }
        }

        public bool CanModifySessionSelection
        {
            get { return IsApplicationReady && !_isScanBusy && !HasOpenScanSession; }
        }

        public bool IsFullBoxReadyToComplete
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_CurrentBoxName) &&
                    SelectedQuantity > 0 &&
                    CurrentScanProgress >= SelectedQuantity;
            }
        }

        private string _Worker;

        public string Worker
        {
            get { return _Worker; }
            set
            {
                string next = value == null ? string.Empty : value.Trim();
                if (string.Equals(_Worker, next, StringComparison.Ordinal)) return;
                _Worker = next;
                OnPropertyChanged();
            }
        }

        private string _InputScanCode;

        public string InputScanCode
        {
            get { return _InputScanCode; }
            set { _InputScanCode = value; OnPropertyChanged(); }
        }

        private ICommand _InputScanCodeCMD;

        public ICommand InputScanCodeCMD
        {
            get
            {
                if (_InputScanCodeCMD == null)
                {
                    _InputScanCodeCMD = new AsyncRelayCommand<string>(
                        code => IsApplicationReady &&
                            InJob &&
                            !string.IsNullOrWhiteSpace(code) &&
                            !IsFullBoxReadyToComplete,
                        async code => await ScanDataAsync(code));
                }

                return _InputScanCodeCMD;
            }
        }

        private ICommand _CancelBoxCMD;

        public ICommand CancelBoxCMD
        {
            get
            {
                if (_CancelBoxCMD == null)
                {
                    _CancelBoxCMD = new AsyncRelayCommand<object>(
                        o => HasOpenScanSession && !_isScanBusy,
                        async o => await CancelCurrentBoxAsync());
                }

                return _CancelBoxCMD;
            }
        }

        private ICommand _ClosePartialBoxCMD;
        private ICommand _CompleteFullBoxCMD;

        public ICommand ClosePartialBoxCMD
        {
            get
            {
                if (_ClosePartialBoxCMD == null)
                {
                    _ClosePartialBoxCMD = new AsyncRelayCommand<object>(
                        o => CurrentScanProgress > 0 &&
                            (SelectedQuantity <= 0 ||
                             CurrentScanProgress < SelectedQuantity) &&
                            !string.IsNullOrWhiteSpace(_CurrentBoxName) &&
                            ScanHistorySource != null &&
                            ScanHistorySource.Count > 0 &&
                            !_isScanBusy,
                        async o => await ClosePartialBoxAsync());
                }

                return _ClosePartialBoxCMD;
            }
        }

        public ICommand CompleteFullBoxCMD
        {
            get
            {
                if (_CompleteFullBoxCMD == null)
                {
                    _CompleteFullBoxCMD = new AsyncRelayCommand<object>(
                        o => IsFullBoxReadyToComplete && !_isScanBusy,
                        async o => await CompleteFullBoxWithLockAsync());
                }

                return _CompleteFullBoxCMD;
            }
        }

        private ScanHistory _CurrentScanHistory;

        private string _CurrentBoxName;
        private DateTime? _currentBoxCreatedDate;

        private string _ScanMess;

        private async Task ScanDataAsync(string inputScanCode)
        {
            await _scanWriteLock.WaitAsync();
            _isScanBusy = true;
            bool scanUiStateChanged = false;
            try
            {
                inputScanCode = inputScanCode?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(inputScanCode))
                    return;

                // Giữ trạng thái nội bộ giống binding cũ nhưng không phát PropertyChanged
                // theo từng ký tự scanner gửi vào TextBox.
                _InputScanCode = inputScanCode;

                DateTime scannedAt = DateTime.UtcNow;
                if (string.Equals(
                        inputScanCode,
                        _lastScannedCode,
                        StringComparison.OrdinalIgnoreCase) &&
                    scannedAt - _lastScannedAt <= DuplicateScanWindow)
                {
                    RecordIgnoredFastDuplicateScan(
                        inputScanCode,
                        "NG - Scan lặp quá nhanh",
                        "Tem này vừa được scanner gửi lại quá nhanh. Không ghi lịch sử và không thêm vào thùng.");
                    return;
                }

                _lastScannedCode = inputScanCode;
                _lastScannedAt = scannedAt;

                EnsureCurrentBoxPassScanCache();
                if (_currentBoxPassScanCodes.Contains(inputScanCode))
                {
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Trùng tem trong thùng hiện tại",
                        "Tem này đã được scan trong thùng hiện tại.",
                        "TRÙNG TEM");
                    return;
                }

                if (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity)
                {
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Thùng đã đủ số lượng",
                        "Thùng đã đủ số lượng. Vui lòng xác nhận đóng thùng trước khi scan tiếp.",
                        "THÙNG ĐÃ ĐỦ");
                    return;
                }

                if (ScanHistorySource == null)
                    ScanHistorySource = new ObservableCollection<ScanHistory>();

                bool allocatedBoxName = string.IsNullOrWhiteSpace(_CurrentBoxName);
                if (string.IsNullOrWhiteSpace(_CurrentBoxName))
                {
                    _currentBoxCreatedDate = BoxDate.Date;
                    _CurrentBoxName = await Task.Run(
                        () => _boxProductRepository.GetNextBoxName(BoxDate));
                    scanUiStateChanged = true;
                }

                ResetScanStatus();

                _CurrentScanHistory = new ScanHistory
                {
                    ScanTime = GetBusinessScanTime(),
                    BoxDate = BoxDate,
                    ScanLabelDate = ScanLabelDate,
                    ProductPartNumber = SelectedPartNumber,
                    ProductPartName = PNameExpected,
                    BoxName = _CurrentBoxName,
                    SealNo = ScanLabelDate.ToString("yyMMdd"),
                    ScanData = inputScanCode,
                    ScanWorker = Worker ?? string.Empty,
                    ActualQty = CurrentScanProgress,
                    TargetQty = SelectedQuantity
                };

                bool isPass = Scan(inputScanCode);

                if (!isPass)
                {
                    _CurrentScanHistory.ScanMessage = _ScanMess;

                    _CurrentScanHistory.ScanResult = false;
                }
                else
                {
                    _CurrentScanHistory.ScanMessage = "PASS";

                    _CurrentScanHistory.ScanResult = true;
                }

                ScanHistory persistedHistory = _CurrentScanHistory;
                string persistedBoxName = _CurrentBoxName;
                int persistedProgress = isPass
                    ? CurrentScanProgress + 1
                    : CurrentScanProgress;
                persistedHistory.ActualQty = persistedProgress;
                bool isFirstPassInBox = isPass && persistedProgress == 1;
                BoxProduct persistedBox = isFirstPassInBox
                    ? new BoxProduct
                    {
                        BoxName = _CurrentBoxName,
                        ProductPartName = PNameExpected,
                        ProductPartNumber = SelectedPartNumber,
                        BoxSealNo = GetCurrentBoxCreatedDate().ToString("yyMMdd"),
                        BoxQuantity = SelectedQuantity,
                        BoxProgress = persistedProgress,
                        BoxComplete = false,
                        BoxWorker = string.IsNullOrWhiteSpace(Worker)
                            ? string.Empty
                            : Worker.Trim(),
                        BoxType = "OPEN",
                        IsPartialBox = false,
                        BoxDate = BoxDate,
                        ScanLabelDate = ScanLabelDate,
                        ActualQty = persistedProgress,
                        TargetQty = SelectedQuantity
                    }
                    : null;

                ScanSessionState persistedSession = BuildCurrentScanSession(
                    InJob,
                    persistedProgress);

                try
                {
                    await Task.Run(() =>
                    {
                        using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
                        using (SQLiteTransaction transaction = connection.BeginTransaction())
                        {
                            _scanHistoryRepository.InsertScanHistory(
                                persistedHistory,
                                connection,
                                transaction);

                            if (isPass)
                            {
                                if (isFirstPassInBox)
                                {
                                    _boxProductRepository.InsertBoxProduct(
                                        persistedBox,
                                        connection,
                                        transaction);
                                }
                                else
                                {
                                    _boxProductRepository.UpdateBoxProgress(
                                        persistedBoxName,
                                        ScanLabelDate,
                                        connection,
                                        transaction);
                                }
                            }

                            _scanSessionService.SaveCurrentSession(
                                persistedSession,
                                connection,
                                transaction);
                            transaction.Commit();
                        }
                    });
                }
                catch (SQLiteException ex) when (
                    ScanHistoryRepository.IsDuplicatePassLotViolation(ex))
                {
                    if (allocatedBoxName)
                    {
                        _CurrentBoxName = null;
                        _currentBoxCreatedDate = null;
                        OnPropertyChanged(nameof(BoxDate));
                        OnPropertyChanged(nameof(BoxDateText));
                        OnPropertyChanged(nameof(HasOpenScanSession));
                    }
                    _CurrentScanHistory = null;
                    ResetScanStatus();
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Trùng LotNo",
                        "LotNo này đã tồn tại trong lịch sử PASS. Không được scan trùng.",
                        "TRÙNG LOT");
                    return;
                }
                catch (SQLiteException ex) when (
                    ScanHistoryRepository.IsUniqueScanDataViolation(ex))
                {
                    if (allocatedBoxName)
                    {
                        _CurrentBoxName = null;
                        _currentBoxCreatedDate = null;
                        OnPropertyChanged(nameof(BoxDate));
                        OnPropertyChanged(nameof(BoxDateText));
                        OnPropertyChanged(nameof(HasOpenScanSession));
                    }
                    _CurrentScanHistory = null;
                    ResetScanStatus();
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Trùng tem trong lịch sử PASS",
                        "Tem này đã tồn tại trong lịch sử scan. Không được scan trùng.",
                        "TRÙNG TEM");
                    return;
                }
                catch (Exception ex)
                {
                    StartupManager.Log("Khong luu duoc lich su scan. Database path: " +
                        Model.Respository.DatabaseRepository.DatabasePath + ". Chi tiet: " + ex);

                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Không lưu được dữ liệu scan vào database.\nVui lòng kiểm tra quyền ghi database và liên hệ kỹ thuật.",
                        "LOI LUU DATABASE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    ScanSoundService.PlayNg();
                    if (allocatedBoxName)
                    {
                        _CurrentBoxName = null;
                        _currentBoxCreatedDate = null;
                        OnPropertyChanged(nameof(HasOpenScanSession));
                    }
                    _CurrentScanHistory = null;
                    ScanTextResult = string.Empty;
                    ResetScanStatus();
                    return;
                }

                ScanTextResult = isPass ? OK : NG;
                if (isPass)
                {
                    ScanSoundService.PlayOk();
                    InputScanCode = string.Empty;
                }
                else
                {
                    ScanErrorPresentation error = BuildNgErrorPresentation(inputScanCode);
                    ScanResultDetailText = error.ToDisplayText();
                    ShowScanErrorWindow(error);
                }

                if (isPass)
                {
                    CurrentScanProgress = persistedProgress;
                    if (SelectedQuantity > 0 && persistedProgress >= SelectedQuantity)
                        scanUiStateChanged = true;
                }

                ScanHistorySource.Insert(0, _CurrentScanHistory);
                if (isPass)
                    _currentBoxPassScanCodes.Add(inputScanCode);
                if (allocatedBoxName)
                {
                    OnPropertyChanged(nameof(HasOpenScanSession));
                    OnPropertyChanged(nameof(CanModifySessionSelection));
                    OnPropertyChanged(nameof(CurrentBoxNameText));
                }

                if (isPass)
                {
                    ApplyPersistedBox(persistedBox, persistedProgress);
                }

                if (isPass && SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity)
                {
                    await CompleteBoxAsync(false);
                }
            }
            finally
            {
                _isScanBusy = false;
                if (scanUiStateChanged)
                {
                    NotifyCurrentBoxStatusChanged();
                    OnPropertyChanged(nameof(CanSwitchProduct));
                    OnPropertyChanged(nameof(CanModifySessionSelection));
                    CommandManager.InvalidateRequerySuggested();
                }
                _scanWriteLock.Release();
            }
        }

        private void RecordIgnoredFastDuplicateScan(
            string inputScanCode,
            string scanMessage,
            string displayMessage)
        {
            _ScanMess = scanMessage;
            ResetScanStatus();
            ScanTextResult = NG;
            ScanResultDetailText = displayMessage;
            InputScanCode = string.Empty;
            ScanSoundService.PlayNg();
            StartupManager.SetStatus(displayMessage);
        }

        private async Task RecordRejectedScanAsync(
            string inputScanCode,
            string scanMessage,
            string displayMessage,
            string title)
        {
            inputScanCode = inputScanCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(inputScanCode))
                return;

            if (ScanHistorySource == null)
                ScanHistorySource = new ObservableCollection<ScanHistory>();

            bool allocatedBoxName = false;
            if (string.IsNullOrWhiteSpace(_CurrentBoxName))
            {
                allocatedBoxName = true;
                _currentBoxCreatedDate = BoxDate.Date;
                _CurrentBoxName = await Task.Run(
                    () => _boxProductRepository.GetNextBoxName(BoxDate));
                OnPropertyChanged(nameof(BoxDate));
                OnPropertyChanged(nameof(BoxDateText));
                NotifyCurrentBoxStatusChanged();
            }

            ResetScanStatus();
            ScanTextResult = NG;
            ScanResultDetailText = scanMessage;

            var rejectedHistory = new ScanHistory
            {
                ScanTime = GetBusinessScanTime(),
                BoxDate = BoxDate,
                ScanLabelDate = ScanLabelDate,
                ProductPartNumber = SelectedPartNumber,
                ProductPartName = PNameExpected,
                BoxName = _CurrentBoxName,
                SealNo = ScanLabelDate.ToString("yyMMdd"),
                ScanData = inputScanCode,
                ScanResult = false,
                ScanMessage = scanMessage,
                ScanWorker = Worker ?? string.Empty,
                ActualQty = CurrentScanProgress,
                TargetQty = SelectedQuantity
            };

            ScanSessionState persistedSession = BuildCurrentScanSession(
                InJob,
                CurrentScanProgress);

            try
            {
                await Task.Run(() =>
                {
                    using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
                    using (SQLiteTransaction transaction = connection.BeginTransaction())
                    {
                        _scanHistoryRepository.InsertScanHistory(
                            rejectedHistory,
                            connection,
                            transaction);

                        _scanSessionService.SaveCurrentSession(
                            persistedSession,
                            connection,
                            transaction);

                        transaction.Commit();
                    }
                });
            }
            catch (Exception ex)
            {
                StartupManager.Log("Khong luu duoc lich su scan loi. Database path: " +
                    Model.Respository.DatabaseRepository.DatabasePath + ". Chi tiet: " + ex);

                if (allocatedBoxName)
                {
                    _CurrentBoxName = null;
                    _currentBoxCreatedDate = null;
                    OnPropertyChanged(nameof(BoxDate));
                    OnPropertyChanged(nameof(BoxDateText));
                    NotifyCurrentBoxStatusChanged();
                    OnPropertyChanged(nameof(HasOpenScanSession));
                }

                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không lưu được lỗi scan vào database.\nVui lòng kiểm tra quyền ghi database và liên hệ kỹ thuật.",
                    "LỖI LƯU DATABASE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ScanSoundService.PlayNg();
                return;
            }

            _CurrentScanHistory = rejectedHistory;
            ScanHistorySource.Insert(0, rejectedHistory);
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();

            ScanSoundService.PlayNg();
            InputScanCode = string.Empty;
            StartupManager.SetStatus(displayMessage);

            var error = new ScanErrorPresentation
            {
                Detail = scanMessage,
                Standard = "Tem hợp lệ, chưa bị trùng và thùng chưa đủ số lượng",
                Actual = ScanValidationService.DisplayValue(inputScanCode),
                Resolution = displayMessage
            };
            ScanResultDetailText = error.ToDisplayText();
            ShowScanErrorWindow(error);
        }


        private void StartScaning(string selectedPartNumber)
        {
            if (ScanHistorySource == null || ScanHistorySource.Count == 0)
            {
                ScanHistorySource = new ObservableCollection<ScanHistory>();
            }

            SaveCurrentScanSession(true);
        }

        private void EnsureCurrentBoxPassScanCache()
        {
            if (_isScanCodeCacheInitialized && string.Equals(
                    _scanCodeCacheBoxName,
                    _CurrentBoxName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentBoxPassScanCodes.Clear();
            if (ScanHistorySource != null)
            {
                foreach (ScanHistory item in ScanHistorySource)
                {
                    if (item != null &&
                        item.ScanResult &&
                        !string.IsNullOrWhiteSpace(item.ScanData))
                    {
                        _currentBoxPassScanCodes.Add(item.ScanData.Trim());
                    }
                }
            }
            _scanCodeCacheBoxName = _CurrentBoxName;
            _isScanCodeCacheInitialized = true;
        }

        private void ResetCurrentBoxPassScanCache()
        {
            _currentBoxPassScanCodes.Clear();
            _scanCodeCacheBoxName = null;
            _isScanCodeCacheInitialized = false;
        }

        private async Task CancelCurrentBoxAsync()
        {
            try
            {
                var dialog = new ConfirmDeleteBoxWD();
                Window owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                bool? result = dialog.ShowDialog();
                if (result != true)
                    return;
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong mo duoc man hinh xac nhan huy thung. Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không thể mở màn hình xác nhận hủy thùng.\n\n" + ex.Message,
                    "Lỗi giao diện",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            await _scanWriteLock.WaitAsync();
            _isScanBusy = true;
            NotifyCurrentBoxStatusChanged();
            OnPropertyChanged(nameof(CanSwitchProduct));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();
            try
            {
                string cancelledBoxName = _CurrentBoxName;
                string cancelledProductCode = SelectedPartNumber;
                DateTime cancelledBoxCreatedDate = GetCurrentBoxCreatedDate();

                await Task.Run(() =>
                {
                    using (SQLiteConnection connection =
                        DatabaseRepository.CreateConnection())
                    using (SQLiteTransaction transaction =
                        connection.BeginTransaction())
                    {
                        _scanHistoryRepository.CancelBoxByBoxName(
                            cancelledBoxName,
                            Worker,
                            connection,
                            transaction);
                        _boxProductRepository.CancelBox(
                            cancelledBoxName,
                            Worker,
                            connection,
                            transaction);
                        _scanSessionService.RemoveSession(
                            cancelledProductCode,
                            cancelledBoxCreatedDate,
                            connection,
                            transaction);
                        transaction.Commit();
                    }
                });

                BoxProduct cancelledBox = ToDayBoxSource?.FirstOrDefault(
                    x => x.BoxName == cancelledBoxName);
                if (cancelledBox != null)
                {
                    cancelledBox.BoxComplete = true;
                    cancelledBox.BoxType = "CANCELLED";
                    cancelledBox.IsPartialBox = false;
                    cancelledBox.BoxWorker = Worker ?? string.Empty;
                }

                ScanHistorySource?.Clear();
                CurrentScanProgress = 0;
                InputScanCode = string.Empty;
                _CurrentBoxName = null;
                _currentBoxCreatedDate = null;
                ResetScanStatus();
                ScanTextResult = string.Empty;
                InJob = false;
                ToDayBoxView?.Refresh();
                RefreshDashboardStats();
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                OnPropertyChanged(nameof(CanModifySessionSelection));

                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "ĐÃ HỦY THÙNG HIỆN TẠI",
                    "THÔNG BÁO",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong huy duoc thung " + _CurrentBoxName + ". Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không hủy được thùng trong database. Dữ liệu hiện tại vẫn được giữ nguyên.",
                    "LỖI HỦY THÙNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isScanBusy = false;
                NotifyCurrentBoxStatusChanged();
                OnPropertyChanged(nameof(CanSwitchProduct));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                CommandManager.InvalidateRequerySuggested();
                _scanWriteLock.Release();
            }
        }

        private void CancelCurrentBox()
        {
            try
            {
                var dialog = new ConfirmDeleteBoxWD();
                Window owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                bool? result = dialog.ShowDialog();
                if (result != true)
                    return;
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong mo duoc man hinh xac nhan huy thung. Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không thể mở màn hình xác nhận hủy thùng.\n\n" + ex.Message,
                    "Lỗi giao diện",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string cancelledBoxName = _CurrentBoxName;
            if (!string.IsNullOrWhiteSpace(cancelledBoxName))
            {
                BoxProduct cancelledBox = ToDayBoxSource?.FirstOrDefault(x => x.BoxName == cancelledBoxName);
                if (cancelledBox != null)
                {
                    ToDayBoxSource.Remove(cancelledBox);
                    ToDayBoxView?.Refresh();
                    RefreshDashboardStats();
                }
            }

            _scanSessionService.RemoveSession(
                SelectedPartNumber,
                GetCurrentBoxCreatedDate());
            ScanHistorySource?.Clear();

            CurrentScanProgress = 0;

            InputScanCode = string.Empty;

            _CurrentBoxName = null;
            _currentBoxCreatedDate = null;

            ResetScanStatus();

            ScanTextResult = string.Empty;
            InJob = false;
            OnPropertyChanged(nameof(HasOpenScanSession));

            SX3_SCANER.Helper.ProfessionalMessageBox.Show("ĐÃ HỦY THÙNG HIỆN TẠI", "THÔNG BÁO", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool ShowWorkerCompletePopup()
        {
            bool isConfirmed = false;

            Window owner = Application.Current?.MainWindow;

            Window wd = new Window
            {
                Title = "Xác nhận hoàn thành thùng",
                Width = 640,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null && owner.IsVisible
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false
            };

            if (owner != null && owner.IsVisible)
            {
                wd.Owner = owner;
            }

            System.Windows.Media.SolidColorBrush greenBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(22, 163, 74));

            System.Windows.Media.SolidColorBrush darkTextBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(17, 24, 39));

            System.Windows.Media.SolidColorBrush mutedTextBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(107, 114, 128));

            System.Windows.Media.SolidColorBrush borderBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(229, 231, 235));

            System.Windows.Media.SolidColorBrush softGreenBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(240, 253, 244));

            System.Windows.Media.SolidColorBrush dangerBrush =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(220, 38, 38));

            Border root = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.22
                }
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Child = mainGrid;

            Border header = new Border
            {
                Background = greenBrush,
                CornerRadius = new CornerRadius(18, 18, 0, 0),
                Padding = new Thickness(28, 22, 28, 22)
            };

            header.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    wd.DragMove();
                }
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border iconCircle = new Border
            {
                Width = 54,
                Height = 54,
                CornerRadius = new CornerRadius(27),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(45, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 32,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetColumn(iconCircle, 0);
            headerGrid.Children.Add(iconCircle);

            StackPanel headerTextPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            headerTextPanel.Children.Add(new TextBlock
            {
                Text = "HOÀN THÀNH THÙNG",
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            });

            headerTextPanel.Children.Add(new TextBlock
            {
                Text = "Xác nhận công nhân trước khi đóng thùng",
                FontSize = 14,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(230, 255, 255, 255))
            });

            Grid.SetColumn(headerTextPanel, 2);
            headerGrid.Children.Add(headerTextPanel);

            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            StackPanel content = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 20)
            };

            Border summaryBox = new Border
            {
                Background = softGreenBrush,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(187, 247, 208)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18)
            };

            Grid summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void AddInfoRow(string label, string value, int rowIndex)
            {
                summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock labelBlock = new TextBlock
                {
                    Text = label,
                    FontSize = 14,
                    Foreground = mutedTextBrush,
                    Margin = new Thickness(0, rowIndex == 0 ? 0 : 8, 14, 0)
                };

                TextBlock valueBlock = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = darkTextBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, rowIndex == 0 ? 0 : 8, 0, 0)
                };

                Grid.SetRow(labelBlock, rowIndex);
                Grid.SetColumn(labelBlock, 0);
                summaryGrid.Children.Add(labelBlock);

                Grid.SetRow(valueBlock, rowIndex);
                Grid.SetColumn(valueBlock, 1);
                summaryGrid.Children.Add(valueBlock);
            }

            AddInfoRow("Mã thùng", _CurrentBoxName, 0);
            AddInfoRow("Mã hàng", SelectedPartNumber, 1);
            AddInfoRow("Tên hàng", PNameExpected, 2);
            AddInfoRow("Số lượng", CurrentScanProgress + " / " + SelectedQuantity, 3);
            AddInfoRow("Ngày tem", ScanLabelDate.ToString("dd/MM/yyyy"), 4);

            summaryBox.Child = summaryGrid;
            content.Children.Add(summaryBox);

            TextBlock workerLabel = new TextBlock
            {
                Text = "Công nhân thực hiện",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = darkTextBrush,
                Margin = new Thickness(0, 22, 0, 8)
            };

            content.Children.Add(workerLabel);

            TextBox txtWorker = new TextBox
            {
                // Luôn bắt buộc nhập lại, không gợi sẵn tên của thùng trước.
                Text = string.Empty,
                FontSize = 24,
                Height = 56,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(14, 0, 14, 0),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Foreground = darkTextBrush
            };

            content.Children.Add(txtWorker);

            TextBlock errorText = new TextBlock
            {
                Text = "Vui lòng nhập tên công nhân trước khi xác nhận.",
                FontSize = 13,
                Foreground = dangerBrush,
                Margin = new Thickness(2, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };

            content.Children.Add(errorText);

            TextBlock hintText = new TextBlock
            {
                Text = "Có thể quét mã nhân viên hoặc nhập tên công nhân, sau đó nhấn Enter để xác nhận.",
                FontSize = 13,
                Foreground = mutedTextBrush,
                Margin = new Thickness(2, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            content.Children.Add(hintText);

            Grid.SetRow(content, 1);
            mainGrid.Children.Add(content);

            Border footer = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(249, 250, 251)),
                CornerRadius = new CornerRadius(0, 0, 18, 18),
                Padding = new Thickness(28, 18, 28, 22)
            };

            Button btnOK = new Button
            {
                Content = "XÁC NHẬN ĐÓNG THÙNG",
                Height = 54,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Background = greenBrush,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            footer.Child = btnOK;
            Grid.SetRow(footer, 2);
            mainGrid.Children.Add(footer);

            void ConfirmWorker()
            {
                string workerName = txtWorker.Text == null
                    ? string.Empty
                    : txtWorker.Text.Trim();

                if (string.IsNullOrWhiteSpace(workerName))
                {
                    errorText.Visibility = Visibility.Visible;
                    txtWorker.Focus();
                    txtWorker.SelectAll();
                    return;
                }

                errorText.Visibility = Visibility.Collapsed;

                Worker = workerName;

                isConfirmed = true;
                wd.DialogResult = true;
                wd.Close();
            }

            btnOK.Click += (s, e) => ConfirmWorker();

            txtWorker.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ConfirmWorker();
                    e.Handled = true;
                }
            };

            wd.Closing += (s, e) =>
            {
                if (!isConfirmed)
                {
                    e.Cancel = true;
                    errorText.Text = "Bắt buộc xác nhận công nhân để hoàn thành thùng.";
                    errorText.Visibility = Visibility.Visible;
                    txtWorker.Focus();
                    txtWorker.SelectAll();
                }
            };

            wd.Loaded += (s, e) =>
            {
                txtWorker.Focus();
                txtWorker.SelectAll();
            };

            wd.Content = root;
            wd.ShowDialog();

            return isConfirmed;
        }
    }
}
