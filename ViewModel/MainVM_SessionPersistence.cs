using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using SX3_SCANER.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private void ScanHistorySource_CollectionChanged(
            object sender,
            NotifyCollectionChangedEventArgs e)
        {
            RefreshScanHistoryDisplayIndex();
        }

        private void RefreshScanHistoryDisplayIndex()
        {
            if (ScanHistorySource == null) return;

            for (int i = 0; i < ScanHistorySource.Count; i++)
            {
                ScanHistorySource[i].RowIndex = i + 1;
            }
        }

        private void SaveCurrentScanSession(bool isInJob)
        {
            if (string.IsNullOrWhiteSpace(SelectedPartNumber) || !HasOpenScanSession)
            {
                return;
            }

            _scanSessionService.SaveCurrentSession(BuildCurrentScanSession(
                isInJob,
                CurrentScanProgress));
        }

        public bool TrySaveScanSessionBeforeSoftwareUpdate(out string message)
        {
            message = string.Empty;

            if (_isScanBusy)
            {
                message =
                    "Ứng dụng đang xử lý mã scan. Vui lòng chờ xử lý xong rồi bấm cập nhật lại.";
                return false;
            }

            if (!HasOpenScanSession)
            {
                InputScanCode = string.Empty;
                return true;
            }

            SaveCurrentScanSession(InJob);
            InJob = false;
            InputScanCode = string.Empty;
            ResetScanStatus();
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            CommandManager.InvalidateRequerySuggested();

            StartupManager.SetStatus(
                "Đã lưu phiên scan đang dở trước khi cập nhật phần mềm.");
            return true;
        }

        private ScanSessionState BuildCurrentScanSession(
            bool isInJob,
            int scannedCount)
        {
            return new ScanSessionState
            {
                ProductCode = SelectedPartNumber,
                BoxCode = _CurrentBoxName ?? string.Empty,
                ScannedCount = scannedCount,
                TargetCount = SelectedQuantity,
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

            ScanHistorySource = string.IsNullOrWhiteSpace(state.BoxCode)
                ? new ObservableCollection<ScanHistory>()
                : _scanHistoryRepository.GetByBoxName(state.BoxCode);
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
