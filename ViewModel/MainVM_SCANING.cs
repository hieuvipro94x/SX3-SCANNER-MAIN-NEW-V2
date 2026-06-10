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

                BtnSTART_CONTENT = value ? "STOP" : "START";

                CanChangeProductInfo = !value;

                AppConfigHelper.Modify(AppConfigStringKey.Injob, value ? "1" : "0");

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
            get { return !_isScanBusy; }
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
                        o => InJob && !string.IsNullOrWhiteSpace(InputScanCode),
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
                        o => HasOpenScanSession,
                        o => CancelCurrentBox());
                }

                return _CancelBoxCMD;
            }
        }

        private ICommand _ClosePartialBoxCMD;

        public ICommand ClosePartialBoxCMD
        {
            get
            {
                if (_ClosePartialBoxCMD == null)
                {
                    _ClosePartialBoxCMD = new RelayCommand<object>(
                        o => CurrentScanProgress > 0,
                        async o => await ClosePartialBoxAsync());
                }

                return _ClosePartialBoxCMD;
            }
        }

        private ScanHistory _CurrentScanHistory;

        private string _CurrentBoxName;

        private string _ScanMess;

        private async Task ScanDataAsync(string inputScanCode)
        {
            await _scanWriteLock.WaitAsync();
            _isScanBusy = true;
            OnPropertyChanged(nameof(CanSwitchProduct));
            try
            {
            inputScanCode = inputScanCode?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(inputScanCode))
                return;

            if (ScanHistorySource == null)
                ScanHistorySource = new ObservableCollection<ScanHistory>();

            bool allocatedBoxName = string.IsNullOrWhiteSpace(_CurrentBoxName);
            if (string.IsNullOrWhiteSpace(_CurrentBoxName))
                _CurrentBoxName = await Task.Run(
                    () => _boxProductRepository.GetNextBoxName(SelectedDate));

            ResetScanStatus();

            _CurrentScanHistory = new ScanHistory
            {
                ScanTime = GetBusinessScanTime(),
                ProductPartNumber = SelectedPartNumber,
                ProductPartName = PNameExpected,
                BoxName = _CurrentBoxName,
                ScanData = inputScanCode,
                ScanWorker = Worker ?? string.Empty
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
            bool isFirstPassInBox = isPass && persistedProgress == 1;
            BoxProduct persistedBox = isFirstPassInBox
                ? new BoxProduct
                {
                    BoxName = _CurrentBoxName,
                    ProductPartName = PNameExpected,
                    ProductPartNumber = SelectedPartNumber,
                    BoxSealNo = SelectedDate.ToString("yyMMdd"),
                    BoxQuantity = SelectedQuantity,
                    BoxProgress = persistedProgress,
                    BoxComplete = false,
                    BoxWorker = string.IsNullOrWhiteSpace(Worker)
                        ? string.Empty
                        : Worker.Trim(),
                    BoxType = "OPEN",
                    IsPartialBox = false
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
            catch (Exception ex)
            {
                StartupManager.Log("Khong luu duoc lich su scan. Database path: " +
                    Model.Respository.DatabaseRepository.DatabasePath + ". Chi tiet: " + ex);

                MessageBox.Show(
                    "Không lưu được dữ liệu scan vào database.\nVui lòng kiểm tra quyền ghi database và liên hệ kỹ thuật.",
                    "LOI LUU DATABASE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                if (allocatedBoxName)
                {
                    _CurrentBoxName = null;
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
                InputScanCode = string.Empty;
            }
            else
            {
                MessageBox.Show(
                    BuildNgPopupMessage(inputScanCode),
                    "K\u1EBET QU\u1EA2 SCAN: NG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (isPass)
            {
                CurrentScanProgress = persistedProgress;
            }

            ScanHistorySource.Insert(0, _CurrentScanHistory);
            RefreshScanHistoryDisplayIndex();

            if (isPass)
            {
                ApplyPersistedBox(persistedBox, persistedProgress);
            }

            if (isPass && SelectedQuantity > 0 && CurrentScanProgress == SelectedQuantity)
            {
                await CompleteBoxAsync(false);
            }
            }
            finally
            {
                _isScanBusy = false;
                OnPropertyChanged(nameof(CanSwitchProduct));
                _scanWriteLock.Release();
            }
        }

        private async Task ClosePartialBoxAsync()
        {
            await _scanWriteLock.WaitAsync();
            try
            {
                if (CurrentScanProgress <= 0 ||
                    (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity))
                {
                    return;
                }

                MessageBoxResult result = MessageBox.Show(
                    "Th\u00F9ng ch\u01B0a \u0111\u1EE7 s\u1ED1 l\u01B0\u1EE3ng.\nB\u1EA1n c\u00F3 mu\u1ED1n x\u00E1c nh\u1EADn TH\u00D9NG L\u1EBA kh\u00F4ng?\n\nYes: X\u00C1C NH\u1EACN TH\u00D9NG L\u1EBA\nNo: TI\u1EBEP T\u1EE4C SCAN",
                    "X\u00C1C NH\u1EACN TH\u00D9NG L\u1EBA",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await CompleteBoxAsync(true);
                }
            }
            finally
            {
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
            else if (SelectedQuantity <= 0 || CurrentScanProgress != SelectedQuantity)
            {
                return;
            }

            if (!ShowWorkerCompletePopup())
                return;

            string completedBoxName = _CurrentBoxName;
            string completedProductCode = SelectedPartNumber;
            DateTime completedSessionDate = GetCurrentBoxSessionDate();
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
                            completedSessionDate,
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
                MessageBox.Show(
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

            CurrentScanProgress = 0;
            ScanHistorySource = new ObservableCollection<ScanHistory>();
            InputScanCode = string.Empty;
            _CurrentBoxName = null;
            ResetScanStatus();
            OnPropertyChanged(nameof(HasOpenScanSession));

            string message = isPartial
                ? "HO\u00C0N TH\u00C0NH TH\u00D9NG L\u1EBA"
                : "HO\u00C0N TH\u00C0NH TH\u00D9NG \u0110\u1EE6";
            MessageBox.Show(message, "TH\u00D4NG B\u00C1O", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task<bool> IsDuplicateLotAsync(string input)
        {
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
                    PNameExpected,
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

            return CheckLength(input);
        }

        private DateTime GetBusinessScanTime()
        {
            return SelectedDate.Date.Add(DateTime.Now.TimeOfDay);
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

            if (!int.TryParse(lotNo, out int _))
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

        private string BuildNgPopupMessage(string inputScanCode)
        {
            string lotNo = !string.IsNullOrWhiteSpace(_CurrentScanHistory?.LotNo)
                ? _CurrentScanHistory.LotNo
                : TryExtractLotNo(inputScanCode);

            var builder = new StringBuilder();
            builder.AppendLine("L\u1ED7i c\u1EE5 th\u1EC3: " + ScanHistory.NormalizeScanMessage(_ScanMess));
            builder.AppendLine("M\u00E3 \u0111ang scan: " + (inputScanCode ?? string.Empty));
            builder.AppendLine("PartNumber \u0111ang ch\u1ECDn: " + (SelectedPartNumber ?? string.Empty));
            builder.AppendLine("T\u00EAn s\u1EA3n ph\u1EA9m mong \u0111\u1EE3i: " + (PNameExpected ?? string.Empty));
            builder.AppendLine("SealNo/ng\u00E0y mong \u0111\u1EE3i: " + (SealNoExpected ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(lotNo))
                builder.AppendLine("LotNo \u0111\u1ECDc \u0111\u01B0\u1EE3c: " + lotNo);

            builder.AppendLine();
            builder.Append("G\u1EE3i \u00FD: Ki\u1EC3m tra l\u1EA1i tem ho\u1EB7c ch\u1ECDn \u0111\u00FAng m\u00E3 h\u00E0ng.");
            return builder.ToString();
        }

        private void StartScaning(string selectedPartNumber)
        {
            if (ScanHistorySource == null || ScanHistorySource.Count == 0)
            {
                ScanHistorySource = new ObservableCollection<ScanHistory>();
            }

            SaveCurrentScanSession(true);
        }

        private void CancelCurrentBox()
        {
            MessageBoxResult result = MessageBox.Show(
                "Bạn có chắc chắn muốn HỦY THÙNG đang scan dở không?\nToàn bộ dữ liệu hiển thị tạm thời của thùng hiện tại sẽ được xóa.",
                "XÁC NHẬN HỦY THÙNG",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            string cancelledBoxName = _CurrentBoxName;
            if (!string.IsNullOrWhiteSpace(cancelledBoxName))
            {
                _scanHistoryRepository.DeleteByBoxName(cancelledBoxName);
                _boxProductRepository.DeleteByBoxName(cancelledBoxName);

                BoxProduct cancelledBox = ToDayBoxSource?.FirstOrDefault(x => x.BoxName == cancelledBoxName);
                if (cancelledBox != null)
                {
                    ToDayBoxSource.Remove(cancelledBox);
                    ToDayBoxView?.Refresh();
                }
            }

            _scanSessionService.RemoveSession(SelectedPartNumber, GetCurrentBoxSessionDate());
            ScanHistorySource?.Clear();

            CurrentScanProgress = 0;

            InputScanCode = string.Empty;

            _CurrentBoxName = null;

            ResetScanStatus();

            ScanTextResult = string.Empty;
            InJob = false;
            OnPropertyChanged(nameof(HasOpenScanSession));

            MessageBox.Show("ĐÃ HỦY THÙNG HIỆN TẠI", "THÔNG BÁO", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    MessageBox.Show(
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
                    MessageBox.Show(
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
                SessionDate = GetCurrentBoxSessionDate()
            };
        }

        private DateTime GetCurrentBoxSessionDate()
        {
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
                    return boxDate;
                }
            }

            return SelectedDate;
        }

        private bool RestoreScanSession(string productCode)
        {
            ScanSessionState state = _scanSessionService.LoadSession(productCode, SelectedDate);
            if (state == null)
            {
                return false;
            }

            _CurrentBoxName = state.BoxCode;
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
            StartupManager.SetStatus("Đã khôi phục phiên quét mã " + productCode + ".");
            return true;
        }
    }
}
