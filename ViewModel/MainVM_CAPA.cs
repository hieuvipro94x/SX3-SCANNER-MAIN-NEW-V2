using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using System;

namespace SX3_SCANER.ViewModel
{
    internal partial class MainViewModel : ViewModelBase
    {
        private int _capaOkCount;
        private int _capaNgCount;
        private int _capaTargetQuantity;

        public int CapaOkCount
        {
            get { return _capaOkCount; }
        }

        public int CapaNgCount
        {
            get { return _capaNgCount; }
        }

        public int CapaTargetQuantity
        {
            get { return _capaTargetQuantity; }
        }

        public int CapaRemainingCount
        {
            get { return Math.Max(0, CapaTargetQuantity - CapaOkCount); }
        }

        public int CapaTotalAttempts
        {
            get { return CapaOkCount + CapaNgCount; }
        }

        public bool ProductionTargetReached
        {
            get
            {
                return CapaTargetQuantity > 0 &&
                    CapaOkCount >= CapaTargetQuantity;
            }
        }

        public double ProductionProgressPercent
        {
            get
            {
                if (CapaTargetQuantity <= 0) return 0.0;
                return Math.Round(
                    Math.Min(
                        100.0,
                        CapaOkCount * 100.0 / CapaTargetQuantity),
                    1);
            }
        }

        public double YieldPercent
        {
            get
            {
                return CapaTotalAttempts <= 0
                    ? 0.0
                    : Math.Round(
                        CapaOkCount * 100.0 / CapaTotalAttempts,
                        1);
            }
        }

        public double DefectPercent
        {
            get
            {
                return CapaTotalAttempts <= 0
                    ? 0.0
                    : Math.Round(
                        CapaNgCount * 100.0 / CapaTotalAttempts,
                        1);
            }
        }

        public string ProductionProgressText
        {
            get { return string.Format("{0:0.0}%", ProductionProgressPercent); }
        }

        public string YieldPercentText
        {
            get { return string.Format("{0:0.0}%", YieldPercent); }
        }

        public string DefectPercentText
        {
            get { return string.Format("{0:0.0}%", DefectPercent); }
        }

        public string CapaCountSummaryText
        {
            get
            {
                return string.Format(
                    "OK {0}  /  NG {1}  /  TARGET {2}",
                    CapaOkCount,
                    CapaNgCount,
                    CapaTargetQuantity);
            }
        }

        private void RefreshCapaFromDataSource()
        {
            try
            {
                CapaDailySummary summary =
                    _scanHistoryRepository.GetDailyCapaSummary(
                        SelectedDate,
                        SelectedPartNumber,
                        SelectedQuantity);

                ApplyCapaSummary(summary);
            }
            catch (Exception ex)
            {
                StartupManager.SetStatus(
                    "Khong doc duoc tong ket CAPA theo ngay: " + ex.Message);
                ApplyCapaSummary(new CapaDailySummary
                {
                    TargetQuantity = Math.Max(0, SelectedQuantity)
                });
            }
        }

        private void ApplyCapaSummary(CapaDailySummary summary)
        {
            int okCount = Math.Max(0, summary?.OkCount ?? 0);
            int ngCount = Math.Max(0, summary?.NgCount ?? 0);
            int targetQuantity = Math.Max(
                0,
                summary?.TargetQuantity ?? SelectedQuantity);

            bool okChanged = _capaOkCount != okCount;
            bool ngChanged = _capaNgCount != ngCount;
            bool targetChanged = _capaTargetQuantity != targetQuantity;

            _capaOkCount = okCount;
            _capaNgCount = ngCount;
            _capaTargetQuantity = targetQuantity;

            if (okChanged) OnPropertyChanged(nameof(CapaOkCount));
            if (ngChanged) OnPropertyChanged(nameof(CapaNgCount));
            if (targetChanged) OnPropertyChanged(nameof(CapaTargetQuantity));

            if (okChanged || targetChanged)
            {
                OnPropertyChanged(nameof(CapaRemainingCount));
                OnPropertyChanged(nameof(ProductionTargetReached));
                OnPropertyChanged(nameof(ProductionProgressPercent));
                OnPropertyChanged(nameof(ProductionProgressText));
            }

            if (okChanged || ngChanged)
            {
                OnPropertyChanged(nameof(CapaTotalAttempts));
                OnPropertyChanged(nameof(YieldPercent));
                OnPropertyChanged(nameof(DefectPercent));
                OnPropertyChanged(nameof(YieldPercentText));
                OnPropertyChanged(nameof(DefectPercentText));
            }

            if (okChanged || ngChanged || targetChanged)
                OnPropertyChanged(nameof(CapaCountSummaryText));
        }
    }
}
