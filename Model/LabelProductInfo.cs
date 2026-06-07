namespace SX3_SCANER.Model
{
    internal class LabelProductInfo
    {
        public int ID { get; set; }
        public string Car { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string CodeStringForm { get; set; } = string.Empty;
        public string CodePrefix { get; set; } = string.Empty;
        public string CodeSuffix { get; set; } = string.Empty;
        public int CodeLength { get; set; }
        public int BoxQuantity { get; set; }

        public bool IsValid => !string.IsNullOrWhiteSpace(PartNumber)
                            && !string.IsNullOrWhiteSpace(PartName)
                            && CodeLength > 0
                            && BoxQuantity > 0;
    }
}
