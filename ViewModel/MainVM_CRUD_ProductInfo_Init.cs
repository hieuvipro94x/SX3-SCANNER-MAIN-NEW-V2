
using SX3_SCANER.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
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
            set
            {
                _CURR_SAMPLE = value ?? string.Empty;
                CURR_LENGTH = _CURR_SAMPLE.Length;
                OnPropertyChanged();
            }
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

                            SX3_SCANER.Helper.ProfessionalMessageBox.Show("Xóa thành công");
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
                    _ADDCMD = new RelayCommand<object>(o => AdminCRUD, o =>
                    {
                        string validationError;
                        LabelProductInfo labelProductInfo;
                        if (!TryBuildProductInfoForSave(0, out labelProductInfo, out validationError))
                        {
                            SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                                validationError,
                                "Dữ liệu mã hàng chưa hợp lệ",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        var rep = new LabelProductInfoRepository();

                        if (rep.CheckIfExist(labelProductInfo.PartName, labelProductInfo.PartNumber))
                        {
                            SX3_SCANER.Helper.ProfessionalMessageBox.Show("PartName||PartNo đã tồn tại trong hệ thống");
                            return;
                        }
                        rep.INSERTLabelProductInfo(labelProductInfo);
                        LabelProductInfoSource = new LabelProductInfoRepository().GetAllLabelProductInfo();
                        SX3_SCANER.Helper.ProfessionalMessageBox.Show("Thêm mới thành công");
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
                    _MODIFYCMD = new RelayCommand<object>(o => AdminCRUD && CURR_ID > 0, o =>
                    {
                        string validationError;
                        LabelProductInfo labelProductInfo;
                        if (!TryBuildProductInfoForSave(CURR_ID, out labelProductInfo, out validationError))
                        {
                            SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                                validationError,
                                "Dữ liệu mã hàng chưa hợp lệ",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        if (new LabelProductInfoRepository().CheckIfExist(labelProductInfo.PartName, labelProductInfo.PartNumber, CURR_ID))
                        {
                            SX3_SCANER.Helper.ProfessionalMessageBox.Show("PartName||PartNo đã tồn tại trong hệ thống");
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
            set
            {
                if (_InputProductNameSearch == value) return;
                _InputProductNameSearch = value;
                OnPropertyChanged();
                ApplyProductInfoFilter();
            }
        }

        private ObservableCollection<LabelProductInfo> _LabelProductInfoSource;

        public ObservableCollection<LabelProductInfo> LabelProductInfoSource
        {
            get { return _LabelProductInfoSource; }
            set
            {
                _LabelProductInfoSource = value ?? new ObservableCollection<LabelProductInfo>();
                LabelProductInfoView = CollectionViewSource.GetDefaultView(_LabelProductInfoSource);
                ApplyProductInfoFilter();
                OnPropertyChanged();
            }
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
        }

        private void ApplyProductInfoFilter()
        {
            if (LabelProductInfoView == null) return;

            string keyword = InputProductNameSearch;
            keyword = keyword == null ? string.Empty : keyword.Trim();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                LabelProductInfoView.Filter = null;
                LabelProductInfoView.Refresh();
                return;
            }

            LabelProductInfoView.Filter = item =>
            {
                if (!(item is LabelProductInfo labelProductInfo))
                    return false;

                return ContainsIgnoreCase(labelProductInfo.ID.ToString(), keyword)
                    || ContainsIgnoreCase(labelProductInfo.Car, keyword)
                    || ContainsIgnoreCase(labelProductInfo.PartNumber, keyword)
                    || ContainsIgnoreCase(labelProductInfo.PartName, keyword)
                    || ContainsIgnoreCase(labelProductInfo.CodeStringForm, keyword)
                    || ContainsIgnoreCase(labelProductInfo.CodePrefix, keyword)
                    || ContainsIgnoreCase(labelProductInfo.CodeSuffix, keyword)
                    || ContainsIgnoreCase(labelProductInfo.CodeLength.ToString(), keyword)
                    || ContainsIgnoreCase(labelProductInfo.BoxQuantity.ToString(), keyword);
            };

            LabelProductInfoView.Refresh();
        }

        private static bool ContainsIgnoreCase(string source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            return source.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryBuildProductInfoForSave(
            int id,
            out LabelProductInfo labelProductInfo,
            out string validationError)
        {
            string car = TrimField(CURR_CAR);
            string partNumber = TrimField(CURR_PARTNUMBER);
            string partName = TrimField(CURR_PARTNAME);
            string prefix = TrimField(CURR_PREFIX);
            string suffix = TrimField(CURR_SUFFIX);
            string sample = TrimField(CURR_SAMPLE);

            var errors = new StringBuilder();
            AppendRequiredError(errors, car, "CAR");
            AppendRequiredError(errors, partNumber, "PART NUMBER");
            AppendRequiredError(errors, partName, "PART NAME");
            AppendRequiredError(errors, sample, "CODE FORMAT");

            if (CURR_LENGTH <= 0)
            {
                errors.AppendLine("- LENGTH phải lớn hơn 0.");
            }

            if (CURR_QTY <= 0)
            {
                errors.AppendLine("- BOX QTY phải lớn hơn 0.");
            }

            if (!IsCodeToken(partNumber))
            {
                errors.AppendLine("- PART NUMBER chỉ được chứa chữ, số và các ký tự - _ . / #.");
            }

            if (!string.IsNullOrEmpty(prefix) && !IsCodeToken(prefix))
            {
                errors.AppendLine("- PREFIX chỉ được chứa chữ, số và các ký tự - _ . / #.");
            }

            if (!string.IsNullOrEmpty(suffix) && !IsCodeToken(suffix))
            {
                errors.AppendLine("- SUFFIX chỉ được chứa chữ, số và các ký tự - _ . / #.");
            }

            validationError = errors.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(validationError))
            {
                labelProductInfo = null;
                return false;
            }

            labelProductInfo = new LabelProductInfo
            {
                ID = id,
                Car = car,
                PartNumber = partNumber,
                PartName = partName,
                CodeStringForm = sample,
                CodePrefix = prefix,
                CodeSuffix = suffix,
                CodeLength = CURR_LENGTH,
                BoxQuantity = CURR_QTY
            };

            return true;
        }

        private static string TrimField(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static void AppendRequiredError(
            StringBuilder errors,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.AppendLine("- " + label + " không được để trống.");
            }
        }

        private static bool IsCodeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) ||
                    c == '-' ||
                    c == '_' ||
                    c == '.' ||
                    c == '/' ||
                    c == '#')
                {
                    continue;
                }

                return false;
            }

            return true;
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
