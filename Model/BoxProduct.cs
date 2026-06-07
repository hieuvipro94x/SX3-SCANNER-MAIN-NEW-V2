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

        public string BoxTypeText
        {
            get
            {
                if (!BoxComplete) return string.Empty;
                return IsPartialBox ? "TH\u00D9NG L\u1EBA" : "TH\u00D9NG \u0110\u1EE6";
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
