using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SX3_SCANER.Helper
{
    internal sealed class StartupStatusWindow : Window
    {
        private readonly TextBlock _statusText;
        private readonly Border _statusBadge;
        private readonly TranslateTransform _loadingBarTransform;
        private readonly TranslateTransform _shineTransform;
        private readonly ScaleTransform _logoScaleTransform;
        private readonly Border _logoGlow;

        internal StartupStatusWindow()
        {
            Title = "SX3 Scanner";
            Width = 620;
            Height = 290;
            MinWidth = 620;
            MinHeight = 290;
            MaxWidth = 620;
            MaxHeight = 290;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
            SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);

            MouseLeftButtonDown += (_, __) =>
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore drag exceptions during startup.
                }
            };

            var root = new Border
            {
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(96, 165, 250), 0),
                        new GradientStop(Color.FromRgb(34, 197, 94), 1)
                    }
                },
                Effect = new DropShadowEffect
                {
                    BlurRadius = 34,
                    ShadowDepth = 0,
                    Opacity = 0.28,
                    Color = Color.FromRgb(15, 23, 42)
                }
            };

            var shell = new Border
            {
                CornerRadius = new CornerRadius(23),
                Padding = new Thickness(26, 24, 26, 22),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(255, 255, 255), 0),
                        new GradientStop(Color.FromRgb(248, 250, 252), 0.58),
                        new GradientStop(Color.FromRgb(239, 246, 255), 1)
                    }
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logoRoot = new Grid
            {
                Width = 62,
                Height = 62,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            _logoScaleTransform = new ScaleTransform(1, 1);
            logoRoot.RenderTransform = _logoScaleTransform;

            _logoGlow = new Border
            {
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(45, 37, 99, 235)),
                Margin = new Thickness(-5),
                Opacity = 0.75,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.35,
                    Color = Color.FromRgb(37, 99, 235)
                }
            };

            var logo = new Border
            {
                Width = 62,
                Height = 62,
                CornerRadius = new CornerRadius(20),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(29, 78, 216), 0),
                        new GradientStop(Color.FromRgb(14, 165, 233), 0.55),
                        new GradientStop(Color.FromRgb(34, 197, 94), 1)
                    }
                },
                Child = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "SX3",
                            Foreground = Brushes.White,
                            FontSize = 17,
                            FontWeight = FontWeights.Black,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            logoRoot.Children.Add(_logoGlow);
            logoRoot.Children.Add(logo);
            header.Children.Add(logoRoot);

            var titlePanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titlePanel, 2);

            titlePanel.Children.Add(new TextBlock
            {
                Text = "SX3 SCANNER",
                FontSize = 26,
                FontWeight = FontWeights.Black,
                Foreground = Brush(15, 23, 42),
                LineHeight = 30
            });

            titlePanel.Children.Add(new TextBlock
            {
                Text = "Hệ thống kiểm tra mã QR sản xuất",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(71, 85, 105),
                Margin = new Thickness(1, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            header.Children.Add(titlePanel);

            _statusBadge = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 7, 12, 7),
                Background = Brush(239, 246, 255),
                BorderBrush = Brush(191, 219, 254),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "ĐANG MỞ",
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    Foreground = Brush(29, 78, 216)
                }
            };
            Grid.SetColumn(_statusBadge, 3);
            header.Children.Add(_statusBadge);

            mainGrid.Children.Add(header);

            var statusCard = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = Brush(248, 250, 252),
                BorderBrush = Brush(226, 232, 240),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconCircle = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(19),
                Background = Brush(219, 234, 254),
                BorderBrush = Brush(147, 197, 253),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "●",
                    FontSize = 16,
                    FontWeight = FontWeights.Black,
                    Foreground = Brush(37, 99, 235),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            statusGrid.Children.Add(iconCircle);

            var statusTextPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusTextPanel, 2);

            statusTextPanel.Children.Add(new TextBlock
            {
                Text = "TRẠNG THÁI KHỞI ĐỘNG",
                FontSize = 11,
                FontWeight = FontWeights.Black,
                Foreground = Brush(100, 116, 139),
                Margin = new Thickness(0, 0, 0, 4)
            });

            _statusText = new TextBlock
            {
                Text = SafeStatusText(StartupManager.CurrentStatus),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(30, 41, 59),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                ToolTip = SafeStatusText(StartupManager.CurrentStatus)
            };
            statusTextPanel.Children.Add(_statusText);

            statusGrid.Children.Add(statusTextPanel);
            statusCard.Child = statusGrid;
            Grid.SetRow(statusCard, 2);
            mainGrid.Children.Add(statusCard);

            var progressSection = new StackPanel();
            Grid.SetRow(progressSection, 4);

            var progressHeader = new Grid
            {
                Margin = new Thickness(1, 0, 1, 8)
            };
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            progressHeader.Children.Add(new TextBlock
            {
                Text = "Đang nạp dữ liệu và kiểm tra kết nối",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(71, 85, 105),
                TextWrapping = TextWrapping.Wrap
            });

            var percentText = new TextBlock
            {
                Text = "VUI LÒNG CHỜ...",
                FontSize = 11,
                FontWeight = FontWeights.Black,
                Foreground = Brush(37, 99, 235),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(percentText, 1);
            progressHeader.Children.Add(percentText);
            progressSection.Children.Add(progressHeader);

            var progressTrack = new Border
            {
                Height = 12,
                CornerRadius = new CornerRadius(8),
                Background = Brush(226, 232, 240),
                BorderBrush = Brush(203, 213, 225),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };

            var progressGrid = new Grid();
            progressGrid.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(18, 37, 99, 235), 0),
                        new GradientStop(Color.FromArgb(30, 34, 197, 94), 1)
                    }
                }
            });

            _loadingBarTransform = new TranslateTransform(-260, 0);
            var loadingBar = new Border
            {
                Width = 250,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                RenderTransform = _loadingBarTransform,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 37, 99, 235), 0),
                        new GradientStop(Color.FromRgb(37, 99, 235), 0.25),
                        new GradientStop(Color.FromRgb(14, 165, 233), 0.55),
                        new GradientStop(Color.FromRgb(34, 197, 94), 0.82),
                        new GradientStop(Color.FromArgb(0, 34, 197, 94), 1)
                    }
                }
            };
            progressGrid.Children.Add(loadingBar);

            _shineTransform = new TranslateTransform(-140, 0);
            var shine = new Rectangle
            {
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                Opacity = 0.45,
                RenderTransform = _shineTransform,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(180, 255, 255, 255), 0.5),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                    }
                }
            };
            progressGrid.Children.Add(shine);

            progressTrack.Child = progressGrid;
            progressSection.Children.Add(progressTrack);

            progressSection.Children.Add(new TextBlock
            {
                Text = "Vui lòng chờ trong giây lát, không tắt ứng dụng khi đang khởi động.",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(100, 116, 139),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(1, 9, 1, 0)
            });

            mainGrid.Children.Add(progressSection);

            shell.Child = mainGrid;
            root.Child = shell;
            Content = root;

            Loaded += OnLoaded;
            StartupManager.StatusChanged += OnStatusChanged;
            Closed += OnClosed;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static string SafeStatusText(string message)
        {
            return string.IsNullOrWhiteSpace(message)
                ? "Đang khởi động hệ thống..."
                : message.Trim();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartAnimations();
        }

        private void StartAnimations()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            _loadingBarTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = -270,
                    To = 620,
                    Duration = TimeSpan.FromSeconds(1.55),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });

            _shineTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = -160,
                    To = 640,
                    Duration = TimeSpan.FromSeconds(1.95),
                    BeginTime = TimeSpan.FromSeconds(0.15),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });

            _logoGlow.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation
                {
                    From = 0.45,
                    To = 0.95,
                    Duration = TimeSpan.FromSeconds(1.1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });

            _logoScaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation
                {
                    From = 0.98,
                    To = 1.03,
                    Duration = TimeSpan.FromSeconds(1.1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });

            _logoScaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation
                {
                    From = 0.98,
                    To = 1.03,
                    Duration = TimeSpan.FromSeconds(1.1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });
        }

        private void OnStatusChanged(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnStatusChanged(message)));
                return;
            }

            var text = SafeStatusText(message);
            _statusText.Text = text;
            _statusText.ToolTip = text;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            StartupManager.StatusChanged -= OnStatusChanged;

            _loadingBarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _shineTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _logoGlow.BeginAnimation(UIElement.OpacityProperty, null);
            _logoScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _logoScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }
}
