using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Views;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private string _CurrentBoxHistoryFilter;
        private ICommand _OpenSessionSetupCMD;
        private int _currentPassCount;
        private int _currentNgCount;

        public ICommand OpenSessionSetupCMD
        {
            get
            {
                if (_OpenSessionSetupCMD == null)
                {
                    _OpenSessionSetupCMD = new RelayCommand<object>(
                        o => IsApplicationReady && !_isScanBusy,
                        o =>
                        {
                            if (!_isScanBusy)
                                OpenSessionSetupFromMainWindow();
                        });
                }

                return _OpenSessionSetupCMD;
            }
        }

        public string CurrentBoxNameText
        {
            get
            {
                return string.IsNullOrWhiteSpace(_CurrentBoxName)
                    ? "Chưa mở thùng"
                    : _CurrentBoxName;
            }
        }

        public string CurrentSessionProductText
        {
            get
            {
                return string.IsNullOrWhiteSpace(SelectedPartNumber)
                    ? "Chưa chọn mã hàng"
                    : SelectedPartNumber;
            }
        }

        public string CurrentSessionDateText
        {
            get { return BoxDate.ToString("dd/MM/yyyy"); }
        }

        public int CurrentPassCount
        {
            get { return _currentPassCount; }
        }

        public int CurrentNgCount
        {
            get { return _currentNgCount; }
        }

        public string CurrentProgressPercentText
        {
            get
            {
                if (SelectedQuantity <= 0)
                    return "0%";

                double percent = Math.Min(100.0, Math.Max(0.0, CurrentScanProgress * 100.0 / SelectedQuantity));
                return percent.ToString("0") + "%";
            }
        }

        public string CurrentBoxHistoryFilter
        {
            get { return _CurrentBoxHistoryFilter; }
            set
            {
                if (_CurrentBoxHistoryFilter == value)
                    return;

                _CurrentBoxHistoryFilter = value;
                OnPropertyChanged();
                ApplyCurrentBoxHistoryFilter();
            }
        }

        public bool BeginStartupScanSession()
        {
            if (string.IsNullOrWhiteSpace(SelectedPartNumber))
            {
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Vui lòng chọn mã hàng trước khi bắt đầu quét.",
                    "THIẾU MÃ HÀNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (!IsApplicationReady)
            {
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Database chưa sẵn sàng. Vui lòng chờ app kiểm tra xong rồi bắt đầu quét.",
                    "DATABASE CHƯA SẴN SÀNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (SelectedQuantity <= 0)
            {
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Số lượng/thùng phải lớn hơn 0.",
                    "THIẾU SỐ LƯỢNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (HasOpenScanSession)
            {
                MessageBoxResult resumeResult = SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Mã hàng/ngày này đang có thùng chưa kết thúc.\n\nBạn muốn mở lại phiên quét này để tiếp tục không?",
                    "MỞ LẠI PHIÊN QUÉT",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (resumeResult != MessageBoxResult.Yes)
                    return false;
            }
            else
            {
                ScanLabelDate = BoxDate;
            }

            TabControlSelectedIndex = (int)JobIndex.SCAN;
            InJob = true;
            StartScaning(SelectedPartNumber);

            StartupManager.SetStatus(
                "Đang quét mã " + SelectedPartNumber +
                " | Ngày scan " + BoxDate.ToString("dd/MM/yyyy") + ".");

            NotifySessionHeaderChanged();
            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void OpenSessionSetupFromMainWindow()
        {
            if (HasOpenScanSession)
            {
                MessageBoxResult result = SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Thùng hiện tại đang có dữ liệu scan.\n\nNếu đổi phiên quét, app sẽ tạm dừng và lưu phiên hiện tại để tránh mất dữ liệu.\n\nBạn có chắc muốn đổi không?",
                    "XÁC NHẬN ĐỔI PHIÊN QUÉT",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                SaveCurrentScanSession(InJob);
                InJob = false;
            }

            Window owner = Application.Current == null ? null : Application.Current.MainWindow;
            StartupScanSessionWindow dialog = new StartupScanSessionWindow
            {
                DataContext = this,
                WindowStartupLocation = owner != null && owner.IsVisible
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };

            if (owner != null && owner.IsVisible)
                dialog.Owner = owner;

            bool? selected = dialog.ShowDialog();
            if (selected == true)
            {
                NotifySessionHeaderChanged();
                StartupManager.SetStatus("Đã đổi sang phiên quét mã " + SelectedPartNumber + ".");
            }
        }


        public void StartDefaultScanSessionOnLaunch()
        {
            TabControlSelectedIndex = (int)JobIndex.SCAN;

            if (string.IsNullOrWhiteSpace(SelectedPartNumber))
            {
                StartupManager.SetStatus("Vui lòng chọn mã hàng để bắt đầu quét.");
                NotifySessionHeaderChanged();
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            if (SelectedQuantity <= 0)
            {
                StartupManager.SetStatus("Mã " + SelectedPartNumber + " chưa có số lượng/thùng hợp lệ.");
                NotifySessionHeaderChanged();
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            ScanLabelDate = BoxDate;
            InJob = true;
            StartScaning(SelectedPartNumber);

            StartupManager.SetStatus(
                "Sẵn sàng quét mã " + SelectedPartNumber +
                " | Ngày box " + BoxDate.ToString("dd/MM/yyyy") + ".");

            NotifySessionHeaderChanged();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ApplyCurrentBoxHistoryFilter()
        {
            if (ScanHistoryView == null)
                return;

            string keyword = (_CurrentBoxHistoryFilter ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                ScanHistoryView.Filter = null;
            }
            else
            {
                ScanHistoryView.Filter = item => IsCurrentBoxHistoryMatched(item as ScanHistory, keyword);
            }

            ScanHistoryView.Refresh();
        }

        private static bool IsCurrentBoxHistoryMatched(ScanHistory row, string keyword)
        {
            if (row == null)
                return false;

            return ContainsIgnoreCase(row.BoxName, keyword) ||
                ContainsIgnoreCase(row.ProductPartNumber, keyword) ||
                ContainsIgnoreCase(row.ProductPartName, keyword) ||
                ContainsIgnoreCase(row.SealNo, keyword) ||
                ContainsIgnoreCase(row.LotNo, keyword) ||
                ContainsIgnoreCase(row.ScanData, keyword) ||
                ContainsIgnoreCase(row.ResultText, keyword) ||
                ContainsIgnoreCase(row.ShortScanMessage, keyword) ||
                ContainsIgnoreCase(row.ScanWorker, keyword);
        }

        private void NotifySessionHeaderChanged()
        {
            OnPropertyChanged(nameof(CurrentBoxNameText));
            OnPropertyChanged(nameof(CurrentSessionProductText));
            OnPropertyChanged(nameof(CurrentSessionDateText));
            OnPropertyChanged(nameof(CurrentPassCount));
            OnPropertyChanged(nameof(CurrentNgCount));
            OnPropertyChanged(nameof(CurrentProgressPercentText));
            OnPropertyChanged(nameof(CurrentBoxHistoryFilter));
        }
    }
}
