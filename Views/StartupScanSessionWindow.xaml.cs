using SX3_SCANER.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SX3_SCANER.Views
{
    public partial class StartupScanSessionWindow : Window
    {
        public StartupScanSessionWindow()
        {
            InitializeComponent();
            Loaded += StartupScanSessionWindow_Loaded;
        }

        private void StartupScanSessionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ProductComboBox.Focus();
                Keyboard.Focus(ProductComboBox);
            }
            catch
            {
            }
        }

        private void Begin_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel vm = DataContext as MainViewModel;
            if (vm == null)
            {
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không lấy được dữ liệu phiên quét.",
                    "SX3 SCANER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!vm.BeginStartupScanSession())
                return;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
