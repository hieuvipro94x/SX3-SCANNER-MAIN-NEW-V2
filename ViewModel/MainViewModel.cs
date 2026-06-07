using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using SX3_SCANER.View;
using System.Windows.Controls;
using System.Windows.Input;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        public enum JobIndex
        {
            SCAN = 0,
            CRUD = 1,
            QUERY = 2
        }


        private bool _AdminCRUD;

        public bool AdminCRUD
        {
            get { return _AdminCRUD; }
            set { _AdminCRUD = value; OnPropertyChanged(); }
        }

        private int _TabControlSelectedIndex;

        public int TabControlSelectedIndex
        {
            get { return _TabControlSelectedIndex; }
            set
            {
                if (_TabControlSelectedIndex == value) return;
                _TabControlSelectedIndex = value;
                OnPropertyChanged();
                if (value == (int)JobIndex.SCAN)
                {
                    PartNumberList = new LabelProductInfoRepository().GetAllPartNumber();
                }
                else if (value == (int)JobIndex.CRUD)
                {
                    AdminCRUD = false;
                    AskPasswordWD askPasswordWD = new AskPasswordWD();
                    bool? result = askPasswordWD.ShowDialog();

                    if (result == true)
                    {
                        SearchProductInfo();
                        AdminCRUD = true;
                        return;
                    }

                    AdminCRUD = false;
                    TabControlSelectedIndex = (int)JobIndex.SCAN;
                }
                else if (value == (int)JobIndex.QUERY)
                {
                    LoadQueryLookupsAsync();
                }
            }
        }

        public class AppConfigStringKey
        {
            public const string Injob = "Injob";
            public const string LastProduct = "LastProduct";
            public const string LastWorker = "LastWorker";
        }

        private void ReadAppConfig()
        {
            BtnSTART_CONTENT = "START";
            CanChangeProductInfo = true;
            InJob = false;

            this.SelectedPartNumber = AppConfigHelper.Read(AppConfigStringKey.LastProduct);
            this.Worker = AppConfigHelper.Read(AppConfigStringKey.LastWorker);

        }

        private ICommand _STARTCMD;

        public ICommand STARTCMD
        {
            get
            {
                if (_STARTCMD == null)
                {
                    _STARTCMD = new RelayCommand<TextBox>(
                        o => CAN_START()
                    , t => START(t));
                }
                return _STARTCMD;
            }
        }

        private bool CAN_START()
        {
            if (string.IsNullOrEmpty(SelectedPartNumber)) return false;
            return SelectedQuantity > 0;
        }

        private void START(TextBox t)
        {
            if (InJob)
            {
                SaveCurrentScanSession(false);
                InJob = false;
                StartupManager.SetStatus("Đã tạm dừng phiên quét mã " + SelectedPartNumber + ".");
                return;
            }

            InJob = true;
            t?.Focus();
            StartScaning(SelectedPartNumber);
            StartupManager.SetStatus("Đang quét mã " + SelectedPartNumber + ".");
        }
        private void EnsureCreateAppConfig()
        {
            AppConfigHelper.EnsureCreate(AppConfigStringKey.Injob, "0");
            AppConfigHelper.EnsureCreate(AppConfigStringKey.LastProduct, "");
            AppConfigHelper.EnsureCreate(AppConfigStringKey.LastWorker, "");
        }

        public MainViewModel()
        {
            EnsureCreateAppConfig();
            InitializeScaningPropeties();
            ReadAppConfig();
        }
    }

}
