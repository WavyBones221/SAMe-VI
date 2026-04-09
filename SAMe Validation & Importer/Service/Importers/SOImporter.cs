using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SamsQueryLibrary.Services;
using System.Data;

namespace SAMe_VI.Service.Importers
{
    internal class SOImporter() : IImporter<SalesOrder>
    {
        public async Task<bool> ImportAsync(SalesOrder so, CancellationToken ct)
        {
            try
            {
                //status = 0 for testing 
                //status update = 10 for testing also
                List<DeliveryLocationLine> deliveryLocations = so.DeliveryLocation;
                DeliveryLocationLine deliveryLocation;
                List<SalesOrderLine> orderLines = so.Items;

                string customerCode = so.CustomerCode.Value;
                string orderNumber = so.OrderNumber.Value;
                string fileBlobId = so.FileBlobID;

                Console.WriteLine($"Import Begining For SalesOrder : {orderNumber}, Customer : {customerCode}");

                string? PDFAttachment = $"{Configuration.AttachmentTempDir}\\{fileBlobId}";

                if (!File.Exists(PDFAttachment) || string.IsNullOrWhiteSpace(fileBlobId))
                {
                    PDFAttachment = null;
                }

                using (SqlConnection con = new(Configuration.ConnectionString))
                {
                    //delivery
                    if (!deliveryLocations.IsNullOrEmpty())
                    {
                        if (deliveryLocations.Count > 1)
                        {
                            //This is technically possible due to how the Azure OCR works but it is very very unlikely, unless South Tees find a way
                            Console.WriteLine($" -- More Than One Delivery Address Found :");
                            foreach (DeliveryLocationLine deliveryLocationLine in deliveryLocations)
                            {
                                Console.WriteLine($" --- AddressLine1 -> {deliveryLocationLine.AddressLine1}");
                                Console.WriteLine($" --- PostCode -> {deliveryLocationLine.PostCode}{Environment.NewLine}");
                            }
                            Console.WriteLine($"Choosing first entry");
                        }

                        deliveryLocation = deliveryLocations.First();
                    }
                    else
                    {
                        throw new InvalidOperationException("Delivery Location cannot be NULL");
                    }

                    if (HandleDeliveryAddress(deliveryLocation, out long DeliveryContactID, con, customerCode))
                    {
                        if (HandleSalesOrderHeader(so, out long SalesOrderID, con, DeliveryContactID, PDFAttachment))
                        {
                            //order lines
                            Console.WriteLine($" --- Processed and Added Header {SalesOrderID}");

                            for (int i = 0; i < orderLines.Count; i++)
                            {
                                bool hasNext = i + 1 < orderLines.Count;

                                SqlParameter currentCode = SQL.CreateParam("ProductCode", orderLines[i].SupplierProductCode.Value);

                                bool currentIsEmbroidery = RunStoredProcedure<bool>($"{Configuration.DatabaseName}.[dbo].[SAMe_Validation_IsProductCodeEmbroidery]", con, "IsEmbroidery", currentCode);

                                if (currentIsEmbroidery)
                                {
                                    if (i == 0)
                                    {
                                        throw new InvalidOperationException($"Can't have embroidery : {orderLines[i].SupplierProductCode.Value} on first line of a sales order");
                                    }

                                    SqlParameter previousCode = SQL.CreateParam("ProductCode", orderLines[i - 1].SupplierProductCode.Value);
                                    bool previousIsEmbroidery = RunStoredProcedure<bool>($"{Configuration.DatabaseName}.[dbo].[SAMe_Validation_IsProductCodeEmbroidery]", con, "IsEmbroidery", previousCode);
                                    SalesOrderLine previous = orderLines[i - 1];

                                    if (previousIsEmbroidery)
                                    {
                                        throw new InvalidOperationException($"Cannot have two Embroidery lines after eachother {previous.SupplierProductCode.Value} & {orderLines[i].SupplierProductCode.Value}");
                                    }

                                    if (previous.Quantity.Value != orderLines[i].Quantity.Value)
                                    {
                                        throw new InvalidOperationException($"Order line for {previous.SupplierProductCode.Value} with {previous.Quantity.Value} inconsistent with Order line Embroidery Order line for {orderLines[i].SupplierProductCode.Value} with {orderLines[i].Quantity.Value}");
                                    }
                                }
                                SalesOrderLine? possibleEmbroideryLine = null;
                                if (hasNext)
                                {
                                    possibleEmbroideryLine = orderLines[i + 1];
                                }

                                if (HandleSalesOrderLine(orderLines[i], SalesOrderID, con, possibleEmbroideryLine, out long salesOrderLineID))
                                {
                                    Console.WriteLine($" ---- Processed and Added Line {salesOrderLineID}");
                                    continue;
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Failed retrieving an Sales Order Line ID for {orderNumber} : Line Number -> {orderLines[i].LineNumber.Value}");
                                }
                            }

                            // ATTENTION - This stored procedure is responsible for posting the order to be picekd up by the live system, if testing PLEASE comment out. 
                            //return RunStoredProcedure<bool>($"{Configuration.DatabaseName}.dbo.SAMe_Importer_Sign_off_Sales_Order", con, "IsSuccess", SQL.CreateParam("SalesOrderID", SalesOrderID, SqlDbType.BigInt));
                            return true;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Failed retrieving the Sales Order ID for {orderNumber}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed retrieving the Delivery Location ID for {orderNumber}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Importing Sales Order : {so.OrderNumber.Value} for Customer : {so.CustomerCode.Value}");
                Console.WriteLine(ex.ToString());

                if (ex is SqlException sqlException)
                {
                    Console.WriteLine($"SqlException.Number: {sqlException.Number}");
                    Console.WriteLine($"SqlException.State: {sqlException.State}");
                    Console.WriteLine($"SqlException.Class: {sqlException.Class}");
                    Console.WriteLine($"SqlException.Procedure: {sqlException.Procedure}");
                    Console.WriteLine($"SqlException.LineNumber: {sqlException.LineNumber}");
                    Console.WriteLine($"SqlException.Server: {sqlException.Server}");

                    foreach (SqlError error in sqlException.Errors)
                    {
                        Console.WriteLine($"  Error {error.Number}, State {error.State}, Class {error.Class}, Line {error.LineNumber}: {error.Message}");
                    }
                }

                return false;
            }
        }

        private static bool HandleDeliveryAddress(DeliveryLocationLine dl, out long DeliveryContactID, SqlConnection con, string customerCode)
        {
            SqlParameter[] p = [
                SQL.CreateParam("address1", dl.AddressLine1.Value),
                SQL.CreateParam("address2", dl.AddressLine2.Value),
                SQL.CreateParam("address3", dl.AddressLine3.Value),
                SQL.CreateParam("address4", dl.AddressLine4.Value),
                SQL.CreateParam("postCode", dl.PostCode.Value),
                SQL.CreateParam("customerCode", customerCode)
                ];

            DeliveryContactID = RunStoredProcedure<long>($"{Configuration.DatabaseName}.dbo.SAMe_Import_DeliveryAddress", con, parameters: p);
            return DeliveryContactID != default;
        }

        private static bool HandleSalesOrderLine(SalesOrderLine orderLine, long SalesOrderID, SqlConnection con, SalesOrderLine? nextSalesOrderLine, out long SalesOrderLineID)
        {
            List<string> deliveryContact = [];

            if (!string.IsNullOrWhiteSpace(orderLine.DeliveryInstructions))
            {
                deliveryContact.Add("Delivery Instruction: " + orderLine.DeliveryInstructions);
            }

            if (!string.IsNullOrWhiteSpace(orderLine.DeliveryContact))
            {
                deliveryContact.Add("Delivery Contact: " + orderLine.DeliveryContact);
            }

            bool embroidery = false;
            if (nextSalesOrderLine != null) 
            {
                SqlParameter npc = SQL.CreateParam("ProductCode", nextSalesOrderLine.SupplierProductCode.Value);
                embroidery = RunStoredProcedure<bool>($"{Configuration.DatabaseName}.[dbo].[SAMe_Validation_IsProductCodeEmbroidery]", con, "IsEmbroidery", npc);
            }
            string? rawContactName = orderLine.DeliveryContact;
            string? contactName = string.IsNullOrWhiteSpace(rawContactName) ? null : rawContactName;

            string? diText = deliveryContact.Count == 0 ? null : string.Join(", ", deliveryContact);

            SqlParameter[] p =
            [
                SQL.CreateParam("ProductCode", orderLine.SupplierProductCode.Value),
                SQL.CreateParam("Quantity", orderLine.Quantity.Value, SqlDbType.Int),
                SQL.CreateParam("UnitPrice", orderLine.UnitPrice.Value, SqlDbType.Decimal),
                SQL.CreateParam("SellingUnits", orderLine.UnitOfIssue.Value),

                //Shud update the library to be able do nullable params
                new SqlParameter("ContactName", SqlDbType.NVarChar)
                {
                    Value = (object?)contactName ?? DBNull.Value
                },

                new SqlParameter("DI", SqlDbType.NVarChar)
                {
                    Value = (object?)diText ?? DBNull.Value
                },

                SQL.CreateParam("SalesOrderID", SalesOrderID, SqlDbType.BigInt),
                SQL.CreateParam("EmbroideryRequired", embroidery, SqlDbType.Bit),
                SQL.CreateParam("Total", orderLine.Quantity.Value * orderLine.UnitPrice.Value, SqlDbType.Decimal)
            ];

            if (embroidery)
            {
                SqlParameter[] pe =
                [
                    SQL.CreateParam("EmbroideryProduct", nextSalesOrderLine!.SupplierProductCode.Value),
                    SQL.CreateParam("EmbroideryDescription", nextSalesOrderLine.Description)
                ];

                p = [.. p, .. pe];
            }

            SalesOrderLineID = RunStoredProcedure<long>($"{Configuration.DatabaseName}.dbo.SAMe_Import_SalesOrderLine", con, parameters: p);

            return SalesOrderLineID != default;
        }

        private static bool HandleSalesOrderHeader(SalesOrder salesOrder, out long SalesOrderID, SqlConnection con, long DeliveryContactID, string? AttachmentPath)
        {
            SqlParameter[] p = [
                SQL.CreateParam("SalesOrderNumber", salesOrder.OrderNumber.Value),
                SQL.CreateParam("OrderDate", salesOrder.OrderDate.ToString("yyyy-MM-dd HH:mm:ss.fff"), SqlDbType.DateTime),
                SQL.CreateParam("CustomerCode", salesOrder.CustomerCode.Value),
                SQL.CreateParam("DeliveryContactID", DeliveryContactID,SqlDbType.BigInt)
                ];

            if (AttachmentPath is not null)
            {
                p = [.. p, SQL.CreateParam("IQ_PathToDocument", AttachmentPath)];
            }

            SalesOrderID = RunStoredProcedure<long>($"{Configuration.DatabaseName}.dbo.SAMe_Import_SalesOrder", con, parameters: p);
            return SalesOrderID != default;
        }

        private static T? RunStoredProcedure<T>(string storedProcedure, SqlConnection con, string? columnName = "ID", params SqlParameter[]? parameters) where T : IConvertible
        {
            bool openedHere = false;

            if (con.State == ConnectionState.Closed)
            {
                con.Open();
                openedHere = true;
            }

            try
            {
                using (SqlCommand cmnd = new(storedProcedure, con))
                {
                    cmnd.CommandType = CommandType.StoredProcedure;

                    if (parameters != null && parameters.Length > 0)
                    {
                        cmnd.Parameters.AddRange(parameters);
                    }
                    if (columnName is not null)
                    {
                        using (SqlDataReader reader = cmnd.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            if (!reader.Read())
                            {
                                throw new InvalidOperationException($"Stored procedure : '{storedProcedure}' returned no rows.");
                            }

                            int ordinal = reader.GetOrdinal(columnName);
                            object rawValue = reader.GetValue(ordinal);

                            if (rawValue == DBNull.Value)
                            {
                                return default;
                            }

                            //usually the case
                            if (typeof(T) == typeof(long))
                            {
                                long value = Convert.ToInt64(rawValue);
                                return (T)(object)value;
                            }

                            T castValue = (T)Convert.ChangeType(rawValue, typeof(T));
                            return castValue;
                        }
                    }
                    else
                    {
                        cmnd.ExecuteNonQuery();
                        return default;
                    }
                }
            }
            finally
            {
                if (openedHere)
                {
                    con.Close();
                }
            }
        }
    }
}