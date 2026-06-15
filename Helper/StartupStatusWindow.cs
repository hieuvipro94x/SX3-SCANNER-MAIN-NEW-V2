using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SX3_SCANER.Helper
{
    internal sealed class StartupStatusWindow : Window
    {
        private readonly TextBlock _statusText;

        internal StartupStatusWindow()
        {
            Title = "SX3 Scanner";
            Width = 520;
            Height = 220;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = true;

            var root = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(28),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(248, 250, 252), 0),
                        new GradientStop(Color.FromRgb(226, 232, 240), 1)
                    }
                },
                Effect = new DropShadowEffect
                {
                    BlurRadius = 28,
                    ShadowDepth = 0,
                    Opacity = 0.25,
                    Color = Colors.Black
                }
            };

            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 18)
            };

            var logo = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(14),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(37, 99, 235), 0),
                        new GradientStop(Color.FromRgb(14, 165, 233), 1)
                    }
                },
                Child = new TextBlock
                {
                    Text = "SX3",
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var titlePanel = new StackPanel
            {
                Margin = new Thickness(14, 2, 0, 0)
            };

            titlePanel.Children.Add(new TextBlock
            {
                Text = "SX3 SCANNER",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            });

            titlePanel.Children.Add(new TextBlock
            {
                Text = "Đang khởi động hệ thống...",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(1, 3, 0, 0)
            });

            header.Children.Add(logo);
            header.Children.Add(titlePanel);

            _statusText = new TextBlock
            {
                Text = StartupManager.CurrentStatus,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };

            var progressBackground = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Child = new Border
                {
                    Width = 190,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(8),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 0),
                        GradientStops =
                        {
                            new GradientStop(Color.FromRgb(37, 99, 235), 0),
                            new GradientStop(Color.FromRgb(14, 165, 233), 1)
                        }
                    }
                }
            };

            panel.Children.Add(header);
            panel.Children.Add(_statusText);
            panel.Children.Add(progressBackground);

            root.Child = panel;
            Content = root;

            StartupManager.StatusChanged += OnStatusChanged;
            Closed += OnClosed;
        }

        private void OnStatusChanged(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnStatusChanged(message)));
                return;
            }

            _statusText.Text = string.IsNullOrWhiteSpace(message)
                ? "Đang khởi động..."
                : message;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            StartupManager.StatusChanged -= OnStatusChanged;
        }
    }
}