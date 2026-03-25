using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SamsQueryLibrary.Services;
using System.Data;
using System.Numerics;

namespace SAMe_VI.Service.Importers
{
    internal class SOImporter() : IImporter<SalesOrder>
    {
        public async Task<bool> ImportAsync(SalesOrder so, CancellationToken ct)
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
            
            SqlConnection con = new(Configuration.ConnectionString);
            
            //delivery
            if (!deliveryLocations.IsNullOrEmpty())
            {
                if (deliveryLocations.Count > 1)
                {
                    //This is technically possible but very very unlikely
                    Console.WriteLine($" -- More Than One Delivery Address Found :");
                    foreach (DeliveryLocationLine deliveryLocationLine in deliveryLocations)
                    {
                        Console.WriteLine($" --- AddressLine1 -> {deliveryLocationLine.AddressLine1}");
                        Console.WriteLine($" --- PostCode -> {deliveryLocationLine.PostCode}");
                        Console.WriteLine(Environment.NewLine);
                    }
                    Console.WriteLine($"Choosing first entry");
                }

                deliveryLocation = deliveryLocations.First();
            }
            else 
            { 
                throw new InvalidOperationException("Delivery Location cannot be NULL");
            }

            if (HandleDeliveryAddress(deliveryLocation, out BigInteger DeliveryContactID, con, customerCode))
            {
                if (HandleSalesOrderHeader(so, out BigInteger SalesOrderID, con, DeliveryContactID, PDFAttachment))
                {
                    //order lines
                    for (int i = 0; i < orderLines.Count; i++)
                    {
                        bool hasNext = i + 1 < orderLines.Count;

                        if (orderLines[i].Description == "EMBROIDERY")
                        {
                            if (i == 0)
                            {
                                throw new InvalidOperationException($"Can't have embroidery : {orderLines[i].SupplierProductCode.Value} on first line of a sales order");
                            }

                            SalesOrderLine previous = orderLines[i - 1];

                            if (previous.Description == "EMBROIDERY")
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

                        if (HandleSalesOrderLine(orderLines[i], SalesOrderID, con, customerCode, possibleEmbroideryLine, out BigInteger salesOrderLineID))
                        {
                            Console.WriteLine($" ---- Processed and Added Line {salesOrderLineID}");
                            continue;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Failed retrieving an Sales Order Line ID for {orderNumber} : Line Number -> {orderLines[i].LineNumber.Value}");
                        }
                    }
                    return RunStoredProcedure<bool>([SQL.CreateParam("SalesOrderID", SalesOrderID, SqlDbType.BigInt)],"dbo.SAMe_Importer_Sign_off_Sales_Order", con, "IsSuccess");
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


        private static bool HandleDeliveryAddress(DeliveryLocationLine dl, out BigInteger DeliveryContactID, SqlConnection con, string customerCode)
        {
            SqlParameter[] p = [
                SQL.CreateParam("address1", dl.AddressLine1.Value),
                SQL.CreateParam("address2", dl.AddressLine2.Value),
                SQL.CreateParam("address3", dl.AddressLine3.Value),
                SQL.CreateParam("address4", dl.AddressLine4.Value),
                SQL.CreateParam("postCode", dl.PostCode.Value),
                SQL.CreateParam("customerCode", customerCode)
                ];

            DeliveryContactID = RunStoredProcedure<BigInteger>(p, "dbo.SAMe_Import_DeliveryAddress", con);
            return DeliveryContactID != default;
        }

        private static bool HandleSalesOrderLine(SalesOrderLine orderLine, BigInteger SalesOrderID, SqlConnection con, string customerCode, SalesOrderLine? nextSalesOrderLine, out BigInteger SalesOrderLineID)
        {

            string[] deliveryContact = [$"Delivery Contact: {orderLine.DeliveryContact}", $"Delivery Instruction :{orderLine.DeliveryInstructions}"];

            SqlParameter[] p = [
                SQL.CreateParam("ProductCode",orderLine.SupplierProductCode.Value),
                SQL.CreateParam("Quantity",orderLine.Quantity.Value, SqlDbType.Float),
                SQL.CreateParam("UnitPrice",orderLine.UnitPrice.Value, SqlDbType.Float),
                SQL.CreateParam("UnitOfIssue",orderLine.UnitOfIssue.Value),
                SQL.CreateParam("DI",string.Join(", ", deliveryContact)),
                SQL.CreateParam("CustomerCode",customerCode),
                SQL.CreateParam("SalesOrderID",SalesOrderID, SqlDbType.BigInt),
                ];




            if (nextSalesOrderLine != null && nextSalesOrderLine.Description == "EMBROIDERY")
            {
                SqlParameter[] pe = [
                    SQL.CreateParam("EmbroideryCode",nextSalesOrderLine.SupplierProductCode.Value),
                    SQL.CreateParam("RequiredEmbroidery",orderLine.RequiredEmbroidery),
                    ];

                p = [.. p, .. pe];
            }

            SalesOrderLineID = RunStoredProcedure<BigInteger>(p, "dbo.SAMe_Import_SalesOrderLine", con);
            return SalesOrderLineID != default;
        }

        private static bool HandleSalesOrderHeader(SalesOrder salesOrder, out BigInteger SalesOrderID, SqlConnection con, BigInteger DeliveryContactID, string? AttachmentPath)
        {
            SqlParameter[] p = [
                SQL.CreateParam("SalesOrderNumber", salesOrder.OrderNumber.Value),
                SQL.CreateParam("OrderDate", salesOrder.OrderDate, SqlDbType.DateTime),
                SQL.CreateParam("CustomerCode", salesOrder.CustomerCode.Value),
                SQL.CreateParam("DeliveryContactID", DeliveryContactID,SqlDbType.BigInt),
                SQL.CreateParam("DeliveryContactID", DeliveryContactID,SqlDbType.BigInt)
                ];

            if (AttachmentPath is not null) 
            {
                p = [.. p, SQL.CreateParam("IQ_PathToDocument", AttachmentPath)];
            }

            SalesOrderID = RunStoredProcedure<BigInteger>(p, "dbo.SAMe_Import_SalesOrder", con);
            return SalesOrderID != default;
        }

        private static T? RunStoredProcedure<T>(SqlParameter[] parameters, string storedProcedure, SqlConnection con , string columnName = "ID")
        {
            using (SqlCommand cmnd = new(storedProcedure, con))
            {
                cmnd.CommandType = CommandType.StoredProcedure;
                foreach (SqlParameter p in parameters)
                {
                    cmnd.Parameters.Add(p);
                }

                if (con.State == ConnectionState.Closed)
                {
                    con.Open();
                }

                using (SqlDataReader reader = cmnd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException($"Stored procedure : '{storedProcedure}' returned no rows.");
                    }

                    object rawValue = reader[columnName];
                    if (typeof(T) == typeof(BigInteger))
                    {
                        if (rawValue == DBNull.Value)
                        {
                            return default;
                        }
                        BigInteger big = new(Convert.ToInt64(rawValue));
                        return (T)(object)big;
                    }

                    T castValue = (T)Convert.ChangeType(rawValue, typeof(T));
                    return castValue;
                }
            }
        }
    }
}