using System.Windows.Controls;

namespace SX3_SCANER.Views.Controls
{
    public partial class HistorySearchTab : UserControl
    {
        public HistorySearchTab()
        {
            InitializeComponent();
        }

        private void SQLiteTable_LoadingRow(
            object sender,
            DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}
