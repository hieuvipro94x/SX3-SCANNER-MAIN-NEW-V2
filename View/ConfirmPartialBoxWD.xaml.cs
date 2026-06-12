using System.Windows;

namespace SX3_SCANER.View
{
    public partial class ConfirmPartialBoxWD : Window
    {
        public bool IsConfirmed { get; private set; }

        public ConfirmPartialBoxWD(int currentQuantity, int targetQuantity)
        {
            InitializeComponent();
            IsConfirmed = false;
            txtQuantity.Text = currentQuantity + "/" + targetQuantity;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
