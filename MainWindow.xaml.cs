using SX3_SCANER.Helper;
using System;
using System.Configuration;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SX3_SCANER.ViewModel;

namespace SX3_SCANER
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly string applicationVersion;

        private readonly GitHubReleaseUpdateService _updateService =
            new GitHubReleaseUpdateService();
        private GitHubReleaseUpdateInfo availableUpdate;
        private Storyboard _onlineAnnouncementStoryboard;

        public MainWindow()
        {
            InitializeComponent();
            StartupManager.StatusChanged += StartupStatus_Changed;
            Closed += MainWindow_Closed;
            txtStartupStatus.Text = StartupManager.CurrentStatus;

            applicationVersion = GetCurrentAppVersion();

            if (txtAppVersion != null)
            {
                txtAppVersion.Text = "SCANER V" + applicationVersion;
            }

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            UpdateClock();
            Loaded += MainWindow_Loaded;
        }

        private string GetCurrentAppVersion()
        {
            string configuredVersion = ConfigurationManager.AppSettings["CurrentVersion"];
            if (!string.IsNullOrWhiteSpace(configuredVersion))
            {
                return configuredVersion;
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            if (informationalVersion != null &&
                !string.IsNullOrWhiteSpace(informationalVersion.InformationalVersion))
            {
                return informationalVersion.InformationalVersion.Split('+')[0];
            }

            Version version = assembly.GetName().Version;
            return string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateClock();
        }

        private void UpdateClock()
        {
            string now = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            Title = "Scanner V" + applicationVersion + " | " + now;

            if (txtAppVersion != null)
            {
                txtAppVersion.Text = "SCANER V" + applicationVersion;
            }

            if (txtDateTimeVersion != null)
            {
                txtDateTimeVersion.Text = now;
            }
        }

        private void HideRowIndex_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
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

        private void SQLiteTable_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshUpdateStatusAsync(false);
        }

        private void StartupStatus_Changed(string message)
        {
            Dispatcher.BeginInvoke(new Action(() => txtStartupStatus.Text = message));
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            StartupManager.StatusChanged -= StartupStatus_Changed;
            timer.Stop();
            _onlineAnnouncementStoryboard?.Stop();

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StopOnlineAnnouncement();
            }
        }

        private void OnlineAnnouncement_TargetUpdated(
            object sender,
            DataTransferEventArgs e)
        {
            RestartOnlineAnnouncementAnimation();
        }

        private void OnlineAnnouncement_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            RestartOnlineAnnouncementAnimation();
        }

        private void OnlineAnnouncementClose_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.CloseOnlineAnnouncement();
            }
        }

        private void RestartOnlineAnnouncementAnimation()
        {
            if (!IsLoaded ||
                onlineAnnouncementCanvas.ActualWidth <= 0 ||
                onlineAnnouncementText.ActualWidth <= 0)
            {
                return;
            }

            _onlineAnnouncementStoryboard?.Stop();

            if (onlineAnnouncementText.ActualWidth <=
                onlineAnnouncementCanvas.ActualWidth)
            {
                onlineAnnouncementTransform.X = 0;
                return;
            }

            double start = onlineAnnouncementCanvas.ActualWidth;
            double end = -onlineAnnouncementText.ActualWidth;
            double distance = start - end;
            double seconds = Math.Max(6, distance / 70);

            var animation = new DoubleAnimation
            {
                From = start,
                To = end,
                Duration = TimeSpan.FromSeconds(seconds),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(animation, onlineAnnouncementTransform);
            Storyboard.SetTargetProperty(
                animation,
                new PropertyPath(TranslateTransform.XProperty));

            _onlineAnnouncementStoryboard = new Storyboard();
            _onlineAnnouncementStoryboard.Children.Add(animation);
            _onlineAnnouncementStoryboard.Begin();
        }

        private async Task RefreshUpdateStatusAsync(bool showErrorMessage)
        {
            txtUpdateStatus.Text = "Đang kiểm tra bản cập nhật...";
            btnSoftwareUpdate.IsEnabled = false;
            updateNotificationDot.Visibility = Visibility.Collapsed;
            availableUpdate = null;

            GitHubReleaseUpdateInfo update =
                await _updateService.CheckForUpdateAsync(showErrorMessage);
            availableUpdate = update;

            if (availableUpdate != null)
            {
                txtUpdateStatus.Text = "Có bản mới: V" + availableUpdate.Version;
                updateNotificationDot.Visibility = Visibility.Visible;

                // Có bản mới thì cho bấm cập nhật.
                btnSoftwareUpdate.IsEnabled = true;
                return;
            }

            if (_updateService.LastCheckSucceeded)
            {
                txtUpdateStatus.Text = _updateService.LastStatusMessage;
                updateNotificationDot.Visibility = Visibility.Collapsed;

                // Đã mới nhất thì khóa nút cập nhật.
                btnSoftwareUpdate.IsEnabled = false;
                return;
            }

            txtUpdateStatus.Text = string.Empty;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            // Network/API failures stay in Debug logs; the button remains retryable.
            btnSoftwareUpdate.IsEnabled = true;
        }

        private async void SoftwareUpdate_Click(object sender, RoutedEventArgs e)
        {
            btnSoftwareUpdate.IsEnabled = false;

            // Nếu chưa có thông tin bản cập nhật thì kiểm tra lại GitHub.
            if (availableUpdate == null)
            {
                await RefreshUpdateStatusAsync(true);

                if (availableUpdate == null)
                {
                    // Nếu GitHub lỗi thì vẫn cho bấm thử lại.
                    if (!_updateService.LastCheckSucceeded)
                    {
                        btnSoftwareUpdate.IsEnabled = true;
                    }

                    return;
                }
            }

            // GỌI CỬA SỔ RELEASE NOTES Ở ĐÂY
            bool accepted = ShowUpdateDetailDialog(availableUpdate);

            if (!accepted)
            {
                txtUpdateStatus.Text = "Đã hủy cập nhật.";
                btnSoftwareUpdate.IsEnabled = true;
                updateNotificationDot.Visibility = Visibility.Visible;
                return;
            }

            txtUpdateStatus.Text = "Đang tải và cài đặt bản cập nhật...";
            bool installerStarted = false;

            try
            {
                installerStarted = await _updateService.DownloadRunAndExitAsync(availableUpdate);

                if (installerStarted)
                {
                    txtUpdateStatus.Text = "Đã khởi động trình cài đặt cập nhật.";
                    updateNotificationDot.Visibility = Visibility.Collapsed;
                    btnSoftwareUpdate.IsEnabled = false;
                    return;
                }

                await RefreshUpdateStatusAsync(false);
            }
            finally
            {
                if (!installerStarted)
                {
                    if (availableUpdate != null || !_updateService.LastCheckSucceeded)
                    {
                        btnSoftwareUpdate.IsEnabled = true;
                    }
                    else
                    {
                        btnSoftwareUpdate.IsEnabled = false;
                    }
                }
            }
        }

        private bool ShowUpdateDetailDialog(GitHubReleaseUpdateInfo update)
        {
            var detailWindow = new UpdateReleaseNotesWindow(applicationVersion, update)
            {
                Owner = this
            };

            return detailWindow.ShowDialog() == true;
        }
    }
}
