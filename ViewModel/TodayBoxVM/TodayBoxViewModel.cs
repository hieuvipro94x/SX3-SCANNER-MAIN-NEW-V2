using SX3_SCANER.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SX3_SCANER.ViewModel.TodayBoxVM
{
    internal class TodayBoxViewModel : ViewModelBase
    {
        private ObservableCollection<ScanHistory> _DataSource;

        public ObservableCollection<ScanHistory> DataSource
        {
            get { return _DataSource; }
            set { _DataSource = value; DataView = value == null ? null : CollectionViewSource.GetDefaultView(value); OnPropertyChanged(); }
        }

        private ICollectionView _DataView;

        public ICollectionView DataView
        {
            get { return _DataView; }
            set { _DataView = value; OnPropertyChanged(); }
        }



        public TodayBoxViewModel(string boxname)
        {
            DataSource = new ScanHistoryRepository().GetByBoxName(boxname);
        }
    }
}
