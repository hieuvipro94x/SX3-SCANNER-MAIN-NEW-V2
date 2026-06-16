using SX3_SCANER.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private string _BoxNameForFilter;

        public string BoxNameForFilter
        {
            get { return _BoxNameForFilter; }
            set
            {
                _BoxNameForFilter = value;
                OnPropertyChanged();
                ApplyFilter();

            }
        }

        private void ApplyFilter()
        {
            if (ToDayBoxView != null)
            {
                if (string.IsNullOrEmpty(BoxNameForFilter))
                {
                    // Nếu BoxNameForFilter trống, loại bỏ bộ lọc
                    ToDayBoxView.Filter = null;
                }
                else
                {
                    // Áp dụng bộ lọc
                    ToDayBoxView.Filter = item =>
                    {
                        BoxProduct box = item as BoxProduct;
                        if (box != null)
                        {
                            // Kiểm tra xem BoxName có chứa BoxNameForFilter không
                            return !string.IsNullOrWhiteSpace(box.BoxName) && box.BoxName.IndexOf(BoxNameForFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        return false;
                    };
                }

                ApplyTodayBoxSort();
            }
        }

        private void ApplyTodayBoxSort()
        {
            if (ToDayBoxView == null) return;

            using (ToDayBoxView.DeferRefresh())
            {
                ToDayBoxView.SortDescriptions.Clear();
                ToDayBoxView.SortDescriptions.Add(new SortDescription(nameof(BoxProduct.BoxComplete), ListSortDirection.Descending));
                ToDayBoxView.SortDescriptions.Add(new SortDescription(nameof(BoxProduct.BoxName), ListSortDirection.Descending));
            }
        }

        private ObservableCollection<BoxProduct> _ToDayBoxSource;

        public ObservableCollection<BoxProduct> ToDayBoxSource
        {
            get { return _ToDayBoxSource; }
            set
            {
                _ToDayBoxSource = value;
                SubscribeDashboardTodayBox(value);
                ToDayBoxView = value == null ? null : CollectionViewSource.GetDefaultView(value);
                ApplyFilter();
                ApplyTodayBoxSort();
                OnPropertyChanged();
            }
        }

        private ICollectionView _ToDayBoxView;

        public ICollectionView ToDayBoxView
        {
            get { return _ToDayBoxView; }
            set { _ToDayBoxView = value; OnPropertyChanged(); }
        }

        private BoxProduct _SelectedTodayBox;

        public BoxProduct SelectedTodayBox
        {
            get { return _SelectedTodayBox; }
            set { _SelectedTodayBox = value; OnPropertyChanged(); }
        }


    }
}
