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
        private bool _duplicateLotForCurrentScan;
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

                    AppConfigHelper.Modify(AppConfigStringKey.LastWorker, Worker ?? string.Empty);
                }
            }
        }

        private int _CurrentScanProgress;

        public int CurrentScanProgress
        {
            get { return _CurrentScanProgress; }
            set
            {
                _CurrentScanProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                OnPropertyChanged(nameof(ScanQuantityText));
                OnPropertyChanged(nameof(CurrentPassCount));
                OnPropertyChanged(nameof(CurrentNgCount));
                OnPropertyChanged(nameof(CurrentProgressPercentText));
                NotifyCurrentBoxStatusChanged();
                CommandManager.InvalidateRequerySuggested();
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
            set { _Worker = value; OnPropertyChanged(); }
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
                    _InputScanCodeCMD = new RelayCommand<object>(
                        o => IsApplicationReady &&
                            InJob &&
                            !string.IsNullOrWhiteSpace(InputScanCode) &&
                            !IsFullBoxReadyToComplete,
                        async o => await ScanDataAsync(InputScanCode));
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
                    _CancelBoxCMD = new RelayCommand<object>(
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
                    _ClosePartialBoxCMD = new RelayCommand<object>(
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
                    _CompleteFullBoxCMD = new RelayCommand<object>(
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
            NotifyCurrentBoxStatusChanged();
            OnPropertyChanged(nameof(CanSwitchProduct));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();
            try
            {
                inputScanCode = inputScanCode?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(inputScanCode))
                    return;

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

                if (ScanHistorySource != null &&
                    ScanHistorySource.Any(item =>
                        item.ScanResult &&
                        string.Equals(
                            item.ScanData,
                            inputScanCode,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Trùng tem trong thùng hiện tại",
                        "Tem này đã được scan trong thùng hiện tại.",
                        "TRÙNG TEM");
                    return;
                }

                if (await Task.Run(() => _scanHistoryRepository.ScanDataExists(inputScanCode)))
                {
                    await RecordRejectedScanAsync(
                        inputScanCode,
                        "NG - Trùng tem trong lịch sử PASS",
                        "Tem này đã tồn tại trong lịch sử scan. Không được scan trùng.",
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
                    OnPropertyChanged(nameof(BoxDate));
                    OnPropertyChanged(nameof(BoxDateText));
                    NotifyCurrentBoxStatusChanged();
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

                _duplicateLotForCurrentScan = await IsDuplicateLotAsync(inputScanCode);
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

                var persistedHistoryItems = new List<ScanHistory> { persistedHistory };
                if (ScanHistorySource != null)
                {
                    persistedHistoryItems.AddRange(ScanHistorySource);
                }

                ScanSessionState persistedSession = BuildCurrentScanSession(
                    InJob,
                    persistedProgress,
                    persistedHistoryItems);

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
                    _duplicateLotForCurrentScan = false;
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
                    _duplicateLotForCurrentScan = false;
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
                }

                ScanHistorySource.Insert(0, _CurrentScanHistory);
                RefreshScanHistoryDisplayIndex();
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                CommandManager.InvalidateRequerySuggested();

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
                NotifyCurrentBoxStatusChanged();
                OnPropertyChanged(nameof(CanSwitchProduct));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                CommandManager.InvalidateRequerySuggested();
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

            var persistedHistoryItems = new List<ScanHistory> { rejectedHistory };
            persistedHistoryItems.AddRange(ScanHistorySource);

            ScanSessionState persistedSession = BuildCurrentScanSession(
                InJob,
                CurrentScanProgress,
                persistedHistoryItems);

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
            RefreshScanHistoryDisplayIndex();
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

        private async Task ClosePartialBoxAsync()
        {
            await _scanWriteLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(_CurrentBoxName) ||
                    ScanHistorySource == null ||
                    ScanHistorySource.Count == 0 ||
                    CurrentScanProgress <= 0 ||
                    (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity))
                {
                    return;
                }

                try
                {
                    var dialog = new ConfirmPartialBoxWD(
                        CurrentScanProgress,
                        SelectedQuantity);
                    Window owner = Application.Current?.MainWindow;
                    if (owner != null && owner.IsVisible)
                    {
                        dialog.Owner = owner;
                    }

                    bool? result = dialog.ShowDialog();
                    if (result == true)
                    {
                        await CompleteBoxAsync(true);
                    }
                }
                catch (Exception ex)
                {
                    StartupManager.Log(
                        "Khong mo duoc man hinh xac nhan dong thung le. Chi tiet: " + ex);
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Không thể mở màn hình xác nhận đóng thùng lẻ.\n\n" + ex.Message,
                        "Lỗi giao diện",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _scanWriteLock.Release();
            }
        }

        private async Task CompleteFullBoxWithLockAsync()
        {
            await _scanWriteLock.WaitAsync();
            _isScanBusy = true;
            NotifyCurrentBoxStatusChanged();
            OnPropertyChanged(nameof(CanSwitchProduct));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await CompleteBoxAsync(false);
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

        private async Task CompleteBoxAsync(bool isPartial)
        {
            if (string.IsNullOrWhiteSpace(_CurrentBoxName) || CurrentScanProgress <= 0)
                return;

            if (isPartial)
            {
                if (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity)
                    return;
            }
            else if (SelectedQuantity <= 0 || CurrentScanProgress < SelectedQuantity)
            {
                return;
            }

            if (!ShowWorkerCompletePopup())
                return;

            string completedBoxName = _CurrentBoxName;
            string completedProductCode = SelectedPartNumber;
            DateTime completedBoxCreatedDate = GetCurrentBoxCreatedDate();
            string boxType = isPartial ? "PARTIAL" : "FULL";

            try
            {
                await Task.Run(() =>
                {
                    using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
                    using (SQLiteTransaction transaction = connection.BeginTransaction())
                    {
                        _scanHistoryRepository.UpdateWorkerByBoxName(
                            completedBoxName,
                            Worker,
                            connection,
                            transaction);
                        _scanHistoryRepository.SetBoxTypeByBoxName(
                            completedBoxName,
                            isPartial,
                            connection,
                            transaction);
                        _boxProductRepository.SetBoxComplete(
                            completedBoxName,
                            isPartial,
                            Worker,
                            connection,
                            transaction);
                        _scanSessionService.RemoveSession(
                            completedProductCode,
                            completedBoxCreatedDate,
                            connection,
                            transaction);
                        transaction.Commit();
                    }
                });
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong hoan tat duoc thung " + completedBoxName + ". Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không hoàn tất được thùng trong database.\nDữ liệu hiện tại vẫn được giữ nguyên để thử lại.",
                    "LỖI HOÀN TẤT THÙNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            foreach (var item in ScanHistorySource)
            {
                item.ScanWorker = Worker;
                item.BoxType = boxType;
                item.IsPartialBox = isPartial;
            }

            BoxProduct completedBox = ToDayBoxSource?.FirstOrDefault(x => x.BoxName == completedBoxName);
            if (completedBox != null)
            {
                completedBox.BoxProgress = CurrentScanProgress;
                completedBox.BoxWorker = Worker;
                completedBox.BoxType = boxType;
                completedBox.IsPartialBox = isPartial;
                completedBox.BoxComplete = true;
            }

            ScanHistoryView?.Refresh();
            ToDayBoxView?.Refresh();
            RefreshDashboardStats();

            CurrentScanProgress = 0;
            ScanHistorySource = new ObservableCollection<ScanHistory>();
            InputScanCode = string.Empty;
            _CurrentBoxName = null;
            _currentBoxCreatedDate = null;
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            NotifyCurrentBoxStatusChanged();
            ResetScanStatus();
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();

            string message = isPartial
                ? "HO\u00C0N TH\u00C0NH TH\u00D9NG L\u1EBA"
                : "HO\u00C0N TH\u00C0NH TH\u00D9NG \u0110\u1EE6";
            SX3_SCANER.Helper.ProfessionalMessageBox.Show(message, "TH\u00D4NG B\u00C1O", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task<bool> IsDuplicateLotAsync(string input)
        {
            // Format mới: PARTNO,SERIAL (ví dụ K32000-22400,2606172001)
            // không có SealNo/LotNo theo cấu trúc cũ nên không kiểm tra trùng LotNo.
            // Trùng tem vẫn được chặn bằng ScanData gốc ở phía trên.
            if (ScanValidationService.IsPartNoCommaSerialFormat(input))
                return false;

            if (string.IsNullOrWhiteSpace(input) || input.Length != CodeLengthExpected)
                return false;

            int lotStart = (PrefixExpected?.Length ?? 0) +
                (PNameExpected?.Length ?? 0) +
                (SealNoExpected?.Length ?? 0);

            if (input.Length < lotStart + 4)
                return false;

            string lotNo = input.Substring(lotStart, 4);
            if (!int.TryParse(lotNo, out int _))
                return false;

            return await Task.Run(
                () => _scanHistoryRepository.CheckExist(
                    SelectedPartNumber,
                    SealNoExpected,
                    lotNo));
        }

        private void ResetScanStatus()
        {
            CodeLengthScanResult = 0;

            PrefixScanResut = string.Empty;

            PNameScanResult = string.Empty;

            SealnoScanResult = string.Empty;

            LotNoScanResult = string.Empty;

            SuffixScanResult = string.Empty;

            Length_OK = -1;

            Prefix_OK = -1;

            Suffix_OK = -1;

            PName_OK = -1;

            SealNo_OK = -1;

            LotNo_OK = -1;

            _ScanMess = string.Empty;
        }

        private bool Scan(string input)
        {
            _CurrentScanHistory.ScanTime = GetBusinessScanTime();

            // Format mới cho mã hàng Car HE EV:
            // PART NO/PARTNAME: K32000-22400
            // QR thực tế: K32000-22400,2606172001
            // Cấu trúc: <PartNo>,<Serial>
            if (ScanValidationService.IsPartNoCommaSerialFormat(input))
            {
                return ScanPartNoCommaSerial(input);
            }

            return CheckLength(input);
        }

        private bool TryParsePartNoCommaSerial(
            string input,
            out string partNo,
            out string serial)
        {
            partNo = string.Empty;
            serial = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Trim().Split(',');
            if (parts.Length != 2)
                return false;

            partNo = parts[0].Trim();
            serial = parts[1].Trim();

            return !string.IsNullOrWhiteSpace(partNo) &&
                !string.IsNullOrWhiteSpace(serial);
        }

        private bool ScanPartNoCommaSerial(string input)
        {
            CodeLengthScanResult = input?.Length ?? 0;

            if (!TryParsePartNoCommaSerial(input, out string scannedPartNo, out string scannedSerial))
            {
                Length_OK = 0;
                _ScanMess = "NG - Sai định dạng QR dấu phẩy";
                return false;
            }

            if (CodeLengthExpected > 0 && input.Length != CodeLengthExpected)
            {
                Length_OK = 0;
                _ScanMess = "NG - Sai độ dài";
                return false;
            }

            Length_OK = 1;

            if (!string.IsNullOrWhiteSpace(PrefixExpected) &&
                !scannedPartNo.StartsWith(PrefixExpected, StringComparison.OrdinalIgnoreCase))
            {
                Prefix_OK = 0;
                _ScanMess = "NG - Sai đầu mã / Prefix";
                return false;
            }

            PrefixScanResut = PrefixExpected ?? string.Empty;
            Prefix_OK = 1;

            string expectedPartName = ScanValidationService.NormalizeQrProductCode(PNameExpected);
            string expectedPartNumber = ScanValidationService.NormalizeQrProductCode(SelectedPartNumber);
            string actualPartNo = ScanValidationService.NormalizeQrProductCode(scannedPartNo);

            if (string.IsNullOrWhiteSpace(actualPartNo) ||
                (actualPartNo != expectedPartName && actualPartNo != expectedPartNumber))
            {
                PName_OK = 0;
                PNameScanResult = scannedPartNo;
                _ScanMess = "NG - Sai mã sản phẩm / PartName";
                return false;
            }

            PName_OK = 1;
            PNameScanResult = scannedPartNo;
            _CurrentScanHistory.ProductPartName = PNameExpected;

            // QR mới không chứa ngày sản xuất/SealNo theo format cũ.
            // Vẫn lưu SealNo theo ngày tem đang chọn để báo cáo/thống kê không bị rỗng.
            SealNo_OK = 1;
            SealnoScanResult = ScanLabelDate.ToString("yyMMdd");
            _CurrentScanHistory.SealNo = SealnoScanResult;

            if (!string.IsNullOrWhiteSpace(SuffixExpected) &&
                !scannedSerial.EndsWith(SuffixExpected, StringComparison.OrdinalIgnoreCase))
            {
                Suffix_OK = 0;
                SuffixScanResult = scannedSerial;
                _ScanMess = "NG - Sai cuối mã / Suffix";
                return false;
            }

            Suffix_OK = 1;
            SuffixScanResult = SuffixExpected ?? string.Empty;

            LotNo_OK = 1;
            LotNoExpected = scannedSerial;
            LotNoScanResult = scannedSerial;
            _CurrentScanHistory.LotNo = scannedSerial;

            return true;
        }

        private DateTime GetBusinessScanTime()
        {
            return ScanLabelDate.Date.Add(DateTime.Now.TimeOfDay);
        }

        private void ShowDuplicateScanMessage(string message, string scanData)
        {
            ScanTextResult = NG;
            var error = new ScanErrorPresentation
            {
                Detail = "NG - Tr\u00F9ng tem",
                Standard = "M\u1ED7i tem ch\u1EC9 \u0111\u01B0\u1EE3c scan m\u1ED9t l\u1EA7n",
                Actual = ScanValidationService.DisplayValue(scanData),
                Resolution = "Ki\u1EC3m tra l\u1EA1i tem v\u00E0 l\u1ECBch s\u1EED scan."
            };
            ScanResultDetailText = error.ToDisplayText();
            InputScanCode = string.Empty;
            StartupManager.SetStatus(message);
            ShowScanErrorWindow(error);
        }

        private void ShowScanErrorWindow(ScanErrorPresentation error)
        {
            if (error == null)
                return;

            ScanSoundService.PlayNg();
            try
            {
                var dialog = new ScanErrorWD(
                    error.Detail,
                    error.Standard,
                    error.Actual,
                    error.Resolution);
                Window owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong mo duoc man hinh ket qua scan NG. Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    error.ToDisplayText() + "\n\nChi tiết lỗi giao diện: " + ex.Message,
                    "KẾT QUẢ SCAN: NG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool CheckLength(string input)
        {
            CodeLengthScanResult = input.Length;

            if (input.Length != CodeLengthExpected)
            {
                Length_OK = 0;

                _ScanMess = "NG - Sai \u0111\u1ED9 d\u00E0i";

                return false;
            }

            Length_OK = 1;

            return CheckPrefix(input);
        }

        private bool CheckPrefix(string input)
        {
            if (!string.IsNullOrWhiteSpace(PrefixExpected)
                && !input.StartsWith(PrefixExpected, StringComparison.Ordinal))
            {
                Prefix_OK = 0;

                _ScanMess = "NG - Sai \u0111\u1EA7u m\u00E3 / Prefix";

                return false;
            }

            PrefixScanResut = PrefixExpected ?? string.Empty;

            Prefix_OK = 1;

            int len = PrefixExpected?.Length ?? 0;

            return CheckProductName(input.Substring(len));
        }

        private bool CheckProductName(string input)
        {
            if (string.IsNullOrWhiteSpace(PNameExpected)
                || !input.StartsWith(PNameExpected, StringComparison.Ordinal))
            {
                PName_OK = 0;

                _ScanMess = "NG - Sai m\u00E3 s\u1EA3n ph\u1EA9m / PartName";

                return false;
            }

            PNameScanResult = PNameExpected;

            PName_OK = 1;

            _CurrentScanHistory.ProductPartName = PNameExpected;

            return CheckSealNo(input.Substring(PNameExpected.Length));
        }

        private bool CheckSealNo(string input)
        {
            if (string.IsNullOrWhiteSpace(SealNoExpected)
                || !input.StartsWith(SealNoExpected, StringComparison.Ordinal))
            {
                SealNo_OK = 0;

                _ScanMess = "NG - Sai ngày / SealNo";

                return false;
            }

            SealnoScanResult = SealNoExpected;

            _CurrentScanHistory.SealNo = SealNoExpected;

            SealNo_OK = 1;

            return CheckLotNo(input.Substring(SealNoExpected.Length));
        }

        private bool CheckLotNo(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length < 4)
            {
                LotNo_OK = 0;

                _ScanMess = "NG - Sai LotNo";

                return false;
            }

            string lotNo = input.Substring(0, 4);

            if (lotNo.Length != 4 ||
                !lotNo.StartsWith("2", StringComparison.Ordinal) ||
                !int.TryParse(lotNo, out int _))
            {
                LotNo_OK = 0;

                _ScanMess = "NG - Sai LotNo";

                return false;
            }

            if (_duplicateLotForCurrentScan)
            {
                LotNo_OK = 0;

                _ScanMess = "NG - Trùng LotNo";

                return false;
            }

            LotNo_OK = 1;

            LotNoExpected = lotNo;

            LotNoScanResult = lotNo;

            _CurrentScanHistory.LotNo = lotNo;

            return CheckSuffix(input.Substring(4));
        }

        private bool CheckSuffix(string input)
        {
            if (!string.IsNullOrWhiteSpace(SuffixExpected)
                && !input.EndsWith(SuffixExpected, StringComparison.Ordinal))
            {
                Suffix_OK = 0;

                _ScanMess = "NG - Sai cu\u1ED1i m\u00E3 / Suffix";

                return false;
            }

            SuffixScanResult = input;

            Suffix_OK = 1;

            return true;
        }

        private string TryExtractLotNo(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            int lotStart = (PrefixExpected?.Length ?? 0) +
                (PNameExpected?.Length ?? 0) +
                (SealNoExpected?.Length ?? 0);

            if (input.Length < lotStart + 4)
                return string.Empty;

            return input.Substring(lotStart, 4);
        }

        private ScanErrorPresentation BuildNgErrorPresentation(string inputScanCode)
        {
            string normalizedMessage = ScanHistory.RemoveVietnameseSigns(
                ScanHistory.NormalizeScanMessage(_ScanMess))
                .ToUpperInvariant();
            string lotNo = !string.IsNullOrWhiteSpace(_CurrentScanHistory?.LotNo)
                ? _CurrentScanHistory.LotNo
                : TryExtractLotNo(inputScanCode);
            string actualPartName = ScanValidationService.ExtractSegment(
                inputScanCode,
                PrefixExpected?.Length ?? 0,
                PNameExpected?.Length ?? 0);
            string actualSealNo = ScanValidationService.ExtractSegment(
                inputScanCode,
                (PrefixExpected?.Length ?? 0) + (PNameExpected?.Length ?? 0),
                SealNoExpected?.Length ?? 0);

            if (normalizedMessage.Contains("LOT"))
            {
                bool isDuplicate = normalizedMessage.Contains("TRUNG");
                return new ScanErrorPresentation
                {
                    Detail = isDuplicate ? "NG - Tr\u00F9ng LotNo" : "NG - Sai LotNo",
                    Standard = isDuplicate
                        ? "LotNo ch\u01B0a \u0111\u01B0\u1EE3c scan trong th\u00F9ng ho\u1EB7c l\u1ECBch s\u1EED"
                        : "LotNo t\u1EEB 2000 \u0111\u1EBFn 2999",
                    Actual = ScanValidationService.DisplayValue(lotNo),
                    Resolution = isDuplicate
                        ? "Ki\u1EC3m tra l\u1EA1i tem, tr\u00E1nh scan tr\u00F9ng LotNo."
                        : "Ki\u1EC3m tra l\u1EA1i LotNo tr\u00EAn tem."
                };
            }

            if (normalizedMessage.Contains("NGAY") ||
                normalizedMessage.Contains("SEAL"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai ng\u00E0y s\u1EA3n xu\u1EA5t",
                    Standard = ScanValidationService.DisplayValue(SealNoExpected),
                    Actual = ScanValidationService.DisplayValue(actualSealNo),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i ng\u00E0y \u0111ang ch\u1ECDn ho\u1EB7c tem \u0111ang scan."
                };
            }

            if (normalizedMessage.Contains("PARTNAME") ||
                normalizedMessage.Contains("TEN SAN PHAM") ||
                normalizedMessage.Contains("MA SAN PHAM"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai m\u00E3 s\u1EA3n ph\u1EA9m",
                    Standard = ScanValidationService.DisplayValue(PNameExpected),
                    Actual = ScanValidationService.DisplayValue(actualPartName),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i s\u1EA3n ph\u1EA9m \u0111ang ch\u1ECDn v\u00E0 tem scan."
                };
            }

            if (normalizedMessage.Contains("DINH DANG") ||
                normalizedMessage.Contains("FORMAT"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai định dạng QR",
                    Standard = "Định dạng đúng: " + ScanValidationService.DisplayValue(PNameExpected) + ",<Serial>  |  Ví dụ: K32000-22400,2606172001",
                    Actual = ScanValidationService.DisplayValue(inputScanCode),
                    Resolution = "Kiểm tra lại QR phải có đúng 1 dấu phẩy, phần trước là mã sản phẩm, phần sau là serial."
                };
            }

            if (normalizedMessage.Contains("DO DAI") ||
                normalizedMessage.Contains("LENGTH"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai \u0111\u1ED9 d\u00E0i m\u00E3",
                    Standard = CodeLengthExpected + " k\u00FD t\u1EF1",
                    Actual = (inputScanCode?.Length ?? 0) + " k\u00FD t\u1EF1",
                    Resolution = "Ki\u1EC3m tra tem c\u00F3 b\u1ECB thi\u1EBFu, th\u1EEBa ho\u1EB7c m\u1EDD k\u00FD t\u1EF1."
                };
            }

            if (normalizedMessage.Contains("PREFIX") ||
                normalizedMessage.Contains("DAU MA"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai \u0111\u1EA7u m\u00E3",
                    Standard = ScanValidationService.DisplayValue(PrefixExpected),
                    Actual = ScanValidationService.DisplayValue(ScanValidationService.ExtractSegment(
                        inputScanCode,
                        0,
                        PrefixExpected?.Length ?? 0)),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i \u0111\u1EA7u m\u00E3 tr\u00EAn tem."
                };
            }

            if (normalizedMessage.Contains("SUFFIX") ||
                normalizedMessage.Contains("CUOI MA"))
            {
                int suffixLength = SuffixExpected?.Length ?? 0;
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai cu\u1ED1i m\u00E3",
                    Standard = ScanValidationService.DisplayValue(SuffixExpected),
                    Actual = ScanValidationService.DisplayValue(ScanValidationService.ExtractSegment(
                        inputScanCode,
                        Math.Max(0, (inputScanCode?.Length ?? 0) - suffixLength),
                        suffixLength)),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i cu\u1ED1i m\u00E3 tr\u00EAn tem."
                };
            }

            return new ScanErrorPresentation
            {
                Detail = ScanHistory.NormalizeScanMessage(_ScanMess),
                Standard = ScanValidationService.DisplayValue(FullCodeExpected),
                Actual = ScanValidationService.DisplayValue(inputScanCode),
                Resolution = "Ki\u1EC3m tra l\u1EA1i tem v\u00E0 s\u1EA3n ph\u1EA9m \u0111ang ch\u1ECDn."
            };
        }

        private sealed class ScanErrorPresentation
        {
            public string Detail { get; set; }
            public string Standard { get; set; }
            public string Actual { get; set; }
            public string Resolution { get; set; }

            public string ToDisplayText()
            {
                var builder = new StringBuilder();
                builder.AppendLine("Chi ti\u1EBFt l\u1ED7i: " + Detail);
                builder.AppendLine("Ti\u00EAu chu\u1EA9n: " + Standard);
                builder.AppendLine("Th\u1EF1c t\u1EBF: " + Actual);
                builder.Append("H\u01B0\u1EDBng x\u1EED l\u00FD: " + Resolution);
                return builder.ToString();
            }
        }

        private void StartScaning(string selectedPartNumber)
        {
            if (ScanHistorySource == null || ScanHistorySource.Count == 0)
            {
                ScanHistorySource = new ObservableCollection<ScanHistory>();
            }

            SaveCurrentScanSession(true);
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

            Window wd = new Window
            {
                Title = "XÁC NHẬN CÔNG NHÂN",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
            };

            Grid grid = new Grid
            {
                Margin = new Thickness(20)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "HOÀN THÀNH THÙNG",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DarkGreen
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            TextBox txtWorker = new TextBox
            {
                Text = Worker ?? string.Empty,
                FontSize = 24,
                Height = 52,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0)
            };
            Grid.SetRow(txtWorker, 2);
            grid.Children.Add(txtWorker);

            Button btnOK = new Button
            {
                Content = "XÁC NHẬN",
                Height = 48,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Background = System.Windows.Media.Brushes.SeaGreen,
                Foreground = System.Windows.Media.Brushes.White
            };

            btnOK.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtWorker.Text))
                {
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Vui lòng nhập tên công nhân",
                        "THIẾU THÔNG TIN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    txtWorker.Focus();
                    return;
                }

                Worker = txtWorker.Text.Trim();
                AppConfigHelper.Modify(AppConfigStringKey.LastWorker, Worker);

                isConfirmed = true;
                wd.DialogResult = true;
                wd.Close();
            };

            Grid.SetRow(btnOK, 4);
            grid.Children.Add(btnOK);

            wd.Content = grid;

            wd.Closing += (s, e) =>
            {
                if (!isConfirmed)
                {
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Bạn phải xác nhận tên công nhân mới được tiếp tục.",
                        "BẮT BUỘC XÁC NHẬN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                }
            };

            wd.Loaded += (s, e) =>
            {
                txtWorker.Focus();
                txtWorker.SelectAll();
            };

            wd.ShowDialog();

            return isConfirmed;
        }
        private void RefreshScanHistoryDisplayIndex()
        {
            if (ScanHistorySource == null) return;

            for (int i = 0; i < ScanHistorySource.Count; i++)
            {
                ScanHistorySource[i].RowIndex = i + 1;
            }

            ScanHistoryView?.Refresh();
            RefreshDashboardStats();
        }

        private void SaveCurrentScanSession(bool isInJob)
        {
            if (string.IsNullOrWhiteSpace(SelectedPartNumber) || !HasOpenScanSession)
            {
                return;
            }

            _scanSessionService.SaveCurrentSession(BuildCurrentScanSession(
                isInJob,
                CurrentScanProgress,
                ScanHistorySource?.ToList() ?? new List<ScanHistory>()));
        }

        private ScanSessionState BuildCurrentScanSession(
            bool isInJob,
            int scannedCount,
            List<ScanHistory> historyItems)
        {
            return new ScanSessionState
            {
                ProductCode = SelectedPartNumber,
                BoxCode = _CurrentBoxName ?? string.Empty,
                ScannedCount = scannedCount,
                TargetCount = SelectedQuantity,
                ScanHistoryItems = historyItems ?? new List<ScanHistory>(),
                IsInJob = isInJob,
                Worker = Worker ?? string.Empty,
                SessionDate = GetCurrentBoxCreatedDate(),
                BoxDate = GetCurrentBoxCreatedDate(),
                ScanLabelDate = ScanLabelDate
            };
        }

        private DateTime GetCurrentBoxCreatedDate()
        {
            if (_currentBoxCreatedDate.HasValue)
            {
                return _currentBoxCreatedDate.Value.Date;
            }

            if (string.IsNullOrWhiteSpace(_CurrentBoxName))
            {
                return BoxDate.Date;
            }

            DateTime? persistedBoxCreatedDate =
                _boxProductRepository.GetBoxCreatedDate(_CurrentBoxName);
            if (persistedBoxCreatedDate.HasValue)
            {
                _currentBoxCreatedDate = persistedBoxCreatedDate.Value.Date;
                return _currentBoxCreatedDate.Value;
            }

            if (!string.IsNullOrWhiteSpace(_CurrentBoxName) &&
                _CurrentBoxName.Length >= 7 &&
                _CurrentBoxName.StartsWith("P"))
            {
                string yymmdd = _CurrentBoxName.Substring(1, 6);
                if (DateTime.TryParseExact(
                    yymmdd,
                    "yyMMdd",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime boxDate))
                {
                    _currentBoxCreatedDate = boxDate.Date;
                    return _currentBoxCreatedDate.Value;
                }
            }

            DateTime? firstScanDate = ScanHistorySource?
                .Where(item => item.ScanTime.HasValue)
                .Select(item => (DateTime?)item.ScanTime.Value.Date)
                .OrderBy(date => date)
                .FirstOrDefault();
            if (firstScanDate.HasValue)
            {
                _currentBoxCreatedDate = firstScanDate.Value;
                return _currentBoxCreatedDate.Value;
            }

            throw new InvalidOperationException(
                "Khong xac dinh duoc ngay tao thung " + _CurrentBoxName + ".");
        }

        private bool RestoreScanSession(string productCode)
        {
            // Chỉ khôi phục phiên theo NGÀY BOX hiện tại.
            // Không LoadLatestSession để tránh tự kéo thùng cũ khác ngày lên.
            ScanSessionState state = _scanSessionService.LoadSession(productCode, BoxDate);
            if (state == null)
            {
                return false;
            }

            _CurrentBoxName = state.BoxCode;
            _currentBoxCreatedDate = state.BoxDate == default(DateTime)
                ? _boxProductRepository.GetBoxCreatedDate(_CurrentBoxName)
                : (DateTime?)state.BoxDate.Date;
            if (_currentBoxCreatedDate.HasValue)
            {
                _SelectedBoxDate = _currentBoxCreatedDate.Value.Date;
            }

            DateTime restoredScanLabelDate = state.ScanLabelDate == default(DateTime)
                ? state.SessionDate.Date
                : state.ScanLabelDate.Date;
            _SelectedDate = restoredScanLabelDate;
            SealNoExpected = restoredScanLabelDate.ToString("yyMMdd");
            SetExpectedData(productCode);
            CurrentScanProgress = state.ScannedCount;
            if (state.TargetCount > 0)
            {
                SelectedQuantity = state.TargetCount;
            }

            if (!string.IsNullOrWhiteSpace(state.Worker))
            {
                Worker = state.Worker;
            }

            ScanHistorySource = new ObservableCollection<ScanHistory>(
                state.ScanHistoryItems ?? new System.Collections.Generic.List<ScanHistory>());
            RefreshScanHistoryDisplayIndex();
            InputScanCode = string.Empty;
            ResetScanStatus();
            InJob = false;
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(ScanLabelDate));
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            OnPropertyChanged(nameof(ScanLabelDateText));
            OnPropertyChanged(nameof(ScanQuantityText));
            NotifyCurrentBoxStatusChanged();
            OnPropertyChanged(nameof(CanModifySessionSelection));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            CommandManager.InvalidateRequerySuggested();
            StartupManager.SetStatus("Đã khôi phục phiên quét mã " + productCode + ".");
            return true;
        }
    }
}
