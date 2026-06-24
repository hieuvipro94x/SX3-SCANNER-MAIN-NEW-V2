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
        private readonly DispatcherTimer updatePollingTimer = new DispatcherTimer();
        private readonly DispatcherTimer updateReadinessTimer = new DispatcherTimer();
        private static readonly TimeSpan UpdatePollingInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan UpdateReadinessCheckInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RequiredScanInputIdleTime = TimeSpan.FromMinutes(1);
        private readonly string applicationVersion;

        private readonly UpdateService _updateService = new UpdateService();
        private UpdateInfo availableUpdate;

        private AnnouncementServerStatusInfo _announcementServerStatus =
            AnnouncementServerStatusInfo.Unknown();

        private bool _hasUpdateAvailable;
        private bool _isUpdateStatusBusy;
        private bool _showUpdateErrorStatus;
        private bool _mandatoryUpdateWorkflowStarted;
        private bool _isBackgroundUpdateCheckRunning;
        private bool _isMandatoryUpdateWorkflowRunning;
        private bool isUpdateDeferredUntilScannerIdle;
        private bool _startupInitializationStarted;
        private DateTime _lastScanInputActivityAt = DateTime.Now;
        private string _currentScanInputText = string.Empty;

        private INotifyPropertyChanged _announcementViewModel;
        private CancellationTokenSource _announcementMarqueeCts;
        private int _announcementMarqueeGeneration;

        public MainWindow()
        {
            InitializeComponent();
            StartupManager.EnsureStartWithWindows();

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

            SetOptionalTextBlockText("txtAppVersion", "SCANER V" + applicationVersion);

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            updatePollingTimer.Interval = UpdatePollingInterval;
            updatePollingTimer.Tick += UpdatePollingTimer_Tick;
            updateReadinessTimer.Interval = UpdateReadinessCheckInterval;
            updateReadinessTimer.Tick += UpdateReadinessTimer_Tick;
            AddHandler(
                TextBox.TextChangedEvent,
                new TextChangedEventHandler(ScanInputTextChanged),
                true);

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

            SetOptionalTextBlockText("txtAppVersion", "SCANER V" + applicationVersion);

            SetOptionalTextBlockText("txtDateTimeVersion", now);
        }

        private void SetOptionalTextBlockText(string controlName, string text)
        {
            TextBlock textBlock = FindName(controlName) as TextBlock;
            if (textBlock != null)
            {
                textBlock.Text = text ?? string.Empty;
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

            if (!_startupInitializationStarted)
            {
                _startupInitializationStarted = true;

                MainViewModel viewModel = DataContext as MainViewModel;
                if (viewModel != null)
                {
                    await viewModel.InitializeApplicationAsync();
                }
            }

#if !DEBUG
            await EnsureMandatoryUpdateBeforeRunAsync();
#else
            await Task.CompletedTask;
#endif

            if (Application.Current != null &&
                !Application.Current.Dispatcher.HasShutdownStarted)
            {
                StartUpdatePolling();
            }
        }

        private void StartUpdatePolling()
        {
            if (updatePollingTimer.IsEnabled)
                return;

            updatePollingTimer.Start();
            StartupManager.Log(
                "Đã bật kiểm tra cập nhật định kỳ mỗi " +
                UpdatePollingInterval.TotalMinutes.ToString("0") +
                " phút.");
        }

        private async void UpdatePollingTimer_Tick(object sender, EventArgs e)
        {
            await CheckMandatoryUpdateWhileRunningAsync();
        }

        private async void UpdateReadinessTimer_Tick(object sender, EventArgs e)
        {
            if (!isUpdateDeferredUntilScannerIdle ||
                availableUpdate == null ||
                _isMandatoryUpdateWorkflowRunning ||
                _isBackgroundUpdateCheckRunning ||
                Application.Current == null ||
                Application.Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (!CanShowMandatoryUpdateNow())
            {
                return;
            }

            updateReadinessTimer.Stop();
            await RunDeferredAutoUpdateWorkflowAsync();
        }

        private async Task CheckMandatoryUpdateWhileRunningAsync()
        {
            if (_isBackgroundUpdateCheckRunning ||
                _isMandatoryUpdateWorkflowRunning ||
                Application.Current == null ||
                Application.Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _isBackgroundUpdateCheckRunning = true;

            try
            {
                if (isUpdateDeferredUntilScannerIdle &&
                    availableUpdate != null)
                {
                    if (!CanShowMandatoryUpdateNow())
                    {
                        SetDeferredUpdateWaitingStatus(
                            GetScannerBusyReasonForUpdate());
                        StartUpdateReadinessPolling();
                        return;
                    }

                    await RunDeferredAutoUpdateWorkflowAsync();
                    return;
                }

                UpdateInfo update =
                    await _updateService.CheckForMandatoryUpdateAsync();

                if (Application.Current.Dispatcher.HasShutdownStarted)
                    return;

                if (update == null)
                {
                    if (_updateService.LastCheckSucceeded &&
                        !_hasUpdateAvailable &&
                        !_isUpdateStatusBusy)
                    {
                        UpdateTopRightStatusText();
                    }

                    return;
                }

                availableUpdate = update;
                _showUpdateErrorStatus = false;
                _isUpdateStatusBusy = false;

                if (!CanShowMandatoryUpdateNow())
                {
                    _hasUpdateAvailable = true;
                    isUpdateDeferredUntilScannerIdle = true;
                    SetDeferredUpdateWaitingStatus(
                        GetScannerBusyReasonForUpdate());
                    StartUpdateReadinessPolling();
                    return;
                }

                _hasUpdateAvailable = true;
                txtUpdateStatus.Text =
                    "Phát hiện bản cập nhật bắt buộc: V" + update.Version;
                txtUpdateStatus.Foreground = Brushes.Red;
                softwareUpdatePanel.Visibility = Visibility.Visible;
                updateNotificationDot.Visibility = Visibility.Visible;
                btnSoftwareUpdate.IsEnabled = false;

                updatePollingTimer.Stop();
                await RunMandatoryUpdateWorkflowAsync(update);
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Không kiểm tra được cập nhật định kỳ khi app đang chạy: " + ex);
            }
            finally
            {
                _isBackgroundUpdateCheckRunning = false;

                if (Application.Current != null &&
                    !Application.Current.Dispatcher.HasShutdownStarted &&
                    !updatePollingTimer.IsEnabled &&
                    !_isMandatoryUpdateWorkflowRunning)
                {
                    updatePollingTimer.Start();
                }
            }
        }

        private void StartUpdateReadinessPolling()
        {
            if (!updateReadinessTimer.IsEnabled)
            {
                updateReadinessTimer.Start();
            }
        }

        private bool IsScannerIdleForAutoUpdate()
        {
            MainViewModel viewModel = DataContext as MainViewModel;
            if (viewModel == null)
                return true;

            return !viewModel.InJob &&
                !viewModel.HasOpenScanSession &&
                !viewModel.IsFullBoxReadyToComplete;
        }

        private bool CanShowMandatoryUpdateNow()
        {
            return IsScannerIdleForAutoUpdate() &&
                IsScanInputIdleForMandatoryUpdate();
        }

        private bool IsScanInputIdleForMandatoryUpdate()
        {
            MainViewModel viewModel = DataContext as MainViewModel;
            if (viewModel == null)
                return true;

            if (!string.IsNullOrWhiteSpace(_currentScanInputText))
                return false;

            if (!string.IsNullOrWhiteSpace(viewModel.InputScanCode))
                return false;

            return DateTime.Now - _lastScanInputActivityAt >=
                RequiredScanInputIdleTime;
        }

        private void ScanInputTextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = e.OriginalSource as TextBox;
            if (textBox == null ||
                !string.Equals(
                    textBox.Name,
                    "TbxInputCode",
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastScanInputActivityAt = DateTime.Now;
            _currentScanInputText = textBox.Text ?? string.Empty;
        }

        private string GetScannerBusyReasonForUpdate()
        {
            MainViewModel viewModel = DataContext as MainViewModel;
            if (viewModel == null)
                return "ứng dụng đang bận";

            if (viewModel.IsFullBoxReadyToComplete)
                return "thùng đã đủ số lượng, vui lòng đóng thùng trước";

            if (viewModel.InJob)
                return "đang trong phiên scan";

            if (viewModel.HasOpenScanSession)
                return "thùng hiện tại đang mở/chưa hoàn tất";

            return "scanner chưa sẵn sàng";
        }

        private void SetDeferredUpdateWaitingStatus(string reason)
        {
            txtUpdateStatus.Text =
                "Có bản cập nhật mới. Có thể cập nhật thủ công sau khi app lưu phiên scan đang dở" +
                (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason + ".");

            txtUpdateStatus.Foreground = Brushes.Red;
            softwareUpdatePanel.Visibility = Visibility.Visible;
            updateNotificationDot.Visibility = Visibility.Visible;
            btnSoftwareUpdate.IsEnabled = true;
        }

        private async Task RunDeferredAutoUpdateWorkflowAsync()
        {
            if (availableUpdate == null)
            {
                isUpdateDeferredUntilScannerIdle = false;
                return;
            }

            isUpdateDeferredUntilScannerIdle = false;
            updatePollingTimer.Stop();
            updateReadinessTimer.Stop();
            await RunMandatoryUpdateWorkflowAsync(availableUpdate);
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

            if (_isUpdateStatusBusy)
                return;

            if (_showUpdateErrorStatus)
            {
                softwareUpdatePanel.Visibility = Visibility.Visible;
                updateNotificationDot.Visibility = Visibility.Collapsed;
                return;
            }

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
            updatePollingTimer.Stop();
            updateReadinessTimer.Stop();
            updatePollingTimer.Tick -= UpdatePollingTimer_Tick;
            updateReadinessTimer.Tick -= UpdateReadinessTimer_Tick;
            RemoveHandler(
                TextBox.TextChangedEvent,
                new TextChangedEventHandler(ScanInputTextChanged));
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


        private enum MandatoryUpdateDialogChoice
        {
            UpdateNow,
            ExitApplication
        }

    }
}
