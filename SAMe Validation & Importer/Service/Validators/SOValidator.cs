using Microsoft.Data.SqlClient;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SAMe_VI.Repository;
using SAMe_VI.Repository.DataTableStruct;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.Reflection;

namespace SAMe_VI.Service.Validators
{
    internal sealed class SOValidator(ISORepository repo) : ConfidenceValidatorBase<SalesOrder>, IValidator<SalesOrder>
    {
        private readonly ISORepository _repo = repo;

        protected override async Task<ValidationResult> PostValidateAsync(ValidationResult r, SalesOrder doc, CancellationToken ct)
        {
            DataTable dtH = _repo.ValidateOrderHeaders(doc);
            List<string> SOHValidatedFields = GetValidatedFieldTypes(doc);
            List<SOHeaderValidationStruct> SOH = MapToList<SOHeaderValidationStruct>(dtH);

            foreach (SOHeaderValidationStruct headerMessage in SOH) 
            {
                if (!headerMessage.IsValid)
                {
                    if (SOHValidatedFields.Any(x => string.Equals(x, headerMessage.Type, StringComparison.OrdinalIgnoreCase)))
                    {
                        //In User We Trust
                        r.AddWarningForPath(headerMessage.Type, headerMessage.Messages ?? "Big problem");
                    }
                    else 
                    {
                        r.AddForPath(headerMessage.Type, headerMessage.Messages ?? "Big problem");
                    }
                }
            }

            DataTable dtL = _repo.ValidateOrderLines(doc.Items);
            List<string> SOLValidatedFields = GetValidatedFieldTypesDeep(doc.Items);
            List<SOLineValidationStruct> SOL = MapToList<SOLineValidationStruct>(dtL);

            foreach (SOLineValidationStruct lineMessage in SOL) 
            {
                if (!lineMessage.IsValid)
                {
                    if (SOLValidatedFields.Any(x => string.Equals(x, lineMessage.Type, StringComparison.OrdinalIgnoreCase)))
                    {
                        //In User We Trust
                        r.AddWarningForPath($"{lineMessage.Type}_{lineMessage.LineNumber}", lineMessage.Messages ?? "Big problem");
                    }
                    else 
                    {
                        r.AddForPath($"{lineMessage.Type}_{lineMessage.LineNumber}", lineMessage.Messages ?? "Big problem");
                    }
                }
            }


            //note this can handle more than one delivery location, since the analyser can in theory extract more than one delivery location
            //This needs to be accounted for later on in the code, either just takeing the first location as the main
            //or comparing both against what is in intact and choosing the one that matches what we have on file
            DataTable dtD = _repo.ValidateDeliveryLocations(doc.DeliveryLocation);
            List<string> SODValidatedFields = GetValidatedFieldTypesDeep(doc.DeliveryLocation);
            List<SOLineValidationStruct> SOD = MapToList<SOLineValidationStruct>(dtD);

            foreach (SOLineValidationStruct deliveryLineMessage in SOD) 
            {
                if (!deliveryLineMessage.IsValid) 
                {
                    if (SODValidatedFields.Any(x => string.Equals(x, deliveryLineMessage.Type, StringComparison.OrdinalIgnoreCase)))
                    {
                        r.AddWarningForPath($"{deliveryLineMessage.Type}_{deliveryLineMessage.LineNumber}", deliveryLineMessage.Messages ?? "Big problem");
                    }
                    else 
                    {
                        r.AddForPath($"{deliveryLineMessage.Type}_{deliveryLineMessage.LineNumber}", deliveryLineMessage.Messages ?? "Big problem");
                    }
                }
                if (doc.DeliveryLocation.Count > 1)
                {
                    //unhandled for now, as this will likely not happen but it is possible
                    r.AddForPath($"{deliveryLineMessage.Type}_{deliveryLineMessage.LineNumber}", "The analyser has extracted more than one delivery location" ?? "Big problem");
                }
            }

            //if any of the orderlines (doc.items) has a non null or string.empty value for requiredEmbroidery addForPath
            if (doc.Items != null && doc.Items.Any(l => !string.IsNullOrWhiteSpace(l.RequiredEmbroidery)))
            {
                foreach (SalesOrderLine line in doc.Items.Where(l => !string.IsNullOrWhiteSpace(l.RequiredEmbroidery)))
                {
                    string lineNo = line.LineNumber != null && line.LineNumber.Value != null ? line.LineNumber.Value : string.Empty;
                    r.AddForPath($"RequiredEmbroidery_{lineNo}", "embroidery present, needs manual review");
                }
            }
            return r;
        }

        private static List<T> MapToList<T>(DataTable table) where T : new()
        {
            List<T> list = [];

            foreach (DataRow row in table.Rows)
            {
                T item = new();

                foreach (PropertyInfo prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (table.Columns.Contains(prop.Name))
                    {
                        object? value = row[prop.Name];

                        if (value == DBNull.Value)
                        {
                            value = null;
                        }
                        else
                        {
                            if (prop.PropertyType == typeof(int))
                            {
                                value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                            }
                            else if (prop.PropertyType == typeof(bool))
                            {
                                value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                            }
                            else if (prop.PropertyType == typeof(string))
                            {
                                value = Convert.ToString(value, CultureInfo.InvariantCulture);
                            }
                        }

                        prop.SetValue(item, value);
                    }
                }

                list.Add(item);
            }

            return list;
        }



        private static List<string> GetValidatedFieldTypesDeep(object obj)
        {
            List<string> results = [];

            if (obj == null)
            {
                return results;
            }

            if (obj is System.Collections.IEnumerable collection && obj is not string)
            {
                foreach (object item in collection)
                {
                    results.AddRange(GetValidatedFieldTypesDeep(item));
                }

                return results;
            }

            //Otherwise treat it as a single object
            results.AddRange(GetValidatedFieldTypes(obj));
            return results;
        }


        private static List<string> GetValidatedFieldTypes(object obj)
        {
            List<string> results = [];

            PropertyInfo[] props = obj.GetType().GetProperties();

            foreach (PropertyInfo prop in props)
            {
                Type type = prop.PropertyType;

                bool isFieldValue =
                    type.IsGenericType &&
                    type.GetGenericTypeDefinition() == typeof(FieldValue<>);

                if (!isFieldValue)
                {
                    continue;
                }

                object? wrapper = prop.GetValue(obj);
                if (wrapper == null)
                {
                    continue;
                }

                //isValidatable shouldnt be null here ever since its automatically set to false, but for the sake of keeping this abstract, it does do this through reflection.
                //if you find a way or want to waste time on making this more efficient by not using reflection, be my guest
                //but it would require changing the structure of both GetValidatedFieldTypes(object obj) and GetValidatedFieldTypesDeep(object obj).

                PropertyInfo? validatedProp = wrapper.GetType().GetProperty("userValidated");
                PropertyInfo? isValidatable = wrapper.GetType().GetProperty("userValidatable");
                PropertyInfo? typeProp = wrapper.GetType().GetProperty("Type");

                if (validatedProp == null || typeProp == null || isValidatable == null)
                {
                    continue;
                }

                bool? validated = validatedProp.GetValue(wrapper) as bool?;
                bool? validatable = isValidatable.GetValue(wrapper) as bool?;
                string? typeName = typeProp.GetValue(wrapper) as string;

                if (validated == true && !string.IsNullOrWhiteSpace(typeName) && validatable == true)
                {
                    results.Add(typeName);
                }
            }

            return results;
        }


    }
}