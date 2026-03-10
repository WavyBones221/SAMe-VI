using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SAMe_VI.Object.Models;

namespace SAMe_VI.Service.Mapping
{
    internal static partial class SalesOrderMapper
    {
        private static readonly string PostCodePattern = @"([A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2})$";

        public static SalesOrder ToDomain(SalesOrderRaw raw)
        {
            List<SalesOrderLine> items = [];

            if (raw.OrderLines.Value is not null)
            {
                foreach (SalesOrderLineRaw line in raw.OrderLines.Value)
                {
                    SalesOrderLine item = new (
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

                        RequiredEmbroidery: StringField(
                            line.RequiredEmbroidery.Value,
                            "Embroidery",
                            line.RequiredEmbroidery.userValidated,
                            userValidatable: true
                        ),

                        CustomerCode: new FieldValue<string>(
                            raw.Operation.Value ?? string.Empty,
                            Type: string.Empty,
                            userValidated: true,
                            userValidatable: true
                        )
                    );

                    items.Add(item);
                }
            }

            string deliveryString = raw.DeliveryLocation.Value ?? string.Empty;
            DeliveryParts delivery = ParseDelivery(deliveryString);

            SalesOrder mapped = new (
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

                DeliveryLocation: StringField(
                    raw.DeliveryLocation.Value,
                    "DeliveryLocation",
                    raw.DeliveryLocation.userValidated,
                    clean: false,
                    userValidatable: true
                ),

                CompanyName: StringField(
                    delivery.CompanyName ?? string.Empty,
                    string.Empty,
                    raw.DeliveryLocation.userValidated,
                    userValidatable: true
                ),

                AddressLine1: StringField(
                    delivery.AddressLine1 ?? delivery.AddressLine2 ?? delivery.AddressLine3 ?? string.Empty,
                    string.Empty,
                    raw.DeliveryLocation.userValidated,
                            userValidatable: true
                ),

                AddressLine2: StringField(
                    delivery.AddressLine2 ?? string.Empty,
                    string.Empty,
                    raw.DeliveryLocation.userValidated,
                    userValidatable: true
                ),

                AddressLine3: StringField(
                    delivery.AddressLine3 ?? string.Empty,
                    string.Empty,
                    raw.DeliveryLocation.userValidated,
                    userValidatable: true
                ),

                PostCode: StringField(
                    delivery.RegexPostCode ?? delivery.PostcodeCandidate ?? string.Empty,
                    string.Empty,
                    raw.DeliveryLocation.userValidated,
                    userValidatable: true
                ),

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
                    userValidated: true,
                    userValidatable: true
                )
            );

            return mapped;
        }

        private static DeliveryParts ParseDelivery(string deliveryString)
        {
            string? postcode = null;
            string? companyName = null;
            string? addressLine1 = null;
            string? addressLine2 = null;
            string? addressLine3 = null;

            string? regexPostcode = ExtractPostCode(deliveryString);

            if (!string.IsNullOrWhiteSpace(deliveryString))
            {
                string[] delsplit = deliveryString.Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (delsplit.Length > 0)
                {
                    string lastLine = delsplit[^1];

                    if (lastLine != regexPostcode)
                    {
                        Console.WriteLine("PostCode does not match REGEX for this order");
                    }

                    int assignment = 0;

                    for (int i = delsplit.Length - 1; i >= 0; i--)
                    {
                        switch (assignment)
                        {
                            case 0: postcode = delsplit[i]; break;
                            case 1: addressLine3 = delsplit[i]; break;
                            case 2: addressLine2 = delsplit[i]; break;
                            case 3: addressLine1 = delsplit[i]; break;
                            case 4: companyName = delsplit[i]; break;
                            default: i = -1; break;
                        }

                        assignment++;
                    }

                    if (delsplit.Length >= 5)
                    {
                        companyName = delsplit[0];
                    }
                }
            }

            DeliveryParts parts = new()
            {
                CompanyName = companyName,
                AddressLine1 = addressLine1,
                AddressLine2 = addressLine2,
                AddressLine3 = addressLine3,
                PostcodeCandidate = postcode,
                RegexPostCode = regexPostcode
            };

            return parts;
        }

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

            FieldValue<string> field = new (ret, Type: type, userValidated: validated, userValidatable: userValidatable);
            return field;
        }

        private static FieldValue<decimal> DecimalField(decimal value, string type, bool? validated, bool? userValidatable = false)
        {
            FieldValue<decimal> field = new (value, Type: type, userValidated: validated, userValidatable: userValidatable);
            return field;
        }

        private static string Clean(string? input)
        {
            string source = input ?? string.Empty;
            string result = EVRegex().Replace(source, " ");
            return result;
        }

        private static string? ExtractPostCode(string? deliveryLocation)
        {
            if (deliveryLocation is null)
            {
                return null;
            }

            Match match = Regex.Match(deliveryLocation, PostCodePattern);

            if (match.Success)
            {
                return match.Value;
            }

            return null;
        }

        private sealed class DeliveryParts
        {
            public string? CompanyName { get; set; }
            public string? AddressLine1 { get; set; }
            public string? AddressLine2 { get; set; }
            public string? AddressLine3 { get; set; }
            public string? PostcodeCandidate { get; set; }
            public string? RegexPostCode { get; set; }
        }

        [GeneratedRegex(@"\r?\n")]
        private static partial Regex EVRegex();
    }
}