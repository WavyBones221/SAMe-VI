using Microsoft.Data.SqlClient;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace SAMe_VI.Service.Mapping
{
    internal static partial class SalesOrderMapper
    {
        private const string PostCodePattern = @"([A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2})$";

        public static SalesOrder ToDomain(SalesOrderRaw raw)
        {
            List<SalesOrderLine> items = [];
            List<DeliveryLocationLine> dls = [];

            if (raw.OrderLines.Value is not null)
            {
                foreach (SalesOrderLineRaw line in raw.OrderLines.Value)
                {
                    SalesOrderLine item = new(
                        LineNumber: StringField(
                            line.LineNumber.Value,
                            string.Empty,
                            line.LineNumber.userValidated,
                            userValidatable: true
                        ),

                        SupplierProductCode: StringField(
                            line.SupplierProductCode.Value,
                            "SupplierProductCode",
                            line.SupplierProductCode.userValidated
                        ),

                        Description: Clean(
                            line.Description.Value
                        ),

                        Quantity: DecimalField(
                            line.Quantity.Value,
                            string.Empty,
                            line.Quantity.userValidated,
                            userValidatable: true
                        ),

                        UnitOfIssue: StringField(
                            line.UnitOfIssue.Value,
                            "UnitOfIssue",
                            line.UnitOfIssue.userValidated,
                            userValidatable: true
                        ),

                        UnitPrice: DecimalField(
                            line.UnitPrice.Value,
                            "UnitPrice",
                            line.UnitPrice.userValidated,
                            userValidatable: true
                        ),

                        DeliveryContact: Clean(
                            line.DeliveryContact.Value
                        ),

                        DeliveryInstructions: Clean(
                            line.DeliveryInstructions.Value
                        ),

                        RequiredEmbroidery: Clean(
                            line.RequiredEmbroidery.Value
                        ),

                        CustomerCode: new FieldValue<string>(
                            raw.Operation.Value ?? string.Empty,
                            Type: string.Empty,
                            userValidated: false,
                            userValidatable: true
                        ),
                        userValidated: BoolField(
                            line.UserValidated.Value,
                            "UserValidated",
                            line.UserValidated.userValidated,
                            userValidatable: true
                        )

                    );

                    items.Add(item);
                }
            }

            if (raw.DeliveryLocation.Value is not null)
            {
                foreach (DeliveryLocationLineRaw line in raw.DeliveryLocation.Value)
                {
                    DeliveryLocationLine dl = new(

                        AddressLine1: StringField(
                            line.AddressLine1.Value,
                            "AddressLine1",
                            line.AddressLine1.userValidated,
                            userValidatable: true

                        ),

                        AddressLine2: StringField(
                            line.AddressLine2.Value,
                            string.Empty,
                            line.AddressLine2.userValidated,
                            userValidatable: true

                        ),

                        AddressLine3: StringField(
                            line.AddressLine3.Value,
                            string.Empty,
                            line.AddressLine3.userValidated,
                            userValidatable: true

                        ),

                        AddressLine4: StringField(
                            line.AddressLine4.Value,
                            string.Empty,
                            line.AddressLine4.userValidated,
                            userValidatable: true

                        ),

                        PostCode: StringField(
                            line.PostCode.Value,
                            "PostCode",
                            line.PostCode.userValidated,
                            userValidatable: true

                        )
                    );
                    dls.Add(dl);
                }
            }

            SalesOrder mapped = new(
                OrderNumber: StringField(
                    raw.OrderNumber.Value,
                    "OrderNumber",
                    raw.OrderNumber.userValidated
                ),

                OrderDate:
                    raw.OrderDate.Value,

                Customer: Clean(
                    raw.Customer.Value
                ),

                Items:
                    items,

                DeliveryLocation: dls,

                InvoiceTo: Clean(
                    raw.InvoiceTo.Value
                ),

                InvoiceContactEmail: Clean(
                    raw.InvoiceContactEmail.Value
                ),

                TotalValue:
                    raw.TotalValue.Value,

                FileBlobID:
                    raw.FileBlobID.Value ?? string.Empty,

                CustomerCode: new FieldValue<string>(
                    raw.Operation.Value ?? string.Empty,
                    Type: string.Empty,
                    userValidated: false,
                    userValidatable: true
                ),
                DivisionCode: GetDivisionCode(raw.Operation.Value) ?? string.Empty
            );

            return mapped;
        }
        //make a MapperBase.cs and put these in that base method so other processes can use them
        private static FieldValue<string> StringField(string? value, string type, bool? validated, bool? userValidatable = false, bool clean = true)
        {
            string? ret;
            if (clean)
            {
                ret = Clean(value);
            }
            else
            {
                ret = value ?? string.Empty;
            }

            FieldValue<string> field = new(ret, Type: type, userValidated: validated, userValidatable: userValidatable);
            return field;
        }

        private static FieldValue<decimal> DecimalField(decimal value, string type, bool? validated, bool? userValidatable = false)
        {
            FieldValue<decimal> field = new(value, Type: type, userValidated: validated, userValidatable: userValidatable);
            return field;
        }

        private static FieldValue<bool> BoolField(bool? value, string type, bool? validated, bool? userValidatable = false)
        {
            bool ret = value ?? false;

            FieldValue<bool> field = new(ret, Type: type, userValidated: validated ?? false, userValidatable: userValidatable);
            return field;
        }

        private static string Clean(string? input)
        {
            string source = input ?? string.Empty;
            string result = EVRegex().Replace(source, " ");
            return result;
        }


        private static string? GetDivisionCode(string? customerCode)
        {
            if (string.IsNullOrWhiteSpace(customerCode))
            {
                return null;
            }

            using (SqlConnection connection = new(Configuration.ConnectionString))
            {
                using (SqlCommand command = new($"{Configuration.DatabaseName}.dbo.SAMe_GetCustomerDetails", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    SqlParameter parameter = new("@CustomerCode", SqlDbType.NVarChar, 50)
                    {
                        Value = customerCode
                    };
                    command.Parameters.Add(parameter);

                    connection.Open();

                    object? result = command.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                    {
                        return null;
                    }

                    return Convert.ToString(result);
                }
            }
        }


        [GeneratedRegex(@"\r?\n")]
        private static partial Regex EVRegex();
    }
}