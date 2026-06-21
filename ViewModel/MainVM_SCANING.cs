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
                    _InputScanCodeCMD = new AsyncRelayCommand<object>(
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
    }
}
