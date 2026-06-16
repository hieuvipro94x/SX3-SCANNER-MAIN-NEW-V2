using System.Windows.Controls;

namespace SX3_SCANER.Views.Controls
{
    public partial class ScanInputHistoryPanel : UserControl
    {
        public ScanInputHistoryPanel()
        {
            InitializeComponent();
        }

        private void HideRowIndex_AutoGeneratingColumn(
            object sender,
            DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "RowIndex" || e.PropertyName == "ID")
            {
                e.Cancel = true;
                return;
            }

            if (e.PropertyName == "ScanTime")
                e.Column.Width = 150;

            if (e.PropertyName == "BoxName")
                e.Column.Width = 150;

            if (e.PropertyName == "ProductPartNumber")
                e.Column.Width = 150;

            if (e.PropertyName == "ProductPartName")
                e.Column.Width = 160;

            if (e.PropertyName == "SealNo")
                e.Column.Width = 100;

            if (e.PropertyName == "LotNo")
                e.Column.Width = 100;

            if (e.PropertyName == "ScanData")
                e.Column.Width = 260;

            if (e.PropertyName == "ScanMessage")
                e.Column.Width = 260;

            if (e.PropertyName == "ScanWorker")
                e.Column.Width = 120;

            if (e.PropertyName == "ResultText")
                e.Column.Width = 100;
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}
