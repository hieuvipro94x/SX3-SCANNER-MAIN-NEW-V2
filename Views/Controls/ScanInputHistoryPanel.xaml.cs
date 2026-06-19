using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SX3_SCANER.Views.Controls
{
    public partial class ScanInputHistoryPanel : UserControl
    {
        public ScanInputHistoryPanel()
        {
            InitializeComponent();
            Loaded += ScanInputHistoryPanel_Loaded;
        }

        private void ScanInputHistoryPanel_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TbxInputCode.Focus();
                Keyboard.Focus(TbxInputCode);
            }
            catch
            {
            }
        }
    }
}
