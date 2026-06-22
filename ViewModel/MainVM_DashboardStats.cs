using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private readonly ScanHistoryRepository _dashboardScanRepository = new ScanHistoryRepository();
        private readonly BoxProductRepository _dashboardBoxRepository = new BoxProductRepository();

        private ObservableCollection<ScanHistory> _dashboardScanHistorySource;
        private ObservableCollection<BoxProduct> _dashboardTodayBoxSource;
        private static readonly TimeSpan DashboardRefreshDebounce = TimeSpan.FromMilliseconds(180);
        private int _dashboardRefreshRequestId;
        private CancellationTokenSource _dashboardRefreshCancellation;

        private int _todayScanCount;
        private int _todayPassCount;
        private int _todayFailCount;
        private int _todayTotalBoxCount;
        private int _todayCompletedBoxCount;
        private int _todayFullBoxCount;
        private int _todayPartialBoxCount;
        private int _todayOpenBoxCount;
        private int _todayCancelledBoxCount;
        private string _todayYieldText = "0.0%";
        private string _dashboardDateText = DateTime.Today.ToString("dd/MM/yyyy");

        public int TodayScanCount
        {
            get { return _todayScanCount; }
            private set
            {
                if (_todayScanCount == value) return;
                _todayScanCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayPassCount
        {
            get { return _todayPassCount; }
            private set
            {
                if (_todayPassCount == value) return;
                _todayPassCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayFailCount
        {
            get { return _todayFailCount; }
            private set
            {
                if (_todayFailCount == value) return;
                _todayFailCount = value;
                OnPropertyChanged();
            }
        }

        public string TodayYieldText
        {
            get { return _todayYieldText; }
            private set
            {
                if (_todayYieldText == value) return;
                _todayYieldText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Tổng thùng trong ngày, không tính thùng đã hủy.
        /// </summary>
        public int TodayTotalBoxCount
        {
            get { return _todayTotalBoxCount; }
            private set
            {
                if (_todayTotalBoxCount == value) return;
                _todayTotalBoxCount = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Tổng thùng đã hoàn thành trong ngày = thùng đủ + thùng lẻ.
        /// </summary>
        public int TodayCompletedBoxCount
        {
            get { return _todayCompletedBoxCount; }
            private set
            {
                if (_todayCompletedBoxCount == value) return;
                _todayCompletedBoxCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayFullBoxCount
        {
            get { return _todayFullBoxCount; }
            private set
            {
                if (_todayFullBoxCount == value) return;
                _todayFullBoxCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayPartialBoxCount
        {
            get { return _todayPartialBoxCount; }
            private set
            {
                if (_todayPartialBoxCount == value) return;
                _todayPartialBoxCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayOpenBoxCount
        {
            get { return _todayOpenBoxCount; }
            private set
            {
                if (_todayOpenBoxCount == value) return;
                _todayOpenBoxCount = value;
                OnPropertyChanged();
            }
        }

        public int TodayCancelledBoxCount
        {
            get { return _todayCancelledBoxCount; }
            private set
            {
                if (_todayCancelledBoxCount == value) return;
                _todayCancelledBoxCount = value;
                OnPropertyChanged();
            }
        }

        public string DashboardDateText
        {
            get { return _dashboardDateText; }
            private set
            {
                if (_dashboardDateText == value) return;
                _dashboardDateText = value;
                OnPropertyChanged();
            }
        }

        private ICommand _RefreshDashboardStatsCMD;
        public ICommand RefreshDashboardStatsCMD
        {
            get
            {
                if (_RefreshDashboardStatsCMD == null)
                {
                    _RefreshDashboardStatsCMD = new RelayCommand<object>(
                        o => true,
                        o => RefreshDashboardStats());
                }
                return _RefreshDashboardStatsCMD;
            }
        }

        private DateTime GetDashboardBusinessDate()
        {
            // Dashboard thống kê theo NGÀY BOX, không theo NGÀY TEM.
            // Lý do: một thùng có thể mở hôm nay nhưng sang ngày hôm sau mới scan đủ.
            // Khi đổi NGÀY TEM để kiểm tem mới, các tem trong cùng thùng vẫn phải
            // được cộng vào tổng scan của NGÀY BOX / phiên thùng hiện tại.
            DateTime boxDate = BoxDate.Date;
            if (boxDate != DateTime.MinValue)
                return boxDate;

            if (SelectedDate == DateTime.MinValue)
                return DateTime.Today;

            return SelectedDate.Date;
        }

        private void SubscribeDashboardScanHistory(ObservableCollection<ScanHistory> source)
        {
            if (_dashboardScanHistorySource != null)
            {
                _dashboardScanHistorySource.CollectionChanged -= DashboardScanHistory_CollectionChanged;
            }

            _dashboardScanHistorySource = source;

            _currentPassCount = source == null ? 0 : source.Count(x => x.ScanResult);
            _currentNgCount = source == null ? 0 : source.Count - _currentPassCount;

            if (_dashboardScanHistorySource != null)
            {
                _dashboardScanHistorySource.CollectionChanged += DashboardScanHistory_CollectionChanged;
            }

            RefreshDashboardStats();
        }

        private void SubscribeDashboardTodayBox(ObservableCollection<BoxProduct> source)
        {
            if (_dashboardTodayBoxSource != null)
            {
                _dashboardTodayBoxSource.CollectionChanged -= DashboardTodayBox_CollectionChanged;
            }

            _dashboardTodayBoxSource = source;

            if (_dashboardTodayBoxSource != null)
            {
                _dashboardTodayBoxSource.CollectionChanged += DashboardTodayBox_CollectionChanged;
            }

            RefreshDashboardStats();
        }

        private void DashboardScanHistory_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _currentPassCount = _dashboardScanHistorySource == null
                    ? 0
                    : _dashboardScanHistorySource.Count(x => x.ScanResult);
                _currentNgCount = _dashboardScanHistorySource == null
                    ? 0
                    : _dashboardScanHistorySource.Count - _currentPassCount;
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (ScanHistory item in e.OldItems)
                    {
                        if (item.ScanResult) _currentPassCount--;
                        else _currentNgCount--;
                    }
                }

                if (e.NewItems != null)
                {
                    foreach (ScanHistory item in e.NewItems)
                    {
                        if (item.ScanResult) _currentPassCount++;
                        else _currentNgCount++;
                    }
                }
            }

            OnPropertyChanged(nameof(CurrentPassCount));
            OnPropertyChanged(nameof(CurrentNgCount));
            OnPropertyChanged(nameof(CurrentProgressPercentText));
            RefreshDashboardStats();
        }

        private void DashboardTodayBox_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshDashboardStats();
        }

        /// <summary>
        /// Dashboard dùng dữ liệu tổng hợp từ database theo ngày đang chọn.
        /// Không query trực tiếp trên UI thread để tránh đơ giao diện khi scan nhanh.
        /// Nhiều lần gọi liên tiếp sẽ được gom lại, chỉ lần mới nhất được áp dụng.
        /// </summary>
        private async void RefreshDashboardStats()
        {
            DateTime businessDate = GetDashboardBusinessDate();
            DashboardDateText = businessDate.ToString("dd/MM/yyyy");

            int requestId = Interlocked.Increment(ref _dashboardRefreshRequestId);
            var cancellation = new CancellationTokenSource();
            CancellationTokenSource previous = Interlocked.Exchange(
                ref _dashboardRefreshCancellation,
                cancellation);
            previous?.Cancel();

            try
            {
                await Task.Delay(DashboardRefreshDebounce, cancellation.Token);

                if (requestId != _dashboardRefreshRequestId)
                    return;

                DashboardStatsSnapshot snapshot = await Task.Run(() =>
                    new DashboardStatsSnapshot
                    {
                        BusinessDate = businessDate,
                        ScanStats = _dashboardScanRepository.GetDashboardScanStats(businessDate),
                        BoxStats = _dashboardBoxRepository.GetDashboardBoxStats(businessDate)
                    }, cancellation.Token);

                if (requestId != _dashboardRefreshRequestId || cancellation.IsCancellationRequested)
                    return;

                ApplyDashboardStats(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Yêu cầu mới hơn đã thay thế lần refresh này.
            }
            catch (Exception ex)
            {
                // Dashboard là phần hiển thị phụ, không được làm gián đoạn thao tác scan.
                StartupManager.Log("Refresh dashboard failed: " + ex);
            }
            finally
            {
                if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _dashboardRefreshCancellation,
                        null,
                        cancellation),
                    cancellation))
                {
                    cancellation.Dispose();
                }
            }
        }

        private void ApplyDashboardStats(DashboardStatsSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            DashboardDateText = snapshot.BusinessDate.ToString("dd/MM/yyyy");

            DashboardScanStats scanStats = snapshot.ScanStats;
            DashboardBoxStats boxStats = snapshot.BoxStats;

            // Tổng scan trong ngày = PASS + NG.
            // Không dùng trực tiếp scanStats.Total để tránh lệch nếu database có thêm loại bản ghi khác.
            int totalScan = Math.Max(0, scanStats.Pass) + Math.Max(0, scanStats.Fail);

            TodayScanCount = totalScan;
            TodayPassCount = scanStats.Pass;
            TodayFailCount = scanStats.Fail;
            TodayYieldText = totalScan <= 0
                ? "0%"
                : ((double)scanStats.Pass / totalScan * 100.0).ToString("0") + "%";

            TodayTotalBoxCount = boxStats.Total;
            TodayCompletedBoxCount = boxStats.Completed;
            TodayFullBoxCount = boxStats.Full;
            TodayPartialBoxCount = boxStats.Partial;
            TodayOpenBoxCount = boxStats.Open;
            TodayCancelledBoxCount = boxStats.Cancelled;
        }

        private sealed class DashboardStatsSnapshot
        {
            public DateTime BusinessDate { get; set; }
            public DashboardScanStats ScanStats { get; set; }
            public DashboardBoxStats BoxStats { get; set; }
        }
    }
}
