using SX3_SCANER.Helper;
using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SX3_SCANER.Views.Controls
{
    public partial class ScanLeftPanel : UserControl
    {
        private readonly DispatcherTimer _clockTimer = new DispatcherTimer();
        private readonly string _applicationVersion;

        public ScanLeftPanel()
        {
            InitializeComponent();

            _applicationVersion = UpdateService.GetCurrentVersionString();

            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += ClockTimer_Tick;

            Loaded += ScanLeftPanel_Loaded;
            Unloaded += ScanLeftPanel_Unloaded;

            UpdateClock();
        }

        private void ScanLeftPanel_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateClock();
            _clockTimer.Start();
        }

        private void ScanLeftPanel_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _clockTimer.Stop();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            UpdateClock();
        }

        private void UpdateClock()
        {
            if (txtAppVersion != null)
            {
                txtAppVersion.Text = "SCANER V" + _applicationVersion;
            }

            if (txtDateTimeVersion != null)
            {
                txtDateTimeVersion.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            }
        }
    }
}
