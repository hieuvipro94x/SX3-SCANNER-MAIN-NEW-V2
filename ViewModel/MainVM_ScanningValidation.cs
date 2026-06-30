using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using SX3_SCANER.Model.Respository;
using SX3_SCANER.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;


namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private async Task ClosePartialBoxAsync()
        {
            await _scanWriteLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(_CurrentBoxName) ||
                    ScanHistorySource == null ||
                    ScanHistorySource.Count == 0 ||
                    CurrentScanProgress <= 0 ||
                    (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity))
                {
                    return;
                }

                try
                {
                    var dialog = new ConfirmPartialBoxWD(
                        CurrentScanProgress,
                        SelectedQuantity);
                    Window owner = Application.Current?.MainWindow;
                    if (owner != null && owner.IsVisible)
                    {
                        dialog.Owner = owner;
                    }

                    bool? result = dialog.ShowDialog();
                    if (result == true)
                    {
                        await CompleteBoxAsync(true);
                    }
                }
                catch (Exception ex)
                {
                    StartupManager.Log(
                        "Khong mo duoc man hinh xac nhan dong thung le. Chi tiet: " + ex);
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                        "Không thể mở màn hình xác nhận đóng thùng lẻ.\n\n" + ex.Message,
                        "Lỗi giao diện",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _scanWriteLock.Release();
            }
        }

        private async Task CompleteFullBoxWithLockAsync()
        {
            await _scanWriteLock.WaitAsync();
            _isScanBusy = true;
            NotifyCurrentBoxStatusChanged();
            OnPropertyChanged(nameof(CanSwitchProduct));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await CompleteBoxAsync(false);
            }
            finally
            {
                _isScanBusy = false;
                NotifyCurrentBoxStatusChanged();
                OnPropertyChanged(nameof(CanSwitchProduct));
                OnPropertyChanged(nameof(CanModifySessionSelection));
                CommandManager.InvalidateRequerySuggested();
                _scanWriteLock.Release();
            }
        }

        private async Task CompleteBoxAsync(bool isPartial)
        {
            if (string.IsNullOrWhiteSpace(_CurrentBoxName) || CurrentScanProgress <= 0)
                return;

            if (isPartial)
            {
                if (SelectedQuantity > 0 && CurrentScanProgress >= SelectedQuantity)
                    return;
            }
            else if (SelectedQuantity <= 0 || CurrentScanProgress < SelectedQuantity)
            {
                return;
            }

            if (!ShowWorkerCompletePopup())
                return;

            string confirmedWorker = Worker.Trim();

            string completedBoxName = _CurrentBoxName;
            string completedProductCode = SelectedPartNumber;
            DateTime completedBoxCreatedDate = GetCurrentBoxCreatedDate();
            string boxType = isPartial ? "PARTIAL" : "FULL";

            try
            {
                await Task.Run(() =>
                {
                    using (SQLiteConnection connection = DatabaseRepository.CreateConnection())
                    using (SQLiteTransaction transaction = connection.BeginTransaction())
                    {
                        _scanHistoryRepository.UpdateWorkerByBoxName(
                            completedBoxName,
                            confirmedWorker,
                            connection,
                            transaction);
                        _scanHistoryRepository.SetBoxTypeByBoxName(
                            completedBoxName,
                            isPartial,
                            connection,
                            transaction);
                        _boxProductRepository.SetBoxComplete(
                            completedBoxName,
                            isPartial,
                            confirmedWorker,
                            connection,
                            transaction);
                        _scanSessionService.RemoveSession(
                            completedProductCode,
                            completedBoxCreatedDate,
                            connection,
                            transaction);
                        transaction.Commit();
                    }
                });
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong hoan tat duoc thung " + completedBoxName + ". Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Không hoàn tất được thùng trong database.\nDữ liệu hiện tại vẫn được giữ nguyên để thử lại.",
                    "LỖI HOÀN TẤT THÙNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            foreach (var item in ScanHistorySource)
            {
                item.ScanWorker = confirmedWorker;
                item.BoxType = boxType;
                item.IsPartialBox = isPartial;
            }

            BoxProduct completedBox = ToDayBoxSource?.FirstOrDefault(x => x.BoxName == completedBoxName);
            if (completedBox != null)
            {
                completedBox.BoxProgress = CurrentScanProgress;
                completedBox.BoxWorker = confirmedWorker;
                completedBox.BoxType = boxType;
                completedBox.IsPartialBox = isPartial;
                completedBox.BoxComplete = true;
            }

            ScanHistoryView?.Refresh();
            ToDayBoxView?.Refresh();
            RefreshDashboardStats();

            CurrentScanProgress = 0;
            ScanHistorySource = new ObservableCollection<ScanHistory>();
            InputScanCode = string.Empty;
            _CurrentBoxName = null;
            _currentBoxCreatedDate = null;
            OnPropertyChanged(nameof(BoxDate));
            OnPropertyChanged(nameof(BoxDateText));
            NotifyCurrentBoxStatusChanged();
            ResetScanStatus();
            OnPropertyChanged(nameof(HasOpenScanSession));
            OnPropertyChanged(nameof(IsFullBoxReadyToComplete));
            OnPropertyChanged(nameof(CanModifySessionSelection));
            CommandManager.InvalidateRequerySuggested();

            // Không mang tên vừa xác nhận sang thùng kế tiếp.
            Worker = string.Empty;
            AppConfigHelper.Modify(AppConfigStringKey.LastWorker, string.Empty);

            string message = isPartial
                ? "HO\u00C0N TH\u00C0NH TH\u00D9NG L\u1EBA"
                : "HO\u00C0N TH\u00C0NH TH\u00D9NG \u0110\u1EE6";
            SX3_SCANER.Helper.ProfessionalMessageBox.Show(message, "TH\u00D4NG B\u00C1O", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResetScanStatus()
        {
            CodeLengthScanResult = 0;

            PrefixScanResut = string.Empty;

            PNameScanResult = string.Empty;

            SealnoScanResult = string.Empty;

            LotNoScanResult = string.Empty;

            SuffixScanResult = string.Empty;

            Length_OK = -1;

            Prefix_OK = -1;

            Suffix_OK = -1;

            PName_OK = -1;

            SealNo_OK = -1;

            LotNo_OK = -1;

            _ScanMess = string.Empty;
        }

        private bool Scan(string input)
        {
            _CurrentScanHistory.ScanTime = GetBusinessScanTime();

            DateTime currentCheckDate = DateTime.Today;
            if (!ScanValidationService.IsScanLabelDateAllowed(
                ScanLabelDate,
                currentCheckDate))
            {
                _CurrentScanHistory.ScanTime = DateTime.Now;
                SealNo_OK = 0;
                SealnoScanResult = SealNoExpected;
                _ScanMess = "NG - Ng\u00E0y tem ch\u1EC9 \u0111\u01B0\u1EE3c t\u1EEB h\u00F4m nay l\u00F9i t\u1ED1i \u0111a 4 ng\u00E0y";
                return false;
            }

            // Format mới cho mã hàng Car HE EV:
            // PART NO/PARTNAME: K32000-22400
            // QR thực tế: K32000-22400,2606172001
            // Cấu trúc: <PartNo>,<Serial>
            if (ScanValidationService.IsPartNoCommaSerialFormat(input))
            {
                return ScanPartNoCommaSerial(input);
            }

            return CheckLength(input);
        }

        private bool TryParsePartNoCommaSerial(
            string input,
            out string partNo,
            out string serial)
        {
            partNo = string.Empty;
            serial = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Trim().Split(',');
            if (parts.Length != 2)
                return false;

            partNo = parts[0].Trim();
            serial = parts[1].Trim();

            return !string.IsNullOrWhiteSpace(partNo) &&
                !string.IsNullOrWhiteSpace(serial);
        }

        private bool ScanPartNoCommaSerial(string input)
        {
            CodeLengthScanResult = input?.Length ?? 0;

            if (!TryParsePartNoCommaSerial(input, out string scannedPartNo, out string scannedSerial))
            {
                Length_OK = 0;
                _ScanMess = "NG - Sai định dạng QR dấu phẩy";
                return false;
            }

            if (CodeLengthExpected > 0 && input.Length != CodeLengthExpected)
            {
                Length_OK = 0;
                _ScanMess = "NG - Sai độ dài";
                return false;
            }

            Length_OK = 1;

            if (!string.IsNullOrWhiteSpace(PrefixExpected) &&
                !scannedPartNo.StartsWith(PrefixExpected, StringComparison.OrdinalIgnoreCase))
            {
                Prefix_OK = 0;
                _ScanMess = "NG - Sai đầu mã / Prefix";
                return false;
            }

            PrefixScanResut = PrefixExpected ?? string.Empty;
            Prefix_OK = 1;

            string expectedPartName = ScanValidationService.NormalizeQrProductCode(PNameExpected);
            string expectedPartNumber = ScanValidationService.NormalizeQrProductCode(SelectedPartNumber);
            string actualPartNo = ScanValidationService.NormalizeQrProductCode(scannedPartNo);

            if (string.IsNullOrWhiteSpace(actualPartNo) ||
                (actualPartNo != expectedPartName && actualPartNo != expectedPartNumber))
            {
                PName_OK = 0;
                PNameScanResult = scannedPartNo;
                _ScanMess = "NG - Sai mã sản phẩm / PartName";
                return false;
            }

            PName_OK = 1;
            PNameScanResult = scannedPartNo;
            _CurrentScanHistory.ProductPartName = PNameExpected;

            DateTime qrLabelDate;
            if (!ScanValidationService.TryParseLeadingDate(scannedSerial, out qrLabelDate) ||
                qrLabelDate.Date != ScanLabelDate.Date)
            {
                SealNo_OK = 0;
                SealnoScanResult = ScanValidationService.ExtractSegment(scannedSerial, 0, 6);
                _CurrentScanHistory.SealNo = SealnoScanResult;
                _ScanMess = "NG - Ng\u00E0y tr\u00EAn tem kh\u00F4ng kh\u1EDBp NG\u00C0Y TEM \u0111ang ch\u1ECDn";
                return false;
            }

            SealNo_OK = 1;
            SealnoScanResult = qrLabelDate.ToString("yyMMdd");
            _CurrentScanHistory.SealNo = SealnoScanResult;

            if (!string.IsNullOrWhiteSpace(SuffixExpected) &&
                !scannedSerial.EndsWith(SuffixExpected, StringComparison.OrdinalIgnoreCase))
            {
                Suffix_OK = 0;
                SuffixScanResult = scannedSerial;
                _ScanMess = "NG - Sai cuối mã / Suffix";
                return false;
            }

            Suffix_OK = 1;
            SuffixScanResult = SuffixExpected ?? string.Empty;

            LotNo_OK = 1;
            LotNoExpected = scannedSerial;
            LotNoScanResult = scannedSerial;
            _CurrentScanHistory.LotNo = scannedSerial;

            return true;
        }

        private DateTime GetBusinessScanTime()
        {
            return ScanLabelDate.Date.Add(DateTime.Now.TimeOfDay);
        }

        private void ShowDuplicateScanMessage(string message, string scanData)
        {
            ScanTextResult = NG;
            var error = new ScanErrorPresentation
            {
                Detail = "NG - Tr\u00F9ng tem",
                Standard = "M\u1ED7i tem ch\u1EC9 \u0111\u01B0\u1EE3c scan m\u1ED9t l\u1EA7n",
                Actual = ScanValidationService.DisplayValue(scanData),
                Resolution = "Ki\u1EC3m tra l\u1EA1i tem v\u00E0 l\u1ECBch s\u1EED scan."
            };
            ScanResultDetailText = error.ToDisplayText();
            InputScanCode = string.Empty;
            StartupManager.SetStatus(message);
            ShowScanErrorWindow(error);
        }

        private void ShowScanErrorWindow(ScanErrorPresentation error)
        {
            if (error == null)
                return;

            ScanSoundService.PlayNg();
            try
            {
                var dialog = new ScanErrorWD(
                    error.Detail,
                    error.Standard,
                    error.Actual,
                    error.Resolution);
                Window owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                StartupManager.Log(
                    "Khong mo duoc man hinh ket qua scan NG. Chi tiet: " + ex);
                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    error.ToDisplayText() + "\n\nChi tiết lỗi giao diện: " + ex.Message,
                    "KẾT QUẢ SCAN: NG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool CheckLength(string input)
        {
            CodeLengthScanResult = input.Length;

            if (input.Length != CodeLengthExpected)
            {
                Length_OK = 0;

                _ScanMess = "NG - Sai \u0111\u1ED9 d\u00E0i";

                return false;
            }

            Length_OK = 1;

            return CheckPrefix(input);
        }

        private bool CheckPrefix(string input)
        {
            if (!string.IsNullOrWhiteSpace(PrefixExpected)
                && !input.StartsWith(PrefixExpected, StringComparison.Ordinal))
            {
                Prefix_OK = 0;

                _ScanMess = "NG - Sai \u0111\u1EA7u m\u00E3 / Prefix";

                return false;
            }

            PrefixScanResut = PrefixExpected ?? string.Empty;

            Prefix_OK = 1;

            int len = PrefixExpected?.Length ?? 0;

            return CheckProductName(input.Substring(len));
        }

        private bool CheckProductName(string input)
        {
            if (string.IsNullOrWhiteSpace(PNameExpected)
                || !input.StartsWith(PNameExpected, StringComparison.Ordinal))
            {
                PName_OK = 0;

                _ScanMess = "NG - Sai m\u00E3 s\u1EA3n ph\u1EA9m / PartName";

                return false;
            }

            PNameScanResult = PNameExpected;

            PName_OK = 1;

            _CurrentScanHistory.ProductPartName = PNameExpected;

            return CheckSealNo(input.Substring(PNameExpected.Length));
        }

        private bool CheckSealNo(string input)
        {
            if (string.IsNullOrWhiteSpace(SealNoExpected)
                || !input.StartsWith(SealNoExpected, StringComparison.Ordinal))
            {
                SealNo_OK = 0;

                _ScanMess = "NG - Sai ngày / SealNo";

                return false;
            }

            SealnoScanResult = SealNoExpected;

            _CurrentScanHistory.SealNo = SealNoExpected;

            SealNo_OK = 1;

            return CheckLotNo(input.Substring(SealNoExpected.Length));
        }

        private bool CheckLotNo(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length < 4)
            {
                LotNo_OK = 0;

                _ScanMess = "NG - Sai LotNo";

                return false;
            }

            string lotNo = input.Substring(0, 4);

            if (lotNo.Length != 4 ||
                !lotNo.StartsWith("2", StringComparison.Ordinal) ||
                !int.TryParse(lotNo, out int _))
            {
                LotNo_OK = 0;

                _ScanMess = "NG - Sai LotNo";

                return false;
            }

            LotNo_OK = 1;

            LotNoExpected = lotNo;

            LotNoScanResult = lotNo;

            _CurrentScanHistory.LotNo = lotNo;

            return CheckSuffix(input.Substring(4));
        }

        private bool CheckSuffix(string input)
        {
            if (!string.IsNullOrWhiteSpace(SuffixExpected)
                && !input.EndsWith(SuffixExpected, StringComparison.Ordinal))
            {
                Suffix_OK = 0;

                _ScanMess = "NG - Sai cu\u1ED1i m\u00E3 / Suffix";

                return false;
            }

            SuffixScanResult = input;

            Suffix_OK = 1;

            return true;
        }

        private string TryExtractLotNo(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            int lotStart = (PrefixExpected?.Length ?? 0) +
                (PNameExpected?.Length ?? 0) +
                (SealNoExpected?.Length ?? 0);

            if (input.Length < lotStart + 4)
                return string.Empty;

            return input.Substring(lotStart, 4);
        }

        private ScanErrorPresentation BuildNgErrorPresentation(string inputScanCode)
        {
            string normalizedMessage = ScanHistory.RemoveVietnameseSigns(
                ScanHistory.NormalizeScanMessage(_ScanMess))
                .ToUpperInvariant();
            string lotNo = !string.IsNullOrWhiteSpace(_CurrentScanHistory?.LotNo)
                ? _CurrentScanHistory.LotNo
                : TryExtractLotNo(inputScanCode);
            string actualPartName = ScanValidationService.ExtractSegment(
                inputScanCode,
                PrefixExpected?.Length ?? 0,
                PNameExpected?.Length ?? 0);
            string actualSealNo = ScanValidationService.ExtractSegment(
                inputScanCode,
                (PrefixExpected?.Length ?? 0) + (PNameExpected?.Length ?? 0),
                SealNoExpected?.Length ?? 0);

            if (normalizedMessage.Contains("LOT"))
            {
                bool isDuplicate = normalizedMessage.Contains("TRUNG");
                return new ScanErrorPresentation
                {
                    Detail = isDuplicate ? "NG - Tr\u00F9ng LotNo" : "NG - Sai LotNo",
                    Standard = isDuplicate
                        ? "LotNo ch\u01B0a \u0111\u01B0\u1EE3c scan trong th\u00F9ng ho\u1EB7c l\u1ECBch s\u1EED"
                        : "LotNo t\u1EEB 2000 \u0111\u1EBFn 2999",
                    Actual = ScanValidationService.DisplayValue(lotNo),
                    Resolution = isDuplicate
                        ? "Ki\u1EC3m tra l\u1EA1i tem, tr\u00E1nh scan tr\u00F9ng LotNo."
                        : "Ki\u1EC3m tra l\u1EA1i LotNo tr\u00EAn tem."
                };
            }

            if (normalizedMessage.Contains("NGAY") ||
                normalizedMessage.Contains("SEAL"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai ng\u00E0y s\u1EA3n xu\u1EA5t",
                    Standard = ScanValidationService.DisplayValue(SealNoExpected),
                    Actual = ScanValidationService.DisplayValue(actualSealNo),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i ng\u00E0y \u0111ang ch\u1ECDn ho\u1EB7c tem \u0111ang scan."
                };
            }

            if (normalizedMessage.Contains("PARTNAME") ||
                normalizedMessage.Contains("TEN SAN PHAM") ||
                normalizedMessage.Contains("MA SAN PHAM"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai m\u00E3 s\u1EA3n ph\u1EA9m",
                    Standard = ScanValidationService.DisplayValue(PNameExpected),
                    Actual = ScanValidationService.DisplayValue(actualPartName),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i s\u1EA3n ph\u1EA9m \u0111ang ch\u1ECDn v\u00E0 tem scan."
                };
            }

            if (normalizedMessage.Contains("DINH DANG") ||
                normalizedMessage.Contains("FORMAT"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai định dạng QR",
                    Standard = "Định dạng đúng: " + ScanValidationService.DisplayValue(PNameExpected) + ",<Serial>  |  Ví dụ: K32000-22400,2606172001",
                    Actual = ScanValidationService.DisplayValue(inputScanCode),
                    Resolution = "Kiểm tra lại QR phải có đúng 1 dấu phẩy, phần trước là mã sản phẩm, phần sau là serial."
                };
            }

            if (normalizedMessage.Contains("DO DAI") ||
                normalizedMessage.Contains("LENGTH"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai \u0111\u1ED9 d\u00E0i m\u00E3",
                    Standard = CodeLengthExpected + " k\u00FD t\u1EF1",
                    Actual = (inputScanCode?.Length ?? 0) + " k\u00FD t\u1EF1",
                    Resolution = "Ki\u1EC3m tra tem c\u00F3 b\u1ECB thi\u1EBFu, th\u1EEBa ho\u1EB7c m\u1EDD k\u00FD t\u1EF1."
                };
            }

            if (normalizedMessage.Contains("PREFIX") ||
                normalizedMessage.Contains("DAU MA"))
            {
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai \u0111\u1EA7u m\u00E3",
                    Standard = ScanValidationService.DisplayValue(PrefixExpected),
                    Actual = ScanValidationService.DisplayValue(ScanValidationService.ExtractSegment(
                        inputScanCode,
                        0,
                        PrefixExpected?.Length ?? 0)),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i \u0111\u1EA7u m\u00E3 tr\u00EAn tem."
                };
            }

            if (normalizedMessage.Contains("SUFFIX") ||
                normalizedMessage.Contains("CUOI MA"))
            {
                int suffixLength = SuffixExpected?.Length ?? 0;
                return new ScanErrorPresentation
                {
                    Detail = "NG - Sai cu\u1ED1i m\u00E3",
                    Standard = ScanValidationService.DisplayValue(SuffixExpected),
                    Actual = ScanValidationService.DisplayValue(ScanValidationService.ExtractSegment(
                        inputScanCode,
                        Math.Max(0, (inputScanCode?.Length ?? 0) - suffixLength),
                        suffixLength)),
                    Resolution = "Ki\u1EC3m tra l\u1EA1i cu\u1ED1i m\u00E3 tr\u00EAn tem."
                };
            }

            return new ScanErrorPresentation
            {
                Detail = ScanHistory.NormalizeScanMessage(_ScanMess),
                Standard = ScanValidationService.DisplayValue(FullCodeExpected),
                Actual = ScanValidationService.DisplayValue(inputScanCode),
                Resolution = "Ki\u1EC3m tra l\u1EA1i tem v\u00E0 s\u1EA3n ph\u1EA9m \u0111ang ch\u1ECDn."
            };
        }

        private sealed class ScanErrorPresentation
        {
            public string Detail { get; set; }
            public string Standard { get; set; }
            public string Actual { get; set; }
            public string Resolution { get; set; }

            public string ToDisplayText()
            {
                var builder = new StringBuilder();
                builder.AppendLine("Chi ti\u1EBFt l\u1ED7i: " + Detail);
                builder.AppendLine("Ti\u00EAu chu\u1EA9n: " + Standard);
                builder.AppendLine("Th\u1EF1c t\u1EBF: " + Actual);
                builder.Append("H\u01B0\u1EDBng x\u1EED l\u00FD: " + Resolution);
                return builder.ToString();
            }
        }
    }
}
