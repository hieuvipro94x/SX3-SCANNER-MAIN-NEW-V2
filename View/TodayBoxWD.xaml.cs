using SX3_SCANER.Model;
using System.Windows;
using System.Windows.Controls;

namespace SX3_SCANER.View
{
    public partial class TodayBoxWD : Window
    {
        public TodayBoxWD()
        {
            InitializeComponent();
        }

        // Đánh lại số thứ tự hiển thị cho bảng TodayBoxWD.
        // RowIndex chỉ dùng để hiển thị: 1, 2, 3... theo thứ tự dòng hiện tại.
        // ID thật trong database vẫn giữ nguyên để tránh lỗi khóa chính SQLite.
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is ScanHistory scanHistory)
            {
                scanHistory.RowIndex = e.Row.GetIndex() + 1;
            }

        }
    }
}
