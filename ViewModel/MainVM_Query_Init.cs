using Microsoft.Win32;
using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private const int DefaultQueryResultLimit = 500;
        private const int ExportHistoryResultLimit = 50000;
        private int _historyQueryVersion;
        private CancellationTokenSource _historySearchCts;

        private readonly List<string> _scanResultFilterOptions =
            new List<string> { "All", "PASS", "NG" };

        private readonly List<int> _queryLimitOptions =
            new List<int> { 100, 200, 500, 1000, 2000 };

        public List<string> ScanResultFilterOptions
        {
            get { return _scanResultFilterOptions; }
        }

        public List<int> QueryLimitOptions
        {
            get { return _queryLimitOptions; }
        }

        private int _selectedQueryLimit = DefaultQueryResultLimit;

        public int SelectedQueryLimit
        {
            get { return _selectedQueryLimit; }
            set
            {
                int next = Math.Max(100, Math.Min(value, 2000));
                if (_selectedQueryLimit == next) return;
                _selectedQueryLimit = next;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private DateTime? _historyFromDate = DateTime.Today;

        public DateTime? HistoryFromDate
        {
            get { return _historyFromDate; }
            set
            {
                if (_historyFromDate == value) return;
                _historyFromDate = value.HasValue ? value.Value.Date : (DateTime?)null;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private DateTime? _historyToDate = DateTime.Today;

        public DateTime? HistoryToDate
        {
            get { return _historyToDate; }
            set
            {
                if (_historyToDate == value) return;
                _historyToDate = value.HasValue ? value.Value.Date : (DateTime?)null;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private List<string> _historyDataSources =
            new List<string> { HistoryDataRepository.ScanHistorySource };

        public List<string> HistoryDataSources
        {
            get { return _historyDataSources; }
            set
            {
                _historyDataSources = value ?? new List<string>();
                OnPropertyChanged();
                OnPropertyChanged("SQLiteTableList");
            }
        }

        public List<string> SQLiteTableList
        {
            get { return HistoryDataSources; }
        }

        private string _selectedHistoryDataSource =
            HistoryDataRepository.ScanHistorySource;

        public string SelectedHistoryDataSource
        {
            get { return _selectedHistoryDataSource; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value)
                    ? HistoryDataRepository.ScanHistorySource
                    : value;

                if (_selectedHistoryDataSource == next)
                {
                    return;
                }

                _selectedHistoryDataSource = next;
                OnPropertyChanged();
                OnPropertyChanged("SelectedSQLiteTable");
                DebounceHistorySearchAsync();
            }
        }

        public string SelectedSQLiteTable
        {
            get { return SelectedHistoryDataSource; }
            set { SelectedHistoryDataSource = value; }
        }

        private ObservableCollection<HistoryDataRow> _historyResults =
            new ObservableCollection<HistoryDataRow>();

        public ObservableCollection<HistoryDataRow> HistoryResults
        {
            get { return _historyResults; }
            set
            {
                _historyResults =
                    value ?? new ObservableCollection<HistoryDataRow>();
                OnPropertyChanged();
            }
        }

        private bool _isQuerying;

        public bool IsQuerying
        {
            get { return _isQuerying; }
            set
            {
                if (_isQuerying == value)
                {
                    return;
                }

                _isQuerying = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _queryStatus = "Chưa tải dữ liệu lịch sử scan.";

        public string QueryStatus
        {
            get { return _queryStatus; }
            set { _queryStatus = value; OnPropertyChanged(); }
        }

        private string _queryTimes = "0";

        public string QueryTimes
        {
            get { return _queryTimes; }
            set { _queryTimes = value; OnPropertyChanged(); }
        }

        private int _rowCounts;

        public int RowCounts
        {
            get { return _rowCounts; }
            set { _rowCounts = value; OnPropertyChanged(); }
        }

        private string _historySearchKeyword = string.Empty;

        public string HistorySearchKeyword
        {
            get { return _historySearchKeyword; }
            set
            {
                if (_historySearchKeyword == value)
                {
                    return;
                }

                _historySearchKeyword = value ?? string.Empty;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private List<string> _scanViewDistinctSealNoList = new List<string> { "All" };

        public List<string> ScanViewDistinctSealNoList
        {
            get { return _scanViewDistinctSealNoList; }
            set { _scanViewDistinctSealNoList = value; OnPropertyChanged(); }
        }

        private string _selectedDistinctSealNo = "All";

        public string SelectedDistinctSealNo
        {
            get { return _selectedDistinctSealNo; }
            set
            {
                string next = NormalizeFilter(value);
                if (_selectedDistinctSealNo == next)
                {
                    return;
                }

                _selectedDistinctSealNo = next;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private List<string> _scanViewDistinctProductNumberList =
            new List<string> { "All" };

        public List<string> ScanViewDistinctProductNumberList
        {
            get { return _scanViewDistinctProductNumberList; }
            set { _scanViewDistinctProductNumberList = value; OnPropertyChanged(); }
        }

        private string _selectedDistinctProductNumber = "All";

        public string SelectedDistinctProductNumber
        {
            get { return _selectedDistinctProductNumber; }
            set
            {
                string next = NormalizeFilter(value);
                if (_selectedDistinctProductNumber == next)
                {
                    return;
                }

                _selectedDistinctProductNumber = next;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private List<string> _scanNGMessageList = new List<string> { "All" };

        public List<string> ScanNGMessageList
        {
            get { return _scanNGMessageList; }
            set { _scanNGMessageList = value; OnPropertyChanged(); }
        }

        private string _selectedScanNGMessage = "All";

        public string SelectedScanNGMessage
        {
            get { return _selectedScanNGMessage; }
            set
            {
                string next = NormalizeFilter(value);
                if (_selectedScanNGMessage == next)
                {
                    return;
                }

                _selectedScanNGMessage = next;
                OnPropertyChanged();
                DebounceHistorySearchAsync();
            }
        }

        private string _selectedScanResultFilter = "All";

        public string SelectedScanResultFilter
        {
            get { return _selectedScanResultFilter; }
            set
            {
                string next = NormalizeResultFilter(value);
                if (_selectedScanResultFilter == next)
                {
                    return;
                }

                _selectedScanResultFilter = next;
                OnPropertyChanged();
                OnPropertyChanged("OnlyPass");
                DebounceHistorySearchAsync();
            }
        }

        public bool OnlyPass
        {
            get { return SelectedScanResultFilter == "PASS"; }
            set { SelectedScanResultFilter = value ? "PASS" : "All"; }
        }

        private ICommand _queryCMD;
        private ICommand _exportHistoryCMD;

        public ICommand QueryCMD
        {
            get
            {
                if (_queryCMD == null)
                {
                    _queryCMD = new RelayCommand<object>(
                        parameter => !IsQuerying,
                        async parameter => await QueryDataAsync(CancellationToken.None));
                }

                return _queryCMD;
            }
        }

        public ICommand ExportHistoryCMD
        {
            get
            {
                if (_exportHistoryCMD == null)
                {
                    _exportHistoryCMD = new RelayCommand<object>(
                        parameter => !IsQuerying,
                        parameter => ExportHistoryToExcelAsync());
                }

                return _exportHistoryCMD;
            }
        }

        internal async void LoadQueryLookupsAsync()
        {
            ResetHistoryFilters();
            QueryStatus = "Đang tải bộ lọc và dữ liệu lịch sử scan trong ngày...";

            try
            {
                var lookups = await Task.Run(() =>
                {
                    var repository = new ScanHistoryRepository();
                    var dataRepository = new HistoryDataRepository();
                    return new
                    {
                        Sources = dataRepository.GetAvailableSources(),
                        SealNos = repository.GetDistinctSealNos(),
                        ProductNumbers = repository.GetDistinctProductNumbers(),
                        Messages = repository.GetDistinctNGMessage()
                    };
                });

                HistoryDataSources = lookups.Sources;
                _selectedHistoryDataSource =
                    lookups.Sources.Contains(
                        HistoryDataRepository.ScanHistorySource)
                        ? HistoryDataRepository.ScanHistorySource
                        : lookups.Sources.FirstOrDefault() ??
                          HistoryDataRepository.ScanHistorySource;
                OnPropertyChanged("SelectedHistoryDataSource");
                OnPropertyChanged("SelectedSQLiteTable");
                ScanViewDistinctSealNoList = lookups.SealNos;
                ScanViewDistinctProductNumberList = lookups.ProductNumbers;
                ScanNGMessageList = lookups.Messages;

                await QueryDataAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Load history lookups failed: " + ex);
                HistoryResults = new ObservableCollection<HistoryDataRow>();
                RowCounts = 0;
                QueryStatus = "Không tải được lịch sử scan: " + ex.Message;
            }
        }

        private void ResetHistoryFilters()
        {
            CancelPendingHistorySearch();
            _historySearchKeyword = string.Empty;
            _selectedDistinctSealNo = "All";
            _selectedDistinctProductNumber = "All";
            _selectedScanNGMessage = "All";
            _selectedScanResultFilter = "All";
            _historyFromDate = DateTime.Today;
            _historyToDate = DateTime.Today;
            _selectedQueryLimit = DefaultQueryResultLimit;

            OnPropertyChanged("HistorySearchKeyword");
            OnPropertyChanged("SelectedDistinctSealNo");
            OnPropertyChanged("SelectedDistinctProductNumber");
            OnPropertyChanged("SelectedScanNGMessage");
            OnPropertyChanged("SelectedScanResultFilter");
            OnPropertyChanged("OnlyPass");
            OnPropertyChanged("HistoryFromDate");
            OnPropertyChanged("HistoryToDate");
            OnPropertyChanged("SelectedQueryLimit");
        }

        private async void DebounceHistorySearchAsync()
        {
            CancelPendingHistorySearch();
            _historySearchCts = new CancellationTokenSource();
            CancellationToken token = _historySearchCts.Token;

            try
            {
                await Task.Delay(300, token);
                await QueryDataAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void CancelPendingHistorySearch()
        {
            if (_historySearchCts == null)
            {
                return;
            }

            _historySearchCts.Cancel();
            _historySearchCts.Dispose();
            _historySearchCts = null;
        }

        private async Task QueryDataAsync(CancellationToken token)
        {
            int queryVersion = Interlocked.Increment(ref _historyQueryVersion);
            Stopwatch stopwatch = Stopwatch.StartNew();
            IsQuerying = true;
            QueryStatus = "Đang tìm kiếm lịch sử scan theo ngày...";

            try
            {
                DateTime? fromDate = HistoryFromDate;
                DateTime? toDate = HistoryToDate;
                NormalizeDateRange(ref fromDate, ref toDate);

                bool? resultFilter = GetScanResultFilter();
                string keyword = HistorySearchKeyword;
                string partNumber = SelectedDistinctProductNumber;
                string sealNo = SelectedDistinctSealNo;
                string message = SelectedScanNGMessage;
                string source = SelectedHistoryDataSource;
                int limit = SelectedQueryLimit;

                ObservableCollection<HistoryDataRow> results = await Task.Run(
                    () => new HistoryDataRepository().Search(
                        source,
                        keyword,
                        partNumber,
                        sealNo,
                        message,
                        resultFilter,
                        fromDate,
                        toDate,
                        limit),
                    token);

                token.ThrowIfCancellationRequested();
                if (queryVersion != _historyQueryVersion)
                {
                    return;
                }

                ObservableCollection<HistoryDataRow> newestFirstResults =
                    SortHistoryNewestFirst(results);

                HistoryResults = newestFirstResults;
                RowCounts = newestFirstResults.Count;
                stopwatch.Stop();
                QueryTimes = stopwatch.Elapsed.TotalMilliseconds.ToString("0");
                QueryStatus = newestFirstResults.Count == 0
                    ? "Không có dữ liệu lịch sử scan phù hợp."
                    : "Hiển thị " + newestFirstResults.Count + " dòng mới nhất theo bộ lọc ngày.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Search history failed: " + ex);
                HistoryResults = new ObservableCollection<HistoryDataRow>();
                RowCounts = 0;
                stopwatch.Stop();
                QueryTimes = stopwatch.Elapsed.TotalMilliseconds.ToString("0");
                QueryStatus = "Lỗi tìm kiếm lịch sử scan: " + ex.Message;
            }
            finally
            {
                if (queryVersion == _historyQueryVersion)
                {
                    IsQuerying = false;
                }
            }
        }

        private async void ExportHistoryToExcelAsync()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Xuất lịch sử scan ra Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = "SX3_ScanHistory_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            IsQuerying = true;
            QueryStatus = "Đang xuất Excel lịch sử scan...";

            try
            {
                DateTime? fromDate = HistoryFromDate;
                DateTime? toDate = HistoryToDate;
                NormalizeDateRange(ref fromDate, ref toDate);

                bool? resultFilter = GetScanResultFilter();
                string keyword = HistorySearchKeyword;
                string partNumber = SelectedDistinctProductNumber;
                string sealNo = SelectedDistinctSealNo;
                string message = SelectedScanNGMessage;
                string source = SelectedHistoryDataSource;

                ObservableCollection<HistoryDataRow> results = await Task.Run(() =>
                    new HistoryDataRepository().Search(
                        source,
                        keyword,
                        partNumber,
                        sealNo,
                        message,
                        resultFilter,
                        fromDate,
                        toDate,
                        ExportHistoryResultLimit));

                ObservableCollection<HistoryDataRow> sorted = SortHistoryNewestFirst(results);
                if (sorted.Count == 0)
                {
                    QueryStatus = "Không có dữ liệu để xuất Excel.";
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Không có dữ liệu lịch sử scan theo bộ lọc hiện tại.",
                        "Xuất Excel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await Task.Run(() => XlsxExportService.ExportHistory(dialog.FileName, sorted));

                stopwatch.Stop();
                QueryTimes = stopwatch.Elapsed.TotalMilliseconds.ToString("0");
                QueryStatus = "Đã xuất " + sorted.Count + " dòng ra Excel: " + Path.GetFileName(dialog.FileName);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Đã xuất Excel thành công:\n" + dialog.FileName,
                    "Xuất lịch sử scan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Export history failed: " + ex);
                QueryStatus = "Xuất Excel lỗi: " + ex.Message;
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Xuất Excel lỗi: " + ex.Message,
                    "Xuất lịch sử scan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsQuerying = false;
            }
        }

        private static void NormalizeDateRange(ref DateTime? fromDate, ref DateTime? toDate)
        {
            if (fromDate.HasValue) fromDate = fromDate.Value.Date;
            if (toDate.HasValue) toDate = toDate.Value.Date;

            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                DateTime temp = fromDate.Value;
                fromDate = toDate.Value;
                toDate = temp;
            }
        }

        private static ObservableCollection<HistoryDataRow> SortHistoryNewestFirst(
            IEnumerable<HistoryDataRow> rows)
        {
            List<HistoryDataRow> sortedRows =
                (rows ?? Enumerable.Empty<HistoryDataRow>())
                .OrderByDescending(row => row.ScanTime ?? DateTime.MinValue)
                .ThenByDescending(row => row.SortSequence > 0 ? row.SortSequence : row.ID)
                .ThenByDescending(row => row.ID)
                .ToList();

            for (int index = 0; index < sortedRows.Count; index++)
            {
                sortedRows[index].RowIndex = index + 1;
            }

            return new ObservableCollection<HistoryDataRow>(sortedRows);
        }

        private bool? GetScanResultFilter()
        {
            if (SelectedScanResultFilter == "PASS")
            {
                return true;
            }

            if (SelectedScanResultFilter == "NG")
            {
                return false;
            }

            return null;
        }

        private static string NormalizeFilter(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "All" : value.Trim();
        }

        private static string NormalizeResultFilter(string value)
        {
            string normalized = NormalizeFilter(value).ToUpperInvariant();
            return normalized == "PASS" || normalized == "NG"
                ? normalized
                : "All";
        }
    }
}
