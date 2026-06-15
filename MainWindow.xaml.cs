using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.ViewModel;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SX3_SCANER
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly string applicationVersion;

        private readonly UpdateService _updateService = new UpdateService();
        private UpdateInfo availableUpdate;

        private AnnouncementServerStatusInfo _announcementServerStatus =
            AnnouncementServerStatusInfo.Unknown();

        private bool _hasUpdateAvailable;

        private INotifyPropertyChanged _announcementViewModel;
        private CancellationTokenSource _announcementMarqueeCts;
        private int _announcementMarqueeGeneration;

        public MainWindow()
        {
            InitializeComponent();

            DataContextChanged += MainWindow_DataContextChanged;
            StartupManager.StatusChanged += StartupStatus_Changed;
            StartupManager.AnnouncementServerStatusChanged +=
                AnnouncementServerStatus_Changed;

            Closed += MainWindow_Closed;

            AnnouncementMarqueeHost.SizeChanged +=
                AnnouncementMarqueeHost_SizeChanged;

            txtStartupStatus.Text = StartupManager.CurrentStatus;

            _announcementServerStatus =
                StartupManager.CurrentAnnouncementServerStatus ??
                AnnouncementServerStatusInfo.Unknown();

            AttachAnnouncementViewModel(DataContext as INotifyPropertyChanged);

            applicationVersion = UpdateService.GetCurrentVersionString();

            if (txtAppVersion != null)
            {
                txtAppVersion.Text = "SCANER V" + applicationVersion;
            }

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            UpdateClock();
            UpdateTopRightStatusText();

            Loaded += MainWindow_Loaded;
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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RestartAnnouncementMarquee();
            await RefreshUpdateStatusAsync(false);
        }

        private void AnnouncementMarqueeHost_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || !IsLoaded)
                return;

            RestartAnnouncementMarquee();
        }

        private void StartupStatus_Changed(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtStartupStatus.Text = message;

                if (message.Contains("Sẵn sàng"))
                {
                    txtStartupStatus.Foreground = Brushes.Green;
                    txtStartupStatus.FontSize = 22;
                    txtStartupStatus.FontWeight = FontWeights.ExtraBold;
                }
                else if (message.Contains("Đang quét"))
                {
                    txtStartupStatus.Foreground = Brushes.DodgerBlue;
                    txtStartupStatus.FontSize = 22;
                    txtStartupStatus.FontWeight = FontWeights.ExtraBold;
                }
                else if (message.Contains("Lỗi"))
                {
                    txtStartupStatus.Foreground = Brushes.Red;
                    txtStartupStatus.FontSize = 22;
                    txtStartupStatus.FontWeight = FontWeights.ExtraBold;
                }
                else
                {
                    txtStartupStatus.Foreground = Brushes.DarkOrange;
                    txtStartupStatus.FontSize = 16;
                    txtStartupStatus.FontWeight = FontWeights.Bold;
                }
            }));
        }

        private void AnnouncementServerStatus_Changed(
            AnnouncementServerStatusInfo status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _announcementServerStatus =
                    status ?? AnnouncementServerStatusInfo.Unknown();

                UpdateTopRightStatusText();
            }));
        }

        private void UpdateTopRightStatusText()
        {
            if (txtUpdateStatus == null)
                return;

            if (_hasUpdateAvailable && availableUpdate != null)
            {
                txtUpdateStatus.Text =
                    "Có bản cập nhật mới: V" + availableUpdate.Version;

                txtUpdateStatus.Foreground =
                    new SolidColorBrush(Color.FromRgb(153, 27, 27));

                serverStatusBadge.Background =
                    new SolidColorBrush(Color.FromRgb(254, 242, 242));

                serverStatusBadge.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(220, 38, 38));

                serverStatusDot.Fill =
                    new SolidColorBrush(Color.FromRgb(220, 38, 38));

                serverStatusGlow.Fill =
                    new SolidColorBrush(Color.FromRgb(220, 38, 38));

                softwareUpdatePanel.Visibility = Visibility.Visible;
                updateNotificationDot.Visibility = Visibility.Visible;
                return;
            }

            softwareUpdatePanel.Visibility = Visibility.Collapsed;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            if (_announcementServerStatus == null)
            {
                _announcementServerStatus =
                    AnnouncementServerStatusInfo.Unknown();
            }

            if (_announcementServerStatus.IsConnected &&
                !_announcementServerStatus.IsUsingFallback)
            {
                txtUpdateStatus.Text = "Đã kết nối máy chủ";

                txtUpdateStatus.Foreground =
                    new SolidColorBrush(Color.FromRgb(22, 101, 52));

                serverStatusBadge.Background =
                    new SolidColorBrush(Color.FromRgb(236, 253, 245));

                serverStatusBadge.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(34, 197, 94));

                serverStatusDot.Fill =
                    new SolidColorBrush(Color.FromRgb(34, 197, 94));

                serverStatusGlow.Fill =
                    new SolidColorBrush(Color.FromRgb(34, 197, 94));

                return;
            }

            if (_announcementServerStatus.IsConnected &&
                _announcementServerStatus.IsUsingFallback)
            {
                txtUpdateStatus.Text = "Đang dùng máy chủ dự phòng";

                txtUpdateStatus.Foreground =
                    new SolidColorBrush(Color.FromRgb(146, 64, 14));

                serverStatusBadge.Background =
                    new SolidColorBrush(Color.FromRgb(255, 251, 235));

                serverStatusBadge.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(245, 158, 11));

                serverStatusDot.Fill =
                    new SolidColorBrush(Color.FromRgb(245, 158, 11));

                serverStatusGlow.Fill =
                    new SolidColorBrush(Color.FromRgb(245, 158, 11));

                return;
            }

            txtUpdateStatus.Text = "Mất kết nối máy chủ thông báo";

            txtUpdateStatus.Foreground =
                new SolidColorBrush(Color.FromRgb(153, 27, 27));

            serverStatusBadge.Background =
                new SolidColorBrush(Color.FromRgb(254, 242, 242));

            serverStatusBadge.BorderBrush =
                new SolidColorBrush(Color.FromRgb(220, 38, 38));

            serverStatusDot.Fill =
                new SolidColorBrush(Color.FromRgb(220, 38, 38));

            serverStatusGlow.Fill =
                new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            DataContextChanged -= MainWindow_DataContextChanged;
            StartupManager.StatusChanged -= StartupStatus_Changed;
            StartupManager.AnnouncementServerStatusChanged -=
                AnnouncementServerStatus_Changed;

            AnnouncementMarqueeHost.SizeChanged -=
                AnnouncementMarqueeHost_SizeChanged;

            timer.Stop();
            StopOnlineAnnouncementAnimation();
            AttachAnnouncementViewModel(null);

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StopOnlineAnnouncement();
            }
        }

        private void MainWindow_DataContextChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            AttachAnnouncementViewModel(e.NewValue as INotifyPropertyChanged);
        }

        private void AttachAnnouncementViewModel(
            INotifyPropertyChanged viewModel)
        {
            if (ReferenceEquals(_announcementViewModel, viewModel))
                return;

            if (_announcementViewModel != null)
            {
                _announcementViewModel.PropertyChanged -=
                    AnnouncementViewModel_PropertyChanged;
            }

            _announcementViewModel = viewModel;

            if (_announcementViewModel != null)
            {
                _announcementViewModel.PropertyChanged +=
                    AnnouncementViewModel_PropertyChanged;
            }

            RestartAnnouncementMarquee();
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

        private void AnnouncementViewModel_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            string propertyName = e?.PropertyName ?? string.Empty;

            if (string.IsNullOrEmpty(propertyName) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.OnlineAnnouncementText),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.OnlineAnnouncementAnimationVersion),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.IsOnlineAnnouncementVisible),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.IsOnlineAnnouncementMarqueeEnabled),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.OnlineAnnouncementMarqueeDirection),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.OnlineAnnouncementMarqueeSpeed),
                    StringComparison.Ordinal) ||
                string.Equals(
                    propertyName,
                    nameof(MainViewModel.OnlineAnnouncementMarqueeDelaySeconds),
                    StringComparison.Ordinal))
            {
                RestartAnnouncementMarquee();
            }
        }

        private async void RestartAnnouncementMarquee()
        {
            StopOnlineAnnouncementAnimation();

            if (!IsLoaded ||
                !(DataContext is MainViewModel viewModel) ||
                !viewModel.IsOnlineAnnouncementVisible ||
                !viewModel.IsOnlineAnnouncementMarqueeEnabled ||
                string.IsNullOrWhiteSpace(viewModel.OnlineAnnouncementText))
            {
                if (AnnouncementMarqueeTransform != null)
                    AnnouncementMarqueeTransform.X = 0;

                return;
            }

            int generation =
                Interlocked.Increment(ref _announcementMarqueeGeneration);

            CancellationTokenSource cts = new CancellationTokenSource();

            CancellationTokenSource previousCts =
                Interlocked.Exchange(ref _announcementMarqueeCts, cts);

            previousCts?.Cancel();
            previousCts?.Dispose();

            try
            {
                await Dispatcher.BeginInvoke(
                    new Action(() => { }),
                    DispatcherPriority.Loaded).Task;
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested ||
                generation != _announcementMarqueeGeneration ||
                !TryGetAnnouncementMarqueeMetrics(
                    out double hostWidth,
                    out double textWidth))
            {
                if (cts.IsCancellationRequested ||
                    generation != _announcementMarqueeGeneration)
                {
                    return;
                }

                try
                {
                    await Task.Delay(100, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (cts.IsCancellationRequested ||
                    generation != _announcementMarqueeGeneration ||
                    !TryGetAnnouncementMarqueeMetrics(
                        out hostWidth,
                        out textWidth))
                {
                    if (AnnouncementMarqueeTransform != null)
                        AnnouncementMarqueeTransform.X = 0;

                    return;
                }
            }

            double from = hostWidth;
            double to = -textWidth;

            const double pixelsPerSecond = 55;

            double distance = hostWidth + textWidth;
            double seconds = Math.Max(12, distance / pixelsPerSecond);

            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(seconds),
                FillBehavior = FillBehavior.Stop
            };

            Timeline.SetDesiredFrameRate(animation, 60);

            animation.Completed += async (animationSender, animationArgs) =>
            {
                if (cts.IsCancellationRequested ||
                    generation != _announcementMarqueeGeneration ||
                    !IsLoaded)
                {
                    return;
                }

                if (AnnouncementMarqueeTransform != null)
                {
                    AnnouncementMarqueeTransform.BeginAnimation(
                        TranslateTransform.XProperty,
                        null);

                    AnnouncementMarqueeTransform.X = to;
                }

                await Task.Delay(150, cts.Token);

                if (cts.IsCancellationRequested ||
                    generation != _announcementMarqueeGeneration ||
                    !IsLoaded ||
                    !(DataContext is MainViewModel currentViewModel) ||
                    !currentViewModel.IsOnlineAnnouncementVisible ||
                    !currentViewModel.IsOnlineAnnouncementMarqueeEnabled ||
                    string.IsNullOrWhiteSpace(
                        currentViewModel.OnlineAnnouncementText))
                {
                    return;
                }

                if (!currentViewModel.CompleteOnlineAnnouncementMarqueeCycle())
                {
                    RestartAnnouncementMarquee();
                }
            };

            if (AnnouncementMarqueeTransform != null)
            {
                AnnouncementMarqueeTransform.X = from;

                AnnouncementMarqueeTransform.BeginAnimation(
                    TranslateTransform.XProperty,
                    animation);
            }
        }

        private void StopOnlineAnnouncementAnimation()
        {
            Interlocked.Increment(ref _announcementMarqueeGeneration);

            CancellationTokenSource cts =
                Interlocked.Exchange(ref _announcementMarqueeCts, null);

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            if (AnnouncementMarqueeTransform != null)
            {
                AnnouncementMarqueeTransform.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);

                AnnouncementMarqueeTransform.X = 0;
            }
        }

        private bool TryGetAnnouncementMarqueeMetrics(
            out double hostWidth,
            out double textWidth)
        {
            hostWidth = AnnouncementMarqueeHost?.ActualWidth ?? 0;
            textWidth = AnnouncementMarqueeText?.ActualWidth ?? 0;

            if (textWidth <= 0 && AnnouncementMarqueeText != null)
            {
                AnnouncementMarqueeText.Measure(
                    new Size(
                        double.PositiveInfinity,
                        double.PositiveInfinity));

                textWidth = AnnouncementMarqueeText.DesiredSize.Width;
            }

            return hostWidth > 0 && textWidth > 0;
        }

        private async Task RefreshUpdateStatusAsync(bool showErrorMessage)
        {
            txtUpdateStatus.Text = "Đang kiểm tra bản cập nhật...";
            txtUpdateStatus.Foreground = Brushes.DarkOrange;

            btnSoftwareUpdate.IsEnabled = false;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            availableUpdate = null;
            _hasUpdateAvailable = false;

            UpdateInfo update =
                await _updateService.CheckForUpdateAsync(showErrorMessage);

            availableUpdate = update;

            if (availableUpdate != null)
            {
                _hasUpdateAvailable = true;
                btnSoftwareUpdate.IsEnabled = true;
                UpdateTopRightStatusText();
                return;
            }

            _hasUpdateAvailable = false;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            if (_updateService.LastCheckSucceeded)
            {
                btnSoftwareUpdate.IsEnabled = false;
                UpdateTopRightStatusText();
                return;
            }

            btnSoftwareUpdate.IsEnabled = true;
            UpdateTopRightStatusText();
        }

        private async void SoftwareUpdate_Click(
            object sender,
            RoutedEventArgs e)
        {
            btnSoftwareUpdate.IsEnabled = false;

            if (availableUpdate == null)
            {
                await RefreshUpdateStatusAsync(true);

                if (availableUpdate == null)
                {
                    if (!_updateService.LastCheckSucceeded)
                    {
                        btnSoftwareUpdate.IsEnabled = true;
                    }

                    _hasUpdateAvailable = false;
                    UpdateTopRightStatusText();
                    return;
                }
            }

            txtUpdateStatus.Text = "Đang tải và xác thực bản cập nhật...";
            txtUpdateStatus.Foreground = Brushes.DarkOrange;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            bool installerStarted = false;

            try
            {
                string installerPath =
                    await _updateService.DownloadAndVerifyAsync(
                        availableUpdate);

                txtUpdateStatus.Text = "Bản cập nhật đã được xác thực.";

                bool accepted = ShowUpdateDetailDialog(availableUpdate);

                if (!accepted)
                {
                    _hasUpdateAvailable = true;
                    btnSoftwareUpdate.IsEnabled = true;
                    UpdateTopRightStatusText();
                    return;
                }

                installerStarted =
                    _updateService.TryStartInstallerAndExit(installerPath);

                if (installerStarted)
                {
                    _hasUpdateAvailable = false;
                    availableUpdate = null;

                    txtUpdateStatus.Text =
                        "Đã khởi động trình cài đặt cập nhật.";

                    updateNotificationDot.Visibility = Visibility.Collapsed;
                    btnSoftwareUpdate.IsEnabled = false;
                    return;
                }

                btnSoftwareUpdate.IsEnabled = true;
                UpdateTopRightStatusText();
            }
            catch (Exception ex)
            {
                _updateService.ReportDownloadError(ex);

                txtUpdateStatus.Text = _updateService.LastStatusMessage;
                txtUpdateStatus.Foreground = Brushes.Red;

                btnSoftwareUpdate.IsEnabled = true;
            }
            finally
            {
                if (!installerStarted)
                {
                    if (availableUpdate != null ||
                        !_updateService.LastCheckSucceeded)
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

        private bool ShowUpdateDetailDialog(UpdateInfo update)
        {
            var detailWindow =
                new UpdateReleaseNotesWindow(applicationVersion, update)
                {
                    Owner = this
                };

            return detailWindow.ShowDialog() == true;
        }
    }
}