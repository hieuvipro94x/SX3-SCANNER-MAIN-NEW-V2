
using SX3_SCANER.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private int _CURR_ID;

        public int CURR_ID
        {
            get { return _CURR_ID; }
            set { _CURR_ID = value; OnPropertyChanged(); }
        }
        private string _CURR_CAR;

        public string CURR_CAR
        {
            get { return _CURR_CAR; }
            set { _CURR_CAR = value; OnPropertyChanged(); }
        }
        private string _CURR_PARTNUMBER = "WH";

        public string CURR_PARTNUMBER
        {
            get { return _CURR_PARTNUMBER; }
            set { _CURR_PARTNUMBER = value; OnPropertyChanged(); }
        }

        private string _CURR_PARTNAME;

        public string CURR_PARTNAME
        {
            get { return _CURR_PARTNAME; }
            set { _CURR_PARTNAME = value; OnPropertyChanged(); UpdateStringSample(); }
        }
        private string _CURR_PREFIX;

        public string CURR_PREFIX
        {
            get { return _CURR_PREFIX; }
            set { _CURR_PREFIX = value; OnPropertyChanged(); UpdateStringSample(); }
        }
        private string _CURR_SUFFIX;

        public string CURR_SUFFIX
        {
            get { return _CURR_SUFFIX; }
            set { _CURR_SUFFIX = value; OnPropertyChanged(); UpdateStringSample(); }
        }
        private string _CURR_SAMPLE;

        public string CURR_SAMPLE
        {
            get { return _CURR_SAMPLE; }
            set { _CURR_SAMPLE = value; CURR_LENGTH = CURR_SAMPLE.Length; OnPropertyChanged(); }
        }

        private void UpdateStringSample()
        {
            CURR_SAMPLE = $"{CURR_PREFIX ?? string.Empty}{CURR_PARTNAME ?? string.Empty}yyMMdd2###{CURR_SUFFIX ?? string.Empty}";

        }
        private int _CURR_LENGTH;

        public int CURR_LENGTH
        {
            get { return _CURR_LENGTH; }
            set { _CURR_LENGTH = value; OnPropertyChanged(); }
        }
        private int _CURR_QTY = 10;

        public int CURR_QTY
        {
            get { return _CURR_QTY; }
            set { _CURR_QTY = value; OnPropertyChanged(); }
        }


        private ICommand _DELETECMD;

        public ICommand DELETECMD
        {
            get
            {
                if (_DELETECMD == null)
                {
                    _DELETECMD = new RelayCommand<object>(o => CURR_ID > 0 && AdminCRUD, o =>
                    {
                        if (CURR_ID != 0)
                        {
                            new LabelProductInfoRepository().DeleteLabelProductInfo(CURR_ID);

                            SearchProductInfo();

                            MessageBox.Show("Xóa thành công");
                        }
                    });
                }
                return _DELETECMD;
            }
        }

        private ICommand _ADDCMD;

        public ICommand ADDCMD
        {
            get
            {
                if (_ADDCMD == null)
                {
                    _ADDCMD = new RelayCommand<object>(o => CancelAddNew(), o =>
                    {
                        LabelProductInfo labelProductInfo = new LabelProductInfo
                        {
                            Car = CURR_CAR,
                            PartNumber = CURR_PARTNUMBER,
                            PartName = CURR_PARTNAME,
                            CodeStringForm = CURR_SAMPLE,
                            CodePrefix = CURR_PREFIX ?? string.Empty,
                            CodeSuffix = CURR_SUFFIX ?? string.Empty,
                            CodeLength = CURR_LENGTH,
                            BoxQuantity = CURR_QTY

                        };

                        var rep = new LabelProductInfoRepository();

                        if (rep.CheckIfExist(CURR_PARTNAME, CURR_PARTNUMBER))
                        {
                            MessageBox.Show("PartName||PartNo đã tồn tại trong hệ thống");
                            return;
                        }
                        rep.INSERTLabelProductInfo(labelProductInfo);
                        LabelProductInfoSource = new LabelProductInfoRepository().GetAllLabelProductInfo();
                        MessageBox.Show("Thêm mới thành công");
                        CURR_CAR = string.Empty;
                        CURR_PARTNUMBER = "WH";
                        CURR_PARTNAME = string.Empty;
                        CURR_PREFIX = string.Empty;
                        CURR_SUFFIX = string.Empty;
                        CURR_SAMPLE = string.Empty;
                        CURR_LENGTH = 0;

                    });
                }
                return _ADDCMD;
            }
        }

        private bool CancelAddNew()
        {
            if (string.IsNullOrEmpty(CURR_CAR)) return false;
            if (string.IsNullOrEmpty(CURR_PARTNAME)) return false;
            return !string.IsNullOrEmpty(CURR_SAMPLE) && AdminCRUD;
        }

        private ICommand _MODIFYCMD;

        public ICommand MODIFYCMD
        {
            get
            {
                if (_MODIFYCMD == null)
                {
                    _MODIFYCMD = new RelayCommand<object>(o => CancelAddNew() && CURR_ID > 0, o =>
                    {
                        LabelProductInfo labelProductInfo = new LabelProductInfo
                        {
                            ID = CURR_ID,
                            Car = CURR_CAR,
                            PartNumber = CURR_PARTNUMBER,
                            PartName = CURR_PARTNAME,
                            CodeStringForm = CURR_SAMPLE,
                            CodePrefix = CURR_PREFIX ?? string.Empty,
                            CodeSuffix = CURR_SUFFIX ?? string.Empty,
                            CodeLength = CURR_LENGTH,
                            BoxQuantity = CURR_QTY
                        };
                        if (new LabelProductInfoRepository().CheckIfExist(CURR_PARTNAME, CURR_PARTNUMBER, CURR_ID))
                        {
                            MessageBox.Show("PartName||PartNo đã tồn tại trong hệ thống");
                            return;
                        }
                        new LabelProductInfoRepository().UpdateLabelProductInfo(labelProductInfo);
                        SearchProductInfo();
                    });
                }
                return _MODIFYCMD;
            }
        }

        private string _InputProductNameSearch;

        public string InputProductNameSearch
        {
            get { return _InputProductNameSearch; }
            set { _InputProductNameSearch = value; OnPropertyChanged(); }
        }

        private ObservableCollection<LabelProductInfo> _LabelProductInfoSource;

        public ObservableCollection<LabelProductInfo> LabelProductInfoSource
        {
            get { return _LabelProductInfoSource; }
            set { if (null == value) return; _LabelProductInfoSource = value; LabelProductInfoView = CollectionViewSource.GetDefaultView(value); }
        }

        private ICollectionView _LabelProductInfoView;

        public ICollectionView LabelProductInfoView
        {
            get { return _LabelProductInfoView; }
            set { _LabelProductInfoView = value; OnPropertyChanged(); }
        }


        private ICommand _SeachProductInfoCMD;

        public ICommand SeachProductInfoCMD
        {
            get
            {
                if (_SeachProductInfoCMD == null)
                {
                    _SeachProductInfoCMD = new RelayCommand<object>(o => true, o =>
                    {
                        SearchProductInfo();
                    });
                }
                return _SeachProductInfoCMD;
            }
        }

        private void SearchProductInfo()
        {
            LabelProductInfoSource = new LabelProductInfoRepository().GetAllLabelProductInfo();
            if (string.IsNullOrEmpty(InputProductNameSearch))
            {
                LabelProductInfoView.Filter = null;
            }
            else
            {
                LabelProductInfoView.Filter = item =>
                {
                    var labelProductInfo = item as LabelProductInfo;
                    return labelProductInfo != null && labelProductInfo.PartName != null && labelProductInfo.PartName.IndexOf(InputProductNameSearch, System.StringComparison.OrdinalIgnoreCase) >= 0;
                };
            }
        }

        private LabelProductInfo _SelectedProductInfoToModify;

        public LabelProductInfo SelectedProductInfoToModify
        {
            get { return _SelectedProductInfoToModify; }
            set
            {
                if (value == null || value == _SelectedProductInfoToModify) return;
                _SelectedProductInfoToModify = value;

                CURR_ID = _SelectedProductInfoToModify.ID;
                CURR_CAR = _SelectedProductInfoToModify.Car;
                CURR_PARTNUMBER = _SelectedProductInfoToModify.PartNumber;
                CURR_PARTNAME = _SelectedProductInfoToModify.PartName;
                CURR_PREFIX = _SelectedProductInfoToModify.CodePrefix;
                CURR_SUFFIX = _SelectedProductInfoToModify.CodeSuffix;
                CURR_SAMPLE = _SelectedProductInfoToModify.CodeStringForm;
                CURR_LENGTH = _SelectedProductInfoToModify.CodeLength;
                CURR_QTY = _SelectedProductInfoToModify.BoxQuantity;
            }
        }
    }
}
