using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.View;
using SX3_SCANER.ViewModel.TodayBoxVM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {

        private List<string> _PartNumberList;

        public List<string> PartNumberList
        {
            get { return _PartNumberList; }
            set
            {
                _PartNumberList = value;
                OnPropertyChanged();
            }
        }

        private void SetExpectedData(string pnumber)
        {
            LabelProductInfo info = new LabelProductInfoRepository().GetWithPartNumber(pnumber);

            if (info == null || string.IsNullOrWhiteSpace(info.PartName)) return;

            this.PrefixExpected = info.CodePrefix ?? string.Empty;
            this.PNameExpected = info.PartName;
            this.SealNoExpected = SelectedDate.ToString("yyMMdd");
            this.LotNoExpected = "####";
            this.SuffixExpected = info.CodeSuffix ?? string.Empty;
            this.CodeLengthExpected = info.CodeLength;
            FullCodeExpected = (info.CodeStringForm ?? string.Empty).Replace("yyMMdd", SealNoExpected);

        }

        private string _SelectedPartNumber;

        public string SelectedPartNumber
        {
            get { return _SelectedPartNumber; }
            set
            {
                if (string.IsNullOrWhiteSpace(value) || value == _SelectedPartNumber) return;

                string previousPartNumber = _SelectedPartNumber;
                bool wasInJob = InJob;
                if (!string.IsNullOrWhiteSpace(previousPartNumber))
                {
                    SaveCurrentScanSession(wasInJob);

                    StartupManager.SetStatus(
                        "Đã lưu phiên quét mã " + previousPartNumber +
                        " để chuyển sang mã " + value + ".");
                }

                _SelectedPartNumber = value;

                this.SelectedQuantity = new LabelProductInfoRepository().GetBoxQuantity(_SelectedPartNumber);

                SetExpectedData(value);

                if (!RestoreScanSession(value))
                {
                    CheckLastJob(value);
                }

                InJob = wasInJob;
                InputScanCode = string.Empty;
                ResetScanStatus();
                ScanTextResult = string.Empty;

                if (wasInJob)
                {
                    SaveCurrentScanSession(true);
                    StartupManager.SetStatus(
                        "Đã chuyển sang mã " + value +
                        ". Phiên quét mã " + previousPartNumber +
                        " vẫn được giữ nguyên.");
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSwitchProduct));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void CheckLastJob(string partNumber)
        {
            var notcompletebox = new BoxProductRepository().GetNotComplete(partNumber, SelectedDate);
            if (string.IsNullOrEmpty(notcompletebox))
            {
                notcompletebox = _boxProductRepository.GetLatestNotComplete(partNumber);
            }

            if (string.IsNullOrEmpty(notcompletebox))
            {
                _CurrentBoxName = null;
                _currentBoxCreatedDate = null;
                ScanHistorySource = new ObservableCollection<ScanHistory>();
                CurrentScanProgress = 0;
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                return;
            }

            _CurrentBoxName = notcompletebox;
            _currentBoxCreatedDate =
                _boxProductRepository.GetBoxCreatedDate(_CurrentBoxName);
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            NotifyCurrentBoxStatusChanged();
            ScanHistorySource = new ScanHistoryRepository().GetNotComplete(notcompletebox);
            RefreshScanHistoryDisplayIndex();
            CurrentScanProgress = ScanHistorySource?.Count(x => x.ScanResult == true) ?? 0;
            SaveCurrentScanSession(false);
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            CommandManager.InvalidateRequerySuggested();
        }

        private ICommand _OpenBoxCMD;

        public ICommand OpenBoxCMD
        {
            get
            {
                if (_OpenBoxCMD == null)
                {
                    _OpenBoxCMD = new RelayCommand<object>(ob => SelectedTodayBox != null, ob => OpenSelectedTodayBoxScanInfo());
                }
                return _OpenBoxCMD;
            }
        }

        private void OpenSelectedTodayBoxScanInfo()
        {
            if (SelectedTodayBox == null)
                return;

            new TodayBoxWD
            {
                DataContext = new TodayBoxViewModel(SelectedTodayBox.BoxName)
            }.ShowDialog();
        }

        private ICommand _SetSelectedBoxOnJobCMD;

        public ICommand SetSelectedBoxOnJobCMD
        {
            get
            {
                if (_SetSelectedBoxOnJobCMD == null)
                {
                    _SetSelectedBoxOnJobCMD = new RelayCommand<object>(
                        ob => SelectedTodayBox != null &&
                            !SelectedTodayBox.BoxComplete &&
                            !HasOpenScanSession &&
                            !_isScanBusy,
                        ob => SetBoxOnJob(SelectedTodayBox));
                }
                return _SetSelectedBoxOnJobCMD;
            }
        }

        private void SetBoxOnJob(BoxProduct selectedTodayBox)
        {
            InJob = false;
            SelectedPartNumber = selectedTodayBox.ProductPartNumber;
        }

        private DateTime _SelectedDate;

        public DateTime ScanLabelDate
        {
            get { return SelectedDate; }
            set { SelectedDate = value; }
        }

        public DateTime BoxDate
        {
            get
            {
                return _currentBoxCreatedDate.HasValue
                    ? _currentBoxCreatedDate.Value.Date
                    : SelectedDate.Date;
            }
        }

        public string BoxDateText
        {
            get { return BoxDate.ToString("dd/MM/yyyy"); }
        }

        public string ScanLabelDateText
        {
            get { return ScanLabelDate.ToString("dd/MM/yyyy"); }
        }

        public string ScanQuantityText
        {
            get { return CurrentScanProgress + " / " + SelectedQuantity; }
        }

        public string CurrentBoxStatusText
        {
            get
            {
                if (_isScanBusy) return "Đang xử lý";
                if (IsFullBoxReadyToComplete) return "Đủ SL - chờ đóng";
                if (HasOpenScanSession && InJob) return "Đang scan";
                if (HasOpenScanSession && !InJob) return "Tạm dừng";
                if (!HasOpenScanSession && InJob) return "Sẵn sàng scan";
                return "Chưa mở thùng";
            }
        }

        public Brush CurrentBoxStatusBackground
        {
            get
            {
                if (_isScanBusy) return new SolidColorBrush(Color.FromRgb(238, 242, 255)); // #EEF2FF
                if (IsFullBoxReadyToComplete) return new SolidColorBrush(Color.FromRgb(255, 251, 235)); // #FFFBEB
                if (HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(236, 253, 245)); // #ECFDF5
                if (HasOpenScanSession && !InJob) return new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9
                if (!HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(239, 246, 255)); // #EFF6FF
                return new SolidColorBrush(Color.FromRgb(248, 250, 252)); // #F8FAFC
            }
        }

        public Brush CurrentBoxStatusBorderBrush
        {
            get
            {
                if (_isScanBusy) return new SolidColorBrush(Color.FromRgb(199, 210, 254)); // #C7D2FE
                if (IsFullBoxReadyToComplete) return new SolidColorBrush(Color.FromRgb(251, 191, 36)); // #FBBF24
                if (HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(187, 247, 208)); // #BBF7D0
                if (HasOpenScanSession && !InJob) return new SolidColorBrush(Color.FromRgb(203, 213, 225)); // #CBD5E1
                if (!HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(191, 219, 254)); // #BFDBFE
                return new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0
            }
        }

        public Brush CurrentBoxStatusForeground
        {
            get
            {
                if (_isScanBusy) return new SolidColorBrush(Color.FromRgb(79, 70, 229)); // #4F46E5
                if (IsFullBoxReadyToComplete) return new SolidColorBrush(Color.FromRgb(180, 83, 9)); // #B45309
                if (HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(22, 163, 74)); // #16A34A
                if (HasOpenScanSession && !InJob) return new SolidColorBrush(Color.FromRgb(71, 85, 105)); // #475569
                if (!HasOpenScanSession && InJob) return new SolidColorBrush(Color.FromRgb(37, 99, 235)); // #2563EB
                return new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748B
            }
        }

        private void NotifyCurrentBoxStatusChanged()
        {
            OnPropertyChanged(nameof(CurrentBoxStatusText));
            OnPropertyChanged(nameof(CurrentBoxStatusBackground));
            OnPropertyChanged(nameof(CurrentBoxStatusBorderBrush));
            OnPropertyChanged(nameof(CurrentBoxStatusForeground));
        }

        public DateTime SelectedDate
        {
            get { return _SelectedDate; }
            set
            {
                if (_SelectedDate == value)
                {
                    return;
                }

                bool hasOpenBox = HasOpenScanSession;
                string old = _SelectedDate.ToString("yyMMdd");
                _SelectedDate = value;
                SealNoExpected = _SelectedDate.ToString("yyMMdd");

                if (!string.IsNullOrWhiteSpace(FullCodeExpected))
                {
                    FullCodeExpected = FullCodeExpected.Replace(
                        old,
                        SealNoExpected);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanLabelDate));
                OnPropertyChanged(nameof(ScanLabelDateText));
                OnPropertyChanged(nameof(BoxDate));
                OnPropertyChanged(nameof(BoxDateText));
                OnPropertyChanged(nameof(SealNoExpected));

                if (hasOpenBox)
                {
                    _boxProductRepository.UpdateScanLabelDate(
                        _CurrentBoxName,
                        ScanLabelDate);
                    SaveCurrentScanSession(InJob);
                    string dateChangeMessage =
                        "Đã đổi ngày tem scan sang " + ScanLabelDateText +
                        ". Thùng hiện tại vẫn giữ ngày box " + BoxDateText + ".";
                    ScanResultDetailText = dateChangeMessage;
                    StartupManager.SetStatus(dateChangeMessage);
                    RefreshDashboardStats();
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }

                // Đổi ngày khi chưa mở thùng: tải lại danh sách thùng và dashboard của ngày vừa chọn.
                ToDayBoxSource = new BoxProductRepository().GetAllTodayBox(_SelectedDate);

                if (!string.IsNullOrWhiteSpace(SelectedPartNumber) &&
                    !RestoreScanSession(SelectedPartNumber))
                {
                    CheckLastJob(SelectedPartNumber);
                }

                RefreshDashboardStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int _Length_OK = -1;

        public int Length_OK
        {
            get { return _Length_OK; }
            set { _Length_OK = value; OnPropertyChanged(); }
        }

        private int _Suffix_OK = -1;

        public int Suffix_OK
        {
            get { return _Suffix_OK; }
            set { _Suffix_OK = value; OnPropertyChanged(); }
        }

        private int _LotNo_OK = -1;

        public int LotNo_OK
        {
            get { return _LotNo_OK; }
            set { _LotNo_OK = value; OnPropertyChanged(); }
        }

        private int _SealNo_OK = -1;

        public int SealNo_OK
        {
            get { return _SealNo_OK; }
            set { _SealNo_OK = value; OnPropertyChanged(); }
        }

        private int _PName_OK = -1;

        public int PName_OK
        {
            get { return _PName_OK; }
            set { _PName_OK = value; OnPropertyChanged(); }
        }

        private int _Prefix_OK = -1;

        public int Prefix_OK
        {
            get { return _Prefix_OK; }
            set { _Prefix_OK = value; OnPropertyChanged(); }
        }



        private int _SelectedQuantity;

        public int SelectedQuantity
        {
            get { return _SelectedQuantity; }
            set
            {
                _SelectedQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanQuantityText));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _FullCodeExpected;

        public string FullCodeExpected
        {
            get { return _FullCodeExpected; }
            set { _FullCodeExpected = value; OnPropertyChanged(); }
        }


        private string _PNameExpected;

        public string PNameExpected
        {
            get { return _PNameExpected; }
            set { _PNameExpected = value; OnPropertyChanged(); }
        }

        private string _PNameScanResult;

        public string PNameScanResult
        {
            get { return _PNameScanResult; }
            set { _PNameScanResult = value; OnPropertyChanged(); }
        }

        private string _SealNoExpected;

        public string SealNoExpected
        {
            get { return _SealNoExpected; }
            set { _SealNoExpected = value; OnPropertyChanged(); }
        }

        private string _SealnoScanResult;

        public string SealnoScanResult
        {
            get { return _SealnoScanResult; }
            set { _SealnoScanResult = value; OnPropertyChanged(); }
        }

        private string _LotNoExpected;

        public string LotNoExpected
        {
            get { return _LotNoExpected; }
            set { _LotNoExpected = value; OnPropertyChanged(); }
        }

        private string _LotNoScanResult;

        public string LotNoScanResult
        {
            get { return _LotNoScanResult; }
            set { _LotNoScanResult = value; OnPropertyChanged(); }
        }

        private string _PrefixExpected;

        public string PrefixExpected
        {
            get { return _PrefixExpected; }
            set { _PrefixExpected = value; OnPropertyChanged(); }
        }
        private string _PrefixScanResut;

        public string PrefixScanResut
        {
            get { return _PrefixScanResut; }
            set { _PrefixScanResut = value; OnPropertyChanged(); }
        }
        private string _SuffixExpected;

        public string SuffixExpected
        {
            get { return _SuffixExpected; }
            set { _SuffixExpected = value; OnPropertyChanged(); }
        }
        private string _SuffixScanResult;

        public string SuffixScanResult
        {
            get { return _SuffixScanResult; }
            set { _SuffixScanResult = value; OnPropertyChanged(); }
        }

        private int _CodeLengthExpected;

        public int CodeLengthExpected
        {
            get { return _CodeLengthExpected; }
            set { _CodeLengthExpected = value; OnPropertyChanged(); }
        }

        private int _CodeLengthScanResult;

        public int CodeLengthScanResult
        {
            get { return _CodeLengthScanResult; }
            set { _CodeLengthScanResult = value; OnPropertyChanged(); }
        }





        private ObservableCollection<ScanHistory> _ScanHistorySource;

        public ObservableCollection<ScanHistory> ScanHistorySource
        {
            get { return _ScanHistorySource; }
            set
            {
                _ScanHistorySource = value;
                SubscribeDashboardScanHistory(value);
                ScanHistoryView = value == null ? null : CollectionViewSource.GetDefaultView(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private ICollectionView _ScanHistoryView;

        public ICollectionView ScanHistoryView
        {
            get { return _ScanHistoryView; }
            set { _ScanHistoryView = value; OnPropertyChanged(); }
        }


        private void InitializeScaningPropeties()
        {
            SelectedDate = DateTime.Now;
            ToDayBoxSource = new BoxProductRepository().GetAllTodayBox(SelectedDate);
            LoadInitialListsAsync();
        }

        private async void LoadInitialListsAsync()
        {
            PartNumberList = await Task.Run(() =>
                new LabelProductInfoRepository().GetAllLabelProductInfo()
                    .Select(x => x.PartNumber)
                    .ToList());
        }
    }
}
