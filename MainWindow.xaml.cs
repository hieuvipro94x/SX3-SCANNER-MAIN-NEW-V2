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
        private static readonly TimeSpan UpdatePollingInterval = TimeSpan.FromMinutes(5);
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


            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            updatePollingTimer.Interval = UpdatePollingInterval;
            updatePollingTimer.Tick += UpdatePollingTimer_Tick;

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
            await EnsureMandatoryUpdateBeforeRunAsync();
            StartUpdatePolling();
        }

        private void StartUpdatePolling()
        {
            if (updatePollingTimer.IsEnabled)
                return;

            // App đang chạy vẫn tự kiểm tra cập nhật định kỳ.
            // Khi phát hiện bản mới, popup bắt buộc cập nhật sẽ hiện ngay,
            // không cần đóng/mở lại phần mềm.
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
                _hasUpdateAvailable = true;
                _showUpdateErrorStatus = false;
                _isUpdateStatusBusy = false;

                txtUpdateStatus.Text =
                    "Phát hiện bản cập nhật bắt buộc: V" + update.Version;
                txtUpdateStatus.Foreground = Brushes.Red;
                softwareUpdatePanel.Visibility = Visibility.Visible;
                updateNotificationDot.Visibility = Visibility.Visible;
                btnSoftwareUpdate.IsEnabled = false;

                // Tạm dừng timer trong lúc popup/download installer đang chạy
                // để tránh mở nhiều popup cùng lúc.
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
                    !updatePollingTimer.IsEnabled)
                {
                    updatePollingTimer.Start();
                }
            }
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
            updatePollingTimer.Tick -= UpdatePollingTimer_Tick;
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

        private async Task EnsureMandatoryUpdateBeforeRunAsync()
        {
            if (_mandatoryUpdateWorkflowStarted)
                return;

            _mandatoryUpdateWorkflowStarted = true;
            _isUpdateStatusBusy = true;
            _showUpdateErrorStatus = false;

            txtUpdateStatus.Text = "Đang kiểm tra bản cập nhật bắt buộc...";
            txtUpdateStatus.Foreground = Brushes.DarkOrange;
            btnSoftwareUpdate.IsEnabled = false;
            softwareUpdatePanel.Visibility = Visibility.Visible;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            try
            {
                availableUpdate = await _updateService.CheckForMandatoryUpdateAsync();
                _hasUpdateAvailable = availableUpdate != null;

                if (availableUpdate == null)
                {
                    return;
                }

                txtUpdateStatus.Text =
                    "Bắt buộc cập nhật lên V" + availableUpdate.Version;
                txtUpdateStatus.Foreground = Brushes.Red;
                softwareUpdatePanel.Visibility = Visibility.Visible;
                updateNotificationDot.Visibility = Visibility.Visible;

                await RunMandatoryUpdateWorkflowAsync(availableUpdate);
            }
            finally
            {
                if (!Application.Current.Dispatcher.HasShutdownStarted)
                {
                    _isUpdateStatusBusy = false;

                    if (_hasUpdateAvailable && availableUpdate != null)
                    {
                        UpdateTopRightStatusText();
                    }
                    else if (_updateService.LastCheckSucceeded)
                    {
                        UpdateTopRightStatusText();
                    }
                    else
                    {
                        _showUpdateErrorStatus = true;
                        txtUpdateStatus.Text = _updateService.LastStatusMessage;
                        txtUpdateStatus.Foreground = Brushes.Red;
                        btnSoftwareUpdate.IsEnabled = true;
                        softwareUpdatePanel.Visibility = Visibility.Visible;
                        updateNotificationDot.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private async Task RunMandatoryUpdateWorkflowAsync(UpdateInfo update)
        {
            if (update == null || _isMandatoryUpdateWorkflowRunning)
                return;

            _isMandatoryUpdateWorkflowRunning = true;
            string lastError = null;

            try
            {
                while (!Application.Current.Dispatcher.HasShutdownStarted)
                {
                    MandatoryUpdateDialogChoice choice =
                        ShowMandatoryUpdateDialog(update, lastError);

                    if (choice != MandatoryUpdateDialogChoice.UpdateNow)
                    {
                        Application.Current.Shutdown();
                        return;
                    }

                    _isUpdateStatusBusy = true;
                    _showUpdateErrorStatus = false;
                    txtUpdateStatus.Text =
                        "Đang tải và xác thực bản cập nhật bắt buộc...";
                    txtUpdateStatus.Foreground = Brushes.DarkOrange;
                    btnSoftwareUpdate.IsEnabled = false;
                    softwareUpdatePanel.Visibility = Visibility.Visible;
                    updateNotificationDot.Visibility = Visibility.Visible;

                    try
                    {
                        string installerPath =
                            await _updateService.DownloadAndVerifyAsync(update);

                        txtUpdateStatus.Text =
                            "Bản cập nhật đã xác thực. Đang mở trình cài đặt...";

                        bool installerStarted =
                            _updateService.TryStartInstallerAndExit(installerPath);

                        if (installerStarted)
                            return;

                        lastError = string.IsNullOrWhiteSpace(
                            _updateService.LastStatusMessage)
                            ? "Không mở được trình cài đặt cập nhật."
                            : _updateService.LastStatusMessage;
                    }
                    catch (Exception ex)
                    {
                        _updateService.ReportDownloadError(ex);
                        lastError = string.IsNullOrWhiteSpace(
                            _updateService.LastStatusMessage)
                            ? ex.Message
                            : _updateService.LastStatusMessage;
                    }
                    finally
                    {
                        if (!Application.Current.Dispatcher.HasShutdownStarted)
                        {
                            _isUpdateStatusBusy = false;
                            _hasUpdateAvailable = true;
                            availableUpdate = update;
                            btnSoftwareUpdate.IsEnabled = true;
                        }
                    }
                }
            }
            finally
            {
                _isMandatoryUpdateWorkflowRunning = false;
            }
        }

        private MandatoryUpdateDialogChoice ShowMandatoryUpdateDialog(
            UpdateInfo update,
            string errorMessage)
        {
            Window dialog = new Window
            {
                Title = "BẮT BUỘC CẬP NHẬT PHẦN MỀM",
                Width = 680,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                Background = Brushes.Transparent,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Owner = IsVisible ? this : null
            };

            MandatoryUpdateDialogChoice choice =
                MandatoryUpdateDialogChoice.ExitApplication;

            Border root = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(26),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                BorderThickness = new Thickness(2)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border icon = new Border
            {
                Width = 62,
                Height = 62,
                CornerRadius = new CornerRadius(31),
                Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 16, 0)
            };

            icon.Child = new TextBlock
            {
                Text = "!",
                FontSize = 38,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            StackPanel titlePanel = new StackPanel();
            titlePanel.Children.Add(new TextBlock
            {
                Text = "BẮT BUỘC CẬP NHẬT PHẦN MỀM",
                FontSize = 24,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 29, 29)),
                TextWrapping = TextWrapping.Wrap
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Phát hiện phiên bản mới. Bạn phải cập nhật trước khi tiếp tục sử dụng SX3 Scanner.",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            header.Children.Add(icon);
            header.Children.Add(titlePanel);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            Border infoBox = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1)
            };

            Grid infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddUpdateInfoText(infoGrid, 0, 0, "Phiên bản hiện tại", "V" + applicationVersion);
            AddUpdateInfoText(infoGrid, 0, 1, "Phiên bản mới", "V" + update.Version);
            AddUpdateInfoText(infoGrid, 2, 0, "File cập nhật", update.FileName ?? "SX3ScannerSetup.exe");
            AddUpdateInfoText(infoGrid, 2, 1, "Dung lượng", FormatFileSize(update.FileSize));

            infoBox.Child = infoGrid;
            Grid.SetRow(infoBox, 2);
            grid.Children.Add(infoBox);

            Border noteBox = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Background = string.IsNullOrWhiteSpace(errorMessage)
                    ? new SolidColorBrush(Color.FromRgb(255, 251, 235))
                    : new SolidColorBrush(Color.FromRgb(254, 242, 242)),
                BorderBrush = string.IsNullOrWhiteSpace(errorMessage)
                    ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                    : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                BorderThickness = new Thickness(1)
            };

            noteBox.Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(errorMessage)
                    ? "Không có nút bỏ qua. Nếu không cập nhật, ứng dụng sẽ thoát để tránh dùng sai phiên bản."
                    : "Cập nhật chưa thành công: " + errorMessage +
                      "\nVui lòng thử cập nhật lại hoặc thoát ứng dụng.",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = string.IsNullOrWhiteSpace(errorMessage)
                    ? new SolidColorBrush(Color.FromRgb(146, 64, 14))
                    : new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };

            Grid.SetRow(noteBox, 4);
            grid.Children.Add(noteBox);

            Grid buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button exitButton = CreateMandatoryUpdateButton(
                "THOÁT ỨNG DỤNG",
                new SolidColorBrush(Color.FromRgb(71, 85, 105)));
            exitButton.Click += delegate
            {
                choice = MandatoryUpdateDialogChoice.ExitApplication;
                dialog.DialogResult = false;
                dialog.Close();
            };

            Button updateButton = CreateMandatoryUpdateButton(
                "CẬP NHẬT NGAY",
                new SolidColorBrush(Color.FromRgb(220, 38, 38)));
            updateButton.Click += delegate
            {
                choice = MandatoryUpdateDialogChoice.UpdateNow;
                dialog.DialogResult = true;
                dialog.Close();
            };

            Grid.SetColumn(exitButton, 0);
            Grid.SetColumn(updateButton, 2);
            buttonGrid.Children.Add(exitButton);
            buttonGrid.Children.Add(updateButton);

            Grid.SetRow(buttonGrid, 6);
            grid.Children.Add(buttonGrid);

            root.Child = grid;
            dialog.Content = root;
            dialog.ShowDialog();

            return choice;
        }

        private static void AddUpdateInfoText(
            Grid grid,
            int row,
            int column,
            string label,
            string value)
        {
            StackPanel panel = new StackPanel
            {
                Margin = column == 0
                    ? new Thickness(0, 0, 12, 0)
                    : new Thickness(12, 0, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 3)
            });

            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                FontSize = 16,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }

        private static Button CreateMandatoryUpdateButton(
            string text,
            Brush background)
        {
            return new Button
            {
                Content = text,
                Height = 48,
                FontSize = 15,
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                Background = background,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0)
                return "Không xác định";

            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return size.ToString(unitIndex == 0 ? "0" : "0.0") + " " + units[unitIndex];
        }

        private async Task RefreshUpdateStatusAsync(bool showErrorMessage)
        {
            _isUpdateStatusBusy = true;

            txtUpdateStatus.Text = "Đang kiểm tra bản cập nhật...";
            txtUpdateStatus.Foreground = Brushes.DarkOrange;

            btnSoftwareUpdate.IsEnabled = false;
            softwareUpdatePanel.Visibility = Visibility.Visible;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            availableUpdate = null;
            _hasUpdateAvailable = false;
            _showUpdateErrorStatus = false;

            try
            {
                UpdateInfo update =
                    await _updateService.CheckForUpdateAsync(showErrorMessage);

                availableUpdate = update;

                if (availableUpdate != null)
                {
                    _hasUpdateAvailable = true;
                    btnSoftwareUpdate.IsEnabled = true;
                    return;
                }

                _hasUpdateAvailable = false;
                updateNotificationDot.Visibility = Visibility.Collapsed;

                if (_updateService.LastCheckSucceeded)
                {
                    btnSoftwareUpdate.IsEnabled = false;
                    return;
                }

                txtUpdateStatus.Text = _updateService.LastStatusMessage;
                txtUpdateStatus.Foreground = Brushes.Red;
                btnSoftwareUpdate.IsEnabled = true;
                _showUpdateErrorStatus = true;
            }
            finally
            {
                _isUpdateStatusBusy = false;

                if (_hasUpdateAvailable && availableUpdate != null)
                {
                    UpdateTopRightStatusText();
                }
                else if (_updateService.LastCheckSucceeded)
                {
                    UpdateTopRightStatusText();
                }
                else
                {
                    softwareUpdatePanel.Visibility = Visibility.Visible;
                    updateNotificationDot.Visibility = Visibility.Collapsed;
                }
            }
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
                    return;
            }

            _isUpdateStatusBusy = true;
            _showUpdateErrorStatus = false;

            txtUpdateStatus.Text = "Đang tải và xác thực bản cập nhật...";
            txtUpdateStatus.Foreground = Brushes.DarkOrange;
            softwareUpdatePanel.Visibility = Visibility.Visible;
            updateNotificationDot.Visibility = Visibility.Collapsed;

            bool installerStarted = false;
            bool updateOperationFailed = false;

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
            }
            catch (Exception ex)
            {
                _updateService.ReportDownloadError(ex);

                updateOperationFailed = true;
                _showUpdateErrorStatus = true;

                txtUpdateStatus.Text = _updateService.LastStatusMessage;
                txtUpdateStatus.Foreground = Brushes.Red;
                softwareUpdatePanel.Visibility = Visibility.Visible;

                btnSoftwareUpdate.IsEnabled = true;
            }
            finally
            {
                if (!installerStarted)
                {
                    _isUpdateStatusBusy = false;

                    if (updateOperationFailed)
                    {
                        _hasUpdateAvailable = availableUpdate != null;
                        btnSoftwareUpdate.IsEnabled = true;
                        softwareUpdatePanel.Visibility = Visibility.Visible;
                        updateNotificationDot.Visibility = Visibility.Collapsed;
                    }
                    else if (availableUpdate != null)
                    {
                        _hasUpdateAvailable = true;
                        btnSoftwareUpdate.IsEnabled = true;
                        UpdateTopRightStatusText();
                    }
                    else if (_updateService.LastCheckSucceeded)
                    {
                        _hasUpdateAvailable = false;
                        btnSoftwareUpdate.IsEnabled = false;
                        UpdateTopRightStatusText();
                    }
                    else
                    {
                        btnSoftwareUpdate.IsEnabled = true;
                        softwareUpdatePanel.Visibility = Visibility.Visible;
                        updateNotificationDot.Visibility = Visibility.Collapsed;
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