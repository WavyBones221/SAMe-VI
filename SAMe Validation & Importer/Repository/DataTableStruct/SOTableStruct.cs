namespace SAMe_VI.Repository.DataTableStruct
{
#pragma warning disable
    internal sealed class SOLineValidationStruct : IValidationStruct
    {
        public int LineNumber { get; set; }
        public string Type { get; set; }
        public bool IsValid { get; set; }
        public string? Messages { get; set; }
    }
    internal sealed class SOHeaderValidationStruct : IValidationStruct
    {
        public string Type { get; set; }
        public bool IsValid { get; set; }
        public string? Messages { get; set; }


    }
#pragma warning restore
}
