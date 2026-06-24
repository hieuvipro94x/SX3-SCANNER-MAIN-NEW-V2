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
    public partial class MainWindow
    {
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

                    bool acceptedReleaseNotes = ShowUpdateDetailDialog(update);
                    if (!acceptedReleaseNotes)
                    {
                        lastError =
                            "Bạn phải xác nhận nội dung cập nhật để tiếp tục.";
                        continue;
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
