using Microsoft.Data.SqlClient;
using SAMe_VI.Object.Models;
using System.Data;

namespace SAMe_VI.Repository
{
    internal interface ISORepository
    {
        public abstract DataTable ValidateOrderHeaders(SalesOrder orderHeaders, SqlConnection? con = null);

        public abstract DataTable ValidateOrderLines(ICollection<SalesOrderLine> orderLines, SqlConnection? con = null);
    }
}