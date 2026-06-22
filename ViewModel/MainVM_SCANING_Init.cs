using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.View;
using SX3_SCANER.ViewModel.TodayBoxVM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private static readonly Brush StatusBusyBackground = CreateFrozenStatusBrush(0xEE, 0xF2, 0xFF);
        private static readonly Brush StatusFullBackground = CreateFrozenStatusBrush(0xFF, 0xFB, 0xEB);
        private static readonly Brush StatusScanningBackground = CreateFrozenStatusBrush(0xEC, 0xFD, 0xF5);
        private static readonly Brush StatusPausedBackground = CreateFrozenStatusBrush(0xF1, 0xF5, 0xF9);
        private static readonly Brush StatusReadyBackground = CreateFrozenStatusBrush(0xEF, 0xF6, 0xFF);
        private static readonly Brush StatusClosedBackground = CreateFrozenStatusBrush(0xF8, 0xFA, 0xFC);

        private static readonly Brush StatusBusyBorder = CreateFrozenStatusBrush(0xC7, 0xD2, 0xFE);
        private static readonly Brush StatusFullBorder = CreateFrozenStatusBrush(0xFB, 0xBF, 0x24);
        private static readonly Brush StatusScanningBorder = CreateFrozenStatusBrush(0xBB, 0xF7, 0xD0);
        private static readonly Brush StatusPausedBorder = CreateFrozenStatusBrush(0xCB, 0xD5, 0xE1);
        private static readonly Brush StatusReadyBorder = CreateFrozenStatusBrush(0xBF, 0xDB, 0xFE);
        private static readonly Brush StatusClosedBorder = CreateFrozenStatusBrush(0xE2, 0xE8, 0xF0);

        private static readonly Brush StatusBusyForeground = CreateFrozenStatusBrush(0x4F, 0x46, 0xE5);
        private static readonly Brush StatusFullForeground = CreateFrozenStatusBrush(0xB4, 0x53, 0x09);
        private static readonly Brush StatusScanningForeground = CreateFrozenStatusBrush(0x16, 0xA3, 0x4A);
        private static readonly Brush StatusPausedForeground = CreateFrozenStatusBrush(0x47, 0x55, 0x69);
        private static readonly Brush StatusReadyForeground = CreateFrozenStatusBrush(0x25, 0x63, 0xEB);
        private static readonly Brush StatusClosedForeground = CreateFrozenStatusBrush(0x64, 0x74, 0x8B);

        private static Brush CreateFrozenStatusBrush(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }


        private List<string> _PartNumberList;

        public List<string> PartNumberList
        {
            get { return _PartNumberList; }
            set
            {
                _PartNumberList = value;
                OnPropertyChanged();
            }
        }

        private void SetExpectedData(string pnumber)
        {
            LabelProductInfo info = new LabelProductInfoRepository().GetWithPartNumber(pnumber);

            if (info == null || string.IsNullOrWhiteSpace(info.PartName)) return;

            this.PrefixExpected = info.CodePrefix ?? string.Empty;
            this.PNameExpected = info.PartName;
            this.SealNoExpected = SelectedDate.ToString("yyMMdd");
            this.LotNoExpected = "####";
            this.SuffixExpected = info.CodeSuffix ?? string.Empty;
            this.CodeLengthExpected = info.CodeLength;
            FullCodeExpected = (info.CodeStringForm ?? string.Empty).Replace("yyMMdd", SealNoExpected);

        }

        private string _SelectedPartNumber;

        public string SelectedPartNumber
        {
            get { return _SelectedPartNumber; }
            set
            {
                if (_isScanBusy) return;
                if (string.IsNullOrWhiteSpace(value) || value == _SelectedPartNumber) return;

                string previousPartNumber = _SelectedPartNumber;
                bool wasInJob = InJob;
                if (!string.IsNullOrWhiteSpace(previousPartNumber))
                {
                    SaveCurrentScanSession(wasInJob);

                    StartupManager.SetStatus(
                        "Đã lưu phiên quét mã " + previousPartNumber +
                        " để chuyển sang mã " + value + ".");
                }

                _SelectedPartNumber = value;

                this.SelectedQuantity = new LabelProductInfoRepository().GetBoxQuantity(_SelectedPartNumber);

                SetExpectedData(value);

                if (!RestoreScanSession(value))
                {
                    CheckLastJob(value);
                }

                InJob = wasInJob;
                InputScanCode = string.Empty;
                ResetScanStatus();
                ScanTextResult = string.Empty;

                if (wasInJob)
                {
                    SaveCurrentScanSession(true);
                    StartupManager.SetStatus(
                        "Đã chuyển sang mã " + value +
                        ". Phiên quét mã " + previousPartNumber +
                        " vẫn được giữ nguyên.");
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentSessionProductText));
                OnPropertyChanged(nameof(CanSwitchProduct));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void CheckLastJob(string partNumber)
        {
            // Chỉ khôi phục thùng đang mở theo NGÀY BOX hiện tại.
            // Không tự kéo thùng cũ khác ngày lên, tránh tình trạng ngày Box bị lệch
            // với phiên quét đang thao tác.
            var notcompletebox = new BoxProductRepository().GetNotComplete(partNumber, BoxDate);

            if (string.IsNullOrEmpty(notcompletebox))
            {
                _CurrentBoxName = null;
                _currentBoxCreatedDate = null;
                ScanHistorySource = new ObservableCollection<ScanHistory>();
                CurrentScanProgress = 0;
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                return;
            }

            _CurrentBoxName = notcompletebox;
            _currentBoxCreatedDate =
                _boxProductRepository.GetBoxCreatedDate(_CurrentBoxName);
            if (_currentBoxCreatedDate.HasValue)
            {
                _SelectedBoxDate = _currentBoxCreatedDate.Value.Date;
            }
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            NotifyCurrentBoxStatusChanged();
            ScanHistorySource = new ScanHistoryRepository().GetNotComplete(notcompletebox);
            RefreshScanHistoryDisplayIndex();
            CurrentScanProgress = ScanHistorySource?.Count(x => x.ScanResult == true) ?? 0;
            SaveCurrentScanSession(false);
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            CommandManager.InvalidateRequerySuggested();
        }

        private ICommand _OpenBoxCMD;

        public ICommand OpenBoxCMD
        {
            get
            {
                if (_OpenBoxCMD == null)
                {
                    _OpenBoxCMD = new RelayCommand<object>(ob => SelectedTodayBox != null, ob => OpenSelectedTodayBoxScanInfo());
                }
                return _OpenBoxCMD;
            }
        }

        private void OpenSelectedTodayBoxScanInfo()
        {
            if (SelectedTodayBox == null)
                return;

            new TodayBoxWD
            {
                DataContext = new TodayBoxViewModel(SelectedTodayBox.BoxName)
            }.ShowDialog();
        }

        private ICommand _SetSelectedBoxOnJobCMD;

        public ICommand SetSelectedBoxOnJobCMD
        {
            get
            {
                if (_SetSelectedBoxOnJobCMD == null)
                {
                    _SetSelectedBoxOnJobCMD = new RelayCommand<object>(
                        ob => SelectedTodayBox != null &&
                            !SelectedTodayBox.BoxComplete &&
                            !HasOpenScanSession &&
                            !_isScanBusy,
                        ob => SetBoxOnJob(SelectedTodayBox));
                }
                return _SetSelectedBoxOnJobCMD;
            }
        }

        private void SetBoxOnJob(BoxProduct selectedTodayBox)
        {
            InJob = false;
            SelectedPartNumber = selectedTodayBox.ProductPartNumber;
        }

        private DateTime _SelectedDate;
        private DateTime _SelectedBoxDate;

        public DateTime ScanLabelDate
        {
            get { return SelectedDate; }
            set { SelectedDate = value; }
        }

        public DateTime BoxDate
        {
            get
            {
                return _currentBoxCreatedDate.HasValue
                    ? _currentBoxCreatedDate.Value.Date
                    : _SelectedBoxDate.Date;
            }
            set
            {
                if (_isScanBusy) return;
                DateTime next = value.Date;

                if (BoxDate.Date == next)
                    return;

                DateTime previous = BoxDate.Date;

                if (HasOpenScanSession)
                {
                    ApplyBoxDateChangeForOpenSession(previous, next);
                    return;
                }

                _SelectedBoxDate = next;
                _currentBoxCreatedDate = null;

                OnPropertyChanged(nameof(BoxDate));
                OnPropertyChanged(nameof(BoxDateText));
                OnPropertyChanged(nameof(CurrentSessionDateText));

                ToDayBoxSource = new BoxProductRepository().GetAllTodayBox(BoxDate);

                if (!string.IsNullOrWhiteSpace(SelectedPartNumber) &&
                    !RestoreScanSession(SelectedPartNumber))
                {
                    CheckLastJob(SelectedPartNumber);
                }

                RefreshDashboardStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ApplyBoxDateChangeForOpenSession(DateTime previous, DateTime next)
        {
            string currentBoxName = _CurrentBoxName;

            _SelectedBoxDate = next;
            _currentBoxCreatedDate = next;

            if (ScanHistorySource != null)
            {
                foreach (ScanHistory item in ScanHistorySource)
                {
                    if (item != null)
                    {
                        item.BoxDate = next;
                    }
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(currentBoxName))
                {
                    _boxProductRepository.UpdateBoxDate(currentBoxName, next);
                    _scanHistoryRepository.UpdateBoxDateByBoxName(currentBoxName, next);
                }

                if (!string.IsNullOrWhiteSpace(SelectedPartNumber))
                {
                    _scanSessionService.RemoveSession(SelectedPartNumber, previous);
                }

                SaveCurrentScanSession(InJob);

                StartupManager.SetStatus(
                    "Đã đổi ngày box sang " + BoxDateText +
                    ". Thùng hiện tại vẫn giữ mã " + CurrentBoxNameText + ".");
            }
            catch (Exception ex)
            {
                StartupManager.Log("Khong cap nhat duoc ngay box cho thung dang mo: " + ex);
                StartupManager.SetStatus("Lỗi đổi ngày box: " + ex.Message);
            }

            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            OnPropertyChanged(nameof(CurrentSessionDateText));

            ToDayBoxSource = new BoxProductRepository().GetAllTodayBox(BoxDate);

            RefreshDashboardStats();
            NotifySessionHeaderChanged();
            NotifyCurrentBoxStatusChanged();
            CommandManager.InvalidateRequerySuggested();
        }


        private void ShowBoxDateChangeBlockedDialog()
        {
            Window owner = Application.Current == null ? null : Application.Current.MainWindow;

            Window dialog = new Window
            {
                Title = "KHÔNG THỂ ĐỔI NGÀY BOX",
                Width = 680,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null && owner.IsVisible
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                WindowStyle = WindowStyle.SingleBorderWindow
            };

            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
            }

            Border root = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(26),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border iconBox = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 18, 0)
            };

            TextBlock iconText = new TextBlock
            {
                Text = "!",
                FontSize = 38,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            iconBox.Child = iconText;

            StackPanel titlePanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock titleText = new TextBlock
            {
                Text = "KHÔNG THỂ ĐỔI NGÀY BOX",
                FontSize = 26,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextWrapping = TextWrapping.Wrap
            };

            TextBlock subTitleText = new TextBlock
            {
                Text = "Thùng hiện tại đang có dữ liệu scan.",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            titlePanel.Children.Add(titleText);
            titlePanel.Children.Add(subTitleText);

            headerPanel.Children.Add(iconBox);
            headerPanel.Children.Add(titlePanel);

            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            Border infoBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18)
            };

            StackPanel infoPanel = new StackPanel();

            TextBlock line1 = new TextBlock
            {
                Text = "NGÀY BOX là ngày quản lý thùng và sinh mã Box.",
                FontSize = 17,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBlock line2 = new TextBlock
            {
                Text = "Thùng đang mở đã có dữ liệu scan, nên không được đổi NGÀY BOX trực tiếp để tránh lệch dữ liệu lịch sử.",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24
            };

            infoPanel.Children.Add(line1);
            infoPanel.Children.Add(line2);
            infoBox.Child = infoPanel;

            Grid.SetRow(infoBox, 2);
            grid.Children.Add(infoBox);

            Grid optionGrid = new Grid();
            optionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            optionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border optionDeleteBox = CreateBoxDateDialogOptionCard(
                "Muốn đổi NGÀY BOX",
                "Hãy hủy/xóa thùng hiện tại trước, sau đó chọn lại ngày box mới.",
                Color.FromRgb(254, 242, 242),
                Color.FromRgb(248, 113, 113),
                Color.FromRgb(185, 28, 28));

            Border optionScanDateBox = CreateBoxDateDialogOptionCard(
                "Muốn scan tem ngày khác",
                "Không đổi ngày box. Chỉ đổi NGÀY TEM để scan tiếp vào cùng thùng.",
                Color.FromRgb(236, 253, 245),
                Color.FromRgb(74, 222, 128),
                Color.FromRgb(21, 128, 61));

            Grid.SetColumn(optionDeleteBox, 0);
            Grid.SetColumn(optionScanDateBox, 2);

            optionGrid.Children.Add(optionDeleteBox);
            optionGrid.Children.Add(optionScanDateBox);

            Grid.SetRow(optionGrid, 4);
            grid.Children.Add(optionGrid);

            Button okButton = new Button
            {
                Content = "ĐÃ HIỂU",
                Height = 52,
                MinWidth = 170,
                Padding = new Thickness(28, 0, 28, 0),
                FontSize = 17,
                FontWeight = FontWeights.Black,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };

            okButton.Click += (sender, args) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };

            Grid.SetRow(okButton, 6);
            grid.Children.Add(okButton);

            root.Child = grid;
            dialog.Content = root;
            dialog.ShowDialog();
        }

        private static Border CreateBoxDateDialogOptionCard(
            string title,
            string description,
            Color backgroundColor,
            Color borderColor,
            Color titleColor)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(backgroundColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                MinHeight = 124
            };

            StackPanel panel = new StackPanel();

            TextBlock titleText = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(titleColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 9)
            };

            TextBlock descriptionText = new TextBlock
            {
                Text = description,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21
            };

            panel.Children.Add(titleText);
            panel.Children.Add(descriptionText);

            card.Child = panel;
            return card;
        }

        public string BoxDateText
        {
            get { return BoxDate.ToString("dd/MM/yyyy"); }
        }

        public string ScanLabelDateText
        {
            get { return ScanLabelDate.ToString("dd/MM/yyyy"); }
        }

        public string ScanQuantityText
        {
            get { return CurrentScanProgress + " / " + SelectedQuantity; }
        }

        public string CurrentBoxStatusText
        {
            get
            {
                if (_isScanBusy) return "Đang xử lý";
                if (IsFullBoxReadyToComplete) return "Đủ SL - chờ đóng";
                if (HasOpenScanSession && InJob) return "Đang scan";
                if (HasOpenScanSession && !InJob) return "Tạm dừng";
                if (!HasOpenScanSession && InJob) return "Sẵn sàng scan";
                return "Chưa mở thùng";
            }
        }

        public Brush CurrentBoxStatusBackground
        {
            get
            {
                if (_isScanBusy) return StatusBusyBackground;
                if (IsFullBoxReadyToComplete) return StatusFullBackground;
                if (HasOpenScanSession && InJob) return StatusScanningBackground;
                if (HasOpenScanSession && !InJob) return StatusPausedBackground;
                if (!HasOpenScanSession && InJob) return StatusReadyBackground;
                return StatusClosedBackground;
            }
        }

        public Brush CurrentBoxStatusBorderBrush
        {
            get
            {
                if (_isScanBusy) return StatusBusyBorder;
                if (IsFullBoxReadyToComplete) return StatusFullBorder;
                if (HasOpenScanSession && InJob) return StatusScanningBorder;
                if (HasOpenScanSession && !InJob) return StatusPausedBorder;
                if (!HasOpenScanSession && InJob) return StatusReadyBorder;
                return StatusClosedBorder;
            }
        }

        public Brush CurrentBoxStatusForeground
        {
            get
            {
                if (_isScanBusy) return StatusBusyForeground;
                if (IsFullBoxReadyToComplete) return StatusFullForeground;
                if (HasOpenScanSession && InJob) return StatusScanningForeground;
                if (HasOpenScanSession && !InJob) return StatusPausedForeground;
                if (!HasOpenScanSession && InJob) return StatusReadyForeground;
                return StatusClosedForeground;
            }
        }

        private void NotifyCurrentBoxStatusChanged()
        {
            OnPropertyChanged(nameof(CurrentBoxStatusText));
            OnPropertyChanged(nameof(CurrentBoxStatusBackground));
            OnPropertyChanged(nameof(CurrentBoxStatusBorderBrush));
            OnPropertyChanged(nameof(CurrentBoxStatusForeground));
            OnPropertyChanged(nameof(CurrentBoxNameText));
            OnPropertyChanged(nameof(CurrentSessionProductText));
            OnPropertyChanged(nameof(CurrentSessionDateText));
            OnPropertyChanged(nameof(CurrentPassCount));
            OnPropertyChanged(nameof(CurrentNgCount));
            OnPropertyChanged(nameof(CurrentProgressPercentText));
        }

        public DateTime SelectedDate
        {
            get { return _SelectedDate; }
            set
            {
                if (_isScanBusy) return;
                if (_SelectedDate == value)
                {
                    return;
                }

                bool hasOpenBox = HasOpenScanSession;
                string old = _SelectedDate.ToString("yyMMdd");
                _SelectedDate = value;
                SealNoExpected = _SelectedDate.ToString("yyMMdd");

                if (!string.IsNullOrWhiteSpace(FullCodeExpected))
                {
                    FullCodeExpected = FullCodeExpected.Replace(
                        old,
                        SealNoExpected);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanLabelDate));
                OnPropertyChanged(nameof(ScanLabelDateText));
                OnPropertyChanged(nameof(BoxDate));
                OnPropertyChanged(nameof(BoxDateText));
                OnPropertyChanged(nameof(SealNoExpected));

                if (hasOpenBox)
                {
                    _boxProductRepository.UpdateScanLabelDate(
                        _CurrentBoxName,
                        ScanLabelDate);
                    SaveCurrentScanSession(InJob);
                    string dateChangeMessage =
                        "Đã đổi ngày tem scan sang " + ScanLabelDateText +
                        ". Thùng hiện tại vẫn giữ ngày box " + BoxDateText + ".";
                    ScanResultDetailText = dateChangeMessage;
                    StartupManager.SetStatus(dateChangeMessage);
                    RefreshDashboardStats();
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }

                // Đổi NGÀY TEM khi chưa mở thùng: chỉ cập nhật mẫu tem/SealNo.
                // NGÀY BOX và danh sách thùng trong ngày vẫn do BoxDate quản lý riêng.
                if (!string.IsNullOrWhiteSpace(SelectedPartNumber))
                {
                    SetExpectedData(SelectedPartNumber);
                }

                RefreshDashboardStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int _Length_OK = -1;

        public int Length_OK
        {
            get { return _Length_OK; }
            set { _Length_OK = value; OnPropertyChanged(); }
        }

        private int _Suffix_OK = -1;

        public int Suffix_OK
        {
            get { return _Suffix_OK; }
            set { _Suffix_OK = value; OnPropertyChanged(); }
        }

        private int _LotNo_OK = -1;

        public int LotNo_OK
        {
            get { return _LotNo_OK; }
            set { _LotNo_OK = value; OnPropertyChanged(); }
        }

        private int _SealNo_OK = -1;

        public int SealNo_OK
        {
            get { return _SealNo_OK; }
            set { _SealNo_OK = value; OnPropertyChanged(); }
        }

        private int _PName_OK = -1;

        public int PName_OK
        {
            get { return _PName_OK; }
            set { _PName_OK = value; OnPropertyChanged(); }
        }

        private int _Prefix_OK = -1;

        public int Prefix_OK
        {
            get { return _Prefix_OK; }
            set { _Prefix_OK = value; OnPropertyChanged(); }
        }



        private int _SelectedQuantity;

        public int SelectedQuantity
        {
            get { return _SelectedQuantity; }
            set
            {
                if (_isScanBusy) return;
                if (_SelectedQuantity == value) return;

                _SelectedQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanQuantityText));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                NotifyCurrentBoxStatusChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _FullCodeExpected;

        public string FullCodeExpected
        {
            get { return _FullCodeExpected; }
            set { _FullCodeExpected = value; OnPropertyChanged(); }
        }


        private string _PNameExpected;

        public string PNameExpected
        {
            get { return _PNameExpected; }
            set { _PNameExpected = value; OnPropertyChanged(); }
        }

        private string _PNameScanResult;

        public string PNameScanResult
        {
            get { return _PNameScanResult; }
            set { _PNameScanResult = value; OnPropertyChanged(); }
        }

        private string _SealNoExpected;

        public string SealNoExpected
        {
            get { return _SealNoExpected; }
            set { _SealNoExpected = value; OnPropertyChanged(); }
        }

        private string _SealnoScanResult;

        public string SealnoScanResult
        {
            get { return _SealnoScanResult; }
            set { _SealnoScanResult = value; OnPropertyChanged(); }
        }

        private string _LotNoExpected;

        public string LotNoExpected
        {
            get { return _LotNoExpected; }
            set { _LotNoExpected = value; OnPropertyChanged(); }
        }

        private string _LotNoScanResult;

        public string LotNoScanResult
        {
            get { return _LotNoScanResult; }
            set { _LotNoScanResult = value; OnPropertyChanged(); }
        }

        private string _PrefixExpected;

        public string PrefixExpected
        {
            get { return _PrefixExpected; }
            set { _PrefixExpected = value; OnPropertyChanged(); }
        }
        private string _PrefixScanResut;

        public string PrefixScanResut
        {
            get { return _PrefixScanResut; }
            set { _PrefixScanResut = value; OnPropertyChanged(); }
        }
        private string _SuffixExpected;

        public string SuffixExpected
        {
            get { return _SuffixExpected; }
            set { _SuffixExpected = value; OnPropertyChanged(); }
        }
        private string _SuffixScanResult;

        public string SuffixScanResult
        {
            get { return _SuffixScanResult; }
            set { _SuffixScanResult = value; OnPropertyChanged(); }
        }

        private int _CodeLengthExpected;

        public int CodeLengthExpected
        {
            get { return _CodeLengthExpected; }
            set { _CodeLengthExpected = value; OnPropertyChanged(); }
        }

        private int _CodeLengthScanResult;

        public int CodeLengthScanResult
        {
            get { return _CodeLengthScanResult; }
            set { _CodeLengthScanResult = value; OnPropertyChanged(); }
        }





        private ObservableCollection<ScanHistory> _ScanHistorySource;

        public ObservableCollection<ScanHistory> ScanHistorySource
        {
            get { return _ScanHistorySource; }
            set
            {
                _ScanHistorySource = value;
                SubscribeDashboardScanHistory(value);
                ScanHistoryView = value == null ? null : CollectionViewSource.GetDefaultView(value);
                ApplyCurrentBoxHistoryFilter();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPassCount));
                OnPropertyChanged(nameof(CurrentNgCount));
                OnPropertyChanged(nameof(CurrentProgressPercentText));
                OnPropertyChanged(nameof(HasOpenScanSession));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private ICollectionView _ScanHistoryView;

        public ICollectionView ScanHistoryView
        {
            get { return _ScanHistoryView; }
            set { _ScanHistoryView = value; OnPropertyChanged(); }
        }


        private void InitializeScaningPropeties()
        {
            _SelectedDate = DateTime.Now.Date;
            _SelectedBoxDate = DateTime.Now.Date;

            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(ScanLabelDate));
            OnPropertyChanged(nameof(ScanLabelDateText));
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));

            ToDayBoxSource = new ObservableCollection<BoxProduct>();
            ScanHistorySource = new ObservableCollection<ScanHistory>();
            PartNumberList = new List<string>();
        }

        private void LoadScanDataAfterDatabaseReady()
        {
            ToDayBoxSource = new BoxProductRepository().GetAllTodayBox(BoxDate);
            LoadInitialListsAsync();
        }

        private async void LoadInitialListsAsync()
        {
            PartNumberList = await Task.Run(() =>
                new LabelProductInfoRepository().GetAllLabelProductInfo()
                    .Select(x => x.PartNumber)
                    .ToList());
        }
    }
}
