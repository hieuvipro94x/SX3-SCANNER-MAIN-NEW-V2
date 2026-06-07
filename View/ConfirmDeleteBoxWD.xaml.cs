using System.Windows;

namespace SX3_SCANER.View
{
    public partial class ConfirmDeleteBoxWD : Window
    {
        public ConfirmDeleteBoxWD()
        {
            InitializeComponent();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}