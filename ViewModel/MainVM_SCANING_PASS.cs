using SX3_SCANER.Model;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private const string OK = "OK";
        private const string NG = "NG";
        private static readonly Brush OkResultBackgroundBrush = CreateFrozenBrush(0x16, 0xA3, 0x4A);
        private static readonly Brush NgResultBackgroundBrush = CreateFrozenBrush(0xDC, 0x26, 0x26);
        private string _ScanTextResult;

        public string ScanTextResult
        {
            get { return _ScanTextResult; }
            set
            {
                _ScanTextResult = value;
                UpdateResultVisualState(value);
                OnPropertyChanged();
            }
        }

        private Brush _ResultBackgroundBrush = Brushes.Transparent;

        public Brush ResultBackgroundBrush
        {
            get { return _ResultBackgroundBrush; }
            set
            {
                if (object.ReferenceEquals(_ResultBackgroundBrush, value)) return;
                _ResultBackgroundBrush = value;
                OnPropertyChanged();
            }
        }

        private Brush _ResultForegroundBrush = Brushes.Gray;

        public Brush ResultForegroundBrush
        {
            get { return _ResultForegroundBrush; }
            set
            {
                if (object.ReferenceEquals(_ResultForegroundBrush, value)) return;
                _ResultForegroundBrush = value;
                OnPropertyChanged();
            }
        }

        private Color _ResultShadowColor = Colors.Transparent;

        public Color ResultShadowColor
        {
            get { return _ResultShadowColor; }
            set
            {
                if (_ResultShadowColor == value) return;
                _ResultShadowColor = value;
                OnPropertyChanged();
            }
        }

        private string _ScanResultDetailText = string.Empty;

        public string ScanResultDetailText
        {
            get { return _ScanResultDetailText; }
            set
            {
                if (string.Equals(_ScanResultDetailText, value, System.StringComparison.Ordinal)) return;
                _ScanResultDetailText = value;
                OnPropertyChanged();
            }
        }

        private void UpdateResultVisualState(string result)
        {
            if (result == OK)
            {
                ResultBackgroundBrush = OkResultBackgroundBrush; // #16A34A
                ResultForegroundBrush = Brushes.White;
                ResultShadowColor = Color.FromRgb(0x05, 0x46, 0x20);
                ScanResultDetailText = "Tem hợp lệ";
            }
            else if (result == NG)
            {
                ResultBackgroundBrush = NgResultBackgroundBrush; // #DC2626
                ResultForegroundBrush = Brushes.White;
                ResultShadowColor = Color.FromRgb(0x7F, 0x1D, 0x1D);
                ScanResultDetailText = ScanHistory.ToShortScanMessage(_ScanMess);
            }
            else
            {
                ResultBackgroundBrush = Brushes.Transparent;
                ResultForegroundBrush = Brushes.Gray;
                ResultShadowColor = Colors.Transparent;
                ScanResultDetailText = string.Empty;
            }
        }

        private void ApplyPersistedBox(BoxProduct persistedBox, int persistedProgress)
        {
            string boxName = persistedBox == null ? _CurrentBoxName : persistedBox.BoxName;
            if (string.IsNullOrWhiteSpace(boxName))
                return;

            if (ToDayBoxSource == null)
                ToDayBoxSource = new ObservableCollection<BoxProduct>();

            var existing = ToDayBoxSource
                .FirstOrDefault(x => string.Equals(x.BoxName, boxName, System.StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                if (persistedBox == null)
                {
                    persistedBox = new BoxProduct
                    {
                        BoxName = boxName,
                        ProductPartName = PNameExpected,
                        ProductPartNumber = SelectedPartNumber,
                        BoxSealNo = GetCurrentBoxCreatedDate().ToString("yyMMdd"),
                        BoxQuantity = SelectedQuantity,
                        BoxComplete = false,
                        BoxWorker = string.IsNullOrWhiteSpace(Worker) ? string.Empty : Worker.Trim(),
                        BoxType = "OPEN",
                        IsPartialBox = false,
                        BoxDate = BoxDate,
                        ScanLabelDate = ScanLabelDate
                    };
                }

                persistedBox.BoxProgress = persistedProgress;
                persistedBox.ActualQty = persistedProgress;
                persistedBox.TargetQty = SelectedQuantity;
                ToDayBoxSource.Add(persistedBox);
                SelectedTodayBox = persistedBox;
            }
            else if (existing != null)
            {
                existing.BoxProgress = persistedProgress;
                existing.ActualQty = persistedProgress;
                existing.TargetQty = SelectedQuantity;
                existing.BoxQuantity = SelectedQuantity;
                existing.ScanLabelDate = ScanLabelDate;

                if (persistedBox != null)
                {
                    existing.ProductPartName = persistedBox.ProductPartName;
                    existing.ProductPartNumber = persistedBox.ProductPartNumber;
                    existing.BoxSealNo = persistedBox.BoxSealNo;
                    existing.BoxComplete = persistedBox.BoxComplete;
                    existing.BoxWorker = persistedBox.BoxWorker;
                    existing.BoxType = persistedBox.BoxType;
                    existing.IsPartialBox = persistedBox.IsPartialBox;
                    existing.BoxDate = persistedBox.BoxDate;
                }

                SelectedTodayBox = existing;
            }

            // Add() tự cập nhật CollectionView và dashboard qua CollectionChanged.
            // Chỉ refresh thủ công khi cập nhật object đã có trong collection.
            if (existing != null)
            {
                ToDayBoxView?.Refresh();
                RefreshDashboardStats();
            }
        }

        private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
