namespace SAMe_VI.Object.Models

{

    //Independant attribute values assigned here
    /*
     *
     * Currently all confidence scores are lower than 0.7 on average, 
     * in the beggining every order is going to be manually checked so these values are fine
     * hopefully training will improve the scores of these fields
     * and when it does it can automatically send itself to live importing into the dataset
     *
     * This also helps for new models as at first thier scores will be low enough to at least
     * trigger a warning.
     *
     * @TODO calculate an average confidence score for a document and flag it original document type & layout for training
     *
     */

    internal sealed record SalesOrderRaw(
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> OrderNumber,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<DateTime> OrderDate,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> DeliveryLocation,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> InvoiceTo,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> InvoiceContactEmail,
        [property: ValidateChildren(MinCount = 1, ItemName = "OrderLines")]
        ConfidenceValue<List<SalesOrderLineRaw>> OrderLines,
        [property: PositiveNumber]
        ConfidenceValue<decimal> TotalValue,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> Customer,
        [property: RequiredField]
        ConfidenceValue<string> FileBlobID,
        [property: RequiredField]
        ConfidenceValue<string> Operation
    );

    internal sealed record SalesOrderLineRaw(
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> LineNumber,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> SupplierProductCode,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> Description,
        [property: PositiveNumber]
        ConfidenceValue<decimal> Quantity,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> UnitOfIssue,
        [property: PositiveNumber]
        ConfidenceValue<decimal> UnitPrice,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> DeliveryContact,
        [property: RequiredField(HardMin = 0.75, SoftMin = 0.90)]
        ConfidenceValue<string> DeliveryInstructions,
        [property: RequiredField(HardMin = 1.75, SoftMin = 1.90)]
        ConfidenceValue<string?> RequiredEmbroidery
    );

    public sealed record SalesOrder(

            FieldValue<string> OrderNumber,
            DateTime OrderDate,
            FieldValue<string> DeliveryLocation,
            FieldValue<string>? CompanyName,
            FieldValue<string> AddressLine1,
            FieldValue<string>? AddressLine2,
            FieldValue<string> AddressLine3,
            FieldValue<string> PostCode,
            string InvoiceTo,
            string InvoiceContactEmail,
            List<SalesOrderLine> Items,
            decimal TotalValue,
            string Customer,
            string FileBlobID,
            FieldValue<string> CustomerCode
        );

    public sealed record SalesOrderLine(
        FieldValue<string> LineNumber,
        FieldValue<string> SupplierProductCode,
        string Description,
        FieldValue<decimal> Quantity,
        FieldValue<string> UnitOfIssue,
        FieldValue<decimal> UnitPrice,
        string DeliveryContact,
        string DeliveryInstructions,
        FieldValue<string>? RequiredEmbroidery,
        FieldValue<string> CustomerCode
    );

    public sealed record FieldValue<T>(
        T Value, //value extracted by the model
        string Type, //Binds to error messages returned by the validation repo
        bool? userValidated, //boolean for users to override errors -  this can be set outside of this solution as it is read from the object, can be ommitted from the input and will default to false, or null, same affect
        bool? userValidatable = false //provides control to this solution to if a field can be validated by the user or if it should be locked behind an error regardless of user validation
        );
}

