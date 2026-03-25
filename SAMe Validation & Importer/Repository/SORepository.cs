using Microsoft.Data.SqlClient;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SAMe_VI.Repository.DataTableStruct;
using SamsQueryLibrary.Services;
using System.Data;

namespace SAMe_VI.Repository
{
    internal class SORepository : ValidationRepositoryBase, ISORepository
    {
        public DataTable ValidateDeliveryLocations(ICollection<DeliveryLocationLine> deliveryLocationLines, SqlConnection? con = null)
        {
            DataTable resultTable = SQL.CreateTable<SOLineValidationStruct>();
            foreach (DeliveryLocationLine deliveryLocationLine in deliveryLocationLines)
            {
                resultTable.Merge(Validate<SOLineValidationStruct>(deliveryLocationLine, $"{Configuration.DatabaseName}.dbo.SAMe_Validation_Delivery_Location_Line", con));
            }
            return resultTable;
        }

        public DataTable ValidateOrderHeaders(SalesOrder orderHeaders, SqlConnection? con = null)
        {
            return Validate<SOHeaderValidationStruct>(orderHeaders, $"{Configuration.DatabaseName}.dbo.SAMe_Validation_OrderHeaders", con);
        }

        public DataTable ValidateOrderLines(ICollection<SalesOrderLine> orderLines, SqlConnection? con = null)
        {
            DataTable resultTable = SQL.CreateTable<SOLineValidationStruct>();
            foreach (SalesOrderLine orderLine in orderLines)
            {
                resultTable.Merge(Validate<SOLineValidationStruct>(orderLine, $"{Configuration.DatabaseName}.dbo.SAMe_Validation_OrderLines", con));
            }
            return resultTable;
        }
    }
}