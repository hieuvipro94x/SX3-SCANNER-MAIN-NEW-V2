using SX3_SCANER.Helper;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SX3_SCANER.Views.Controls
{
    public partial class ScanLeftPanel : UserControl
    {
        private const string DateTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly DispatcherTimer _clockTimer;
        private readonly string _applicationVersion;
        private bool _isTimerRunning;

        public ScanLeftPanel()
        {
            InitializeComponent();

            _applicationVersion = UpdateService.GetCurrentVersionString();

            _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _clockTimer.Tick += ClockTimer_Tick;

            Loaded += ScanLeftPanel_Loaded;
            Unloaded += ScanLeftPanel_Unloaded;

            PrepareTextElements();
            UpdateClock();
        }

        private void ScanLeftPanel_Loaded(object sender, RoutedEventArgs e)
        {
            PrepareTextElements();
            UpdateClock();

            if (_isTimerRunning)
            {
                return;
            }

            _clockTimer.Start();
            _isTimerRunning = true;
        }

        private void ScanLeftPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isTimerRunning)
            {
                return;
            }

            _clockTimer.Stop();
            _isTimerRunning = false;
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            UpdateClock();
        }

        private void UpdateClock()
        {
            SetReadableText(txtAppVersion, $"SCANNER V{_applicationVersion}");
            SetReadableText(txtDateTimeVersion, DateTime.Now.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
        }

        private void PrepareTextElements()
        {
            MakeTextReadable(txtAppVersion);
            MakeTextReadable(txtDateTimeVersion);
        }

        /// <summary>
        /// Gán text và luôn giữ full nội dung trong ToolTip để người dùng rê chuột vào là xem đủ.
        /// Đồng thời bật Wrap/không cắt chữ với TextBlock để hạn chế mất văn bản trên giao diện.
        /// </summary>
        private static void SetReadableText(object target, string text)
        {
            if (target == null)
            {
                return;
            }

            switch (target)
            {
                case TextBlock textBlock:
                    textBlock.Text = text;
                    textBlock.ToolTip = text;
                    MakeTextReadable(textBlock);
                    break;

                case TextBox textBox:
                    textBox.Text = text;
                    textBox.ToolTip = text;
                    MakeTextReadable(textBox);
                    break;

                case ContentControl contentControl:
                    contentControl.Content = text;
                    contentControl.ToolTip = text;
                    break;
            }
        }

        /// <summary>
        /// Tối ưu hiển thị chữ: cho phép xuống dòng, không trim chữ, chống nhòe font.
        /// </summary>
        private static void MakeTextReadable(object target)
        {
            if (target == null)
            {
                return;
            }

            switch (target)
            {
                case TextBlock textBlock:
                    textBlock.TextWrapping = TextWrapping.Wrap;
                    textBlock.TextTrimming = TextTrimming.None;
                    textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    textBlock.LineHeight = Math.Max(textBlock.FontSize + 4, 18);
                    textBlock.SnapsToDevicePixels = true;
                    RenderOptions.SetClearTypeHint(textBlock, ClearTypeHint.Enabled);
                    break;

                case TextBox textBox:
                    textBox.TextWrapping = TextWrapping.Wrap;
                    textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    textBox.SnapsToDevicePixels = true;
                    RenderOptions.SetClearTypeHint(textBox, ClearTypeHint.Enabled);
                    break;

                case FrameworkElement frameworkElement:
                    frameworkElement.SnapsToDevicePixels = true;
                    break;
            }
        }
    }
}
