using System;

namespace SX3_SCANER.Model
{
    internal class BoxProduct
    {
        public int ID { get; set; }
        public string BoxName { get; set; } = string.Empty;
        public string ProductPartName { get; set; } = string.Empty;
        public string ProductPartNumber { get; set; } = string.Empty;
        public string BoxSealNo { get; set; } = string.Empty;
        public int BoxQuantity { get; set; }
        public int BoxProgress { get; set; }
        public bool BoxComplete { get; set; }
        public string BoxWorker { get; set; } = string.Empty;
        public string BoxType { get; set; } = "OPEN";
        public bool IsPartialBox { get; set; }
        public DateTime? BoxDate { get; set; }
        public DateTime? ScanLabelDate { get; set; }
        public int ActualQty { get; set; }
        public int TargetQty { get; set; }

        public string BoxTypeText
        {
            get
            {
                if (string.Equals(
                    BoxType,
                    "CANCELLED",
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return "ĐÃ HỦY";
                }

                if (!BoxComplete) return string.Empty;
                return IsPartialBox ? "TH\u00D9NG L\u1EBA" : "TH\u00D9NG \u0110\u1EE6";
            }
        }

        public string BoxCompleteText
        {
            get
            {
                if (string.Equals(
                    BoxType,
                    "CANCELLED",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return "\u0110\u00E3 h\u1EE7y";
                }

                if (BoxComplete) return "\u0110\u00E3 \u0111\u00F3ng";

                int requiredQuantity = TargetQty > 0 ? TargetQty : BoxQuantity;
                int currentQuantity = ActualQty > 0 ? ActualQty : BoxProgress;

                if (currentQuantity <= 0) return "\u0110ang m\u1EDF";
                if (requiredQuantity > 0 && currentQuantity >= requiredQuantity)
                    return "\u0110\u1EE7 SL";

                return "Thi\u1EBFu h\u00E0ng";
            }
        }

        public string ProgressText
        {
            get
            {
                if (BoxQuantity <= 0) return $"{BoxProgress}";

                if (BoxComplete && IsPartialBox)
                {
                    return $"{BoxProgress}/{BoxQuantity}";
                }

                int completedBoxes = BoxProgress / BoxQuantity;
                int remainder = BoxProgress % BoxQuantity;

                if (remainder == 0)
                {
                    return $"{completedBoxes} thùng";
                }

                return $"{completedBoxes} thùng + {remainder}/{BoxQuantity}";
            }
        }
    }
}
