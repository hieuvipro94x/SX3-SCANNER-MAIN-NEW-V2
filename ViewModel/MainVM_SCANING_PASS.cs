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
            set { _ResultBackgroundBrush = value; OnPropertyChanged(); }
        }

        private Brush _ResultForegroundBrush = Brushes.Gray;

        public Brush ResultForegroundBrush
        {
            get { return _ResultForegroundBrush; }
            set { _ResultForegroundBrush = value; OnPropertyChanged(); }
        }

        private Color _ResultShadowColor = Colors.Transparent;

        public Color ResultShadowColor
        {
            get { return _ResultShadowColor; }
            set { _ResultShadowColor = value; OnPropertyChanged(); }
        }

        private string _ScanResultDetailText = string.Empty;

        public string ScanResultDetailText
        {
            get { return _ScanResultDetailText; }
            set { _ScanResultDetailText = value; OnPropertyChanged(); }
        }

        private void UpdateResultVisualState(string result)
        {
            if (result == OK)
            {
                ResultBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)); // #16A34A
                ResultForegroundBrush = Brushes.White;
                ResultShadowColor = Color.FromRgb(0x05, 0x46, 0x20);
                ScanResultDetailText = "Tem hợp lệ";
            }
            else if (result == NG)
            {
                ResultBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)); // #DC2626
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
            if (persistedBox == null)
                return;

            if (ToDayBoxSource == null)
                ToDayBoxSource = new ObservableCollection<BoxProduct>();

            var existing = ToDayBoxSource
                .FirstOrDefault(x => x.BoxName == persistedBox.BoxName);

            if (existing == null)
            {
                ToDayBoxSource.Add(persistedBox);
                SelectedTodayBox = persistedBox;
            }
            else
            {
                existing.BoxProgress = persistedBox.BoxProgress;
                existing.BoxComplete = persistedBox.BoxComplete;
                SelectedTodayBox = existing;
            }

            ToDayBoxView?.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
