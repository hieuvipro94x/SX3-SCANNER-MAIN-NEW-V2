using System;

namespace SX3_SCANER.Model
{
    internal sealed class HistoryDataRow
    {
        public int RowIndex { get; set; }

        public int ID { get; set; }

        public DateTime? ScanTime { get; set; }

        public string DataSource { get; set; }

        public string BoxName { get; set; }

        public string ProductPartNumber { get; set; }

        public string ProductPartName { get; set; }

        public string SealNo { get; set; }

        public string LotNo { get; set; }

        public string ScanData { get; set; }

        public bool? ScanResult { get; set; }

        public string ResultText { get; set; }

        public string ScanMessage { get; set; }

        public string ScanWorker { get; set; }

        public string BoxTypeText { get; set; }

        public string BoxTypeDB { get; set; }

        public bool IsOddBox { get; set; }

        public bool BoxComplete
        {
            get { return ScanResult == true; }
        }

        public bool IsPartialBox
        {
            get { return IsOddBox; }
        }

        public string BoxType
        {
            get { return BoxTypeDB; }
        }
    }
}
