namespace SAMe_VI.Repository.DataTableStruct
{
    internal interface IValidationStruct
    {
        internal string Type { get; set; } 
        internal bool IsValid { get; set; }
        internal string? Messages { get; set; }
    }
}
