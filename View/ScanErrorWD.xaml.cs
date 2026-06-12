using System.Windows;

namespace SX3_SCANER.View
{
    public partial class ScanErrorWD : Window
    {
        public ScanErrorWD(
            string detail,
            string standard,
            string actual,
            string resolution)
        {
            InitializeComponent();
            DetailText.Text = detail ?? string.Empty;
            StandardText.Text = standard ?? string.Empty;
            ActualText.Text = actual ?? string.Empty;
            ResolutionText.Text = resolution ?? string.Empty;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
