using SX3_SCANER.Helper;
using SX3_SCANER.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SX3_SCANER
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _clockTimer = new DispatcherTimer();
        private readonly string _applicationVersion = ApplicationVersion.Current;
        private bool _startupInitializationStarted;

        public MainWindow()
        {
            InitializeComponent();
            StartupManager.EnsureStartWithWindows();
            StartupManager.StatusChanged += StartupStatus_Changed;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
            UpdateClock();
            ShowStartupStatus(StartupManager.CurrentStatus);
        }

        private void ClockTimer_Tick(object sender, EventArgs e) => UpdateClock();

        private void UpdateClock()
        {
            Title = "SX3 Scanner V" + _applicationVersion + " | " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            txtHeaderVersion.Text = "V" + _applicationVersion;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupInitializationStarted) return;
            _startupInitializationStarted = true;
            if (DataContext is MainViewModel viewModel)
                await viewModel.InitializeApplicationAsync();
        }

        private void StartupStatus_Changed(string message)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() => ShowStartupStatus(message)));
        }

        private void ShowStartupStatus(string message)
        {
            txtStartupStatus.Text = message;
            txtStartupStatus.ToolTip = message;
            Brush color = message.IndexOf("Lỗi", StringComparison.OrdinalIgnoreCase) >= 0
                ? Brushes.Firebrick
                : message.IndexOf("Sẵn sàng", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Brushes.ForestGreen : Brushes.DarkSlateGray;
            txtStartupStatus.Foreground = color;
            startupStatusDot.Fill = color;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            StartupManager.StatusChanged -= StartupStatus_Changed;
            _clockTimer.Stop();
            _clockTimer.Tick -= ClockTimer_Tick;
            if (DataContext is MainViewModel viewModel)
                viewModel.StopDailyBackupScheduler();
        }

        private void HideRowIndex_AutoGeneratingColumn(
            object sender,
            DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "RowIndex" || e.PropertyName == "ID")
            {
                e.Cancel = true;
                return;
            }

            if (e.PropertyName == "ScanTime")
                e.Column.Width = 150;

            if (e.PropertyName == "BoxName")
                e.Column.Width = 150;

            if (e.PropertyName == "ProductPartNumber")
                e.Column.Width = 150;

            if (e.PropertyName == "ProductPartName")
                e.Column.Width = 160;

            if (e.PropertyName == "SealNo")
                e.Column.Width = 100;

            if (e.PropertyName == "LotNo")
                e.Column.Width = 100;

            if (e.PropertyName == "ScanData")
                e.Column.Width = 260;

            if (e.PropertyName == "ScanMessage")
                e.Column.Width = 260;

            if (e.PropertyName == "ScanWorker")
                e.Column.Width = 120;

            if (e.PropertyName == "ResultText")
                e.Column.Width = 100;
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private void SQLiteTable_LoadingRow(
            object sender,
            DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

    }
}
