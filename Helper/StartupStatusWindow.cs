using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SX3_SCANER.Helper
{
    internal sealed class StartupStatusWindow : Window
    {
        private readonly TextBlock _statusText;

        internal StartupStatusWindow()
        {
            Title = "SX3 Scanner";
            Width = 460;
            Height = 150;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = true;
            Background = Brushes.White;

            var panel = new StackPanel
            {
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = "SX3 SCANNER",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(21, 101, 192)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            _statusText = new TextBlock
            {
                Text = StartupManager.CurrentStatus,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                TextWrapping = TextWrapping.Wrap
            };

            panel.Children.Add(_statusText);
            Content = panel;

            StartupManager.StatusChanged += OnStatusChanged;
            Closed += OnClosed;
        }

        private void OnStatusChanged(string message)
        {
            Dispatcher.BeginInvoke(new Action(() => _statusText.Text = message));
        }

        private void OnClosed(object sender, EventArgs e)
        {
            StartupManager.StatusChanged -= OnStatusChanged;
        }
    }
}
