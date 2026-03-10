using Microsoft.Data.SqlClient;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SamsQueryLibrary.Services;
using System.Data;
using System.Reflection;

internal class ValidationRepositoryBase
{
    private static SqlConnection? Con;


    /// <summary>
    /// Will loop through each param of an object 
    /// disregading complex types and collections, and call the stored procedure with those params to validate the object
    /// 
    /// Does require the stored procedure to be set up to receive the params and return a result set that matches the structure of the object type T which can be found within the SAMe_VI.Repository.DataTableStruct folder
    /// Feel free to create new structures in that folder if needed, just make sure the variables names match the columns of the results of the stored procedure
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="obj"></param>
    /// <param name="storedProcedure"></param>
    /// <param name="con"></param>
    /// <returns></returns>
    public static DataTable Validate<T>(object obj, string storedProcedure, SqlConnection? con = null)
    {
        if (con is not null)
        {
            Con = con;
        }
        else Con ??= GetSqlConnection();

        DataTable table = SQL.CreateTable<T>();
        PropertyInfo[] properties = obj.GetType().GetProperties();
        List<SqlParameter> parameters = new(properties.Length);

        foreach (PropertyInfo prop in properties)
        {
            Type type = prop.PropertyType;


            bool isFieldValue = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FieldValue<>);

            if (!isFieldValue)
            {
                continue;
            }

            Type fieldType = type.GetGenericArguments()[0];
            Type underlying = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

            bool isSimpleFieldType =
                 underlying.IsPrimitive ||
                 underlying.IsEnum ||
                 underlying == typeof(string) ||
                 underlying == typeof(decimal) ||
                 underlying == typeof(DateTime) ||
                 underlying == typeof(Guid) ||
                 underlying == typeof(TimeSpan) ||
                 underlying == typeof(DateTimeOffset);

            if (!isSimpleFieldType)
            {
                continue;
            }

            object? wrapper = prop.GetValue(obj);
            if (wrapper == null)
            {
                SqlParameter nullParam = new($"@{prop.Name}", DBNull.Value);
                parameters.Add(nullParam);
                continue;
            }

            PropertyInfo? valueProp = wrapper.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null)
            {
                continue;
            }

            object? rawValue = valueProp.GetValue(wrapper);
            SqlParameter param = new($"@{prop.Name}", rawValue ?? DBNull.Value);
            parameters.Add(param);
        }

        using (SqlCommand cmnd = new(storedProcedure, Con))
        {

            cmnd.CommandType = CommandType.StoredProcedure;
            if (parameters != null && parameters.Count != 0)
            {
                foreach (SqlParameter parameter in parameters)
                {
                    cmnd.Parameters.Add(parameter);
                }
            }

            if (Con.State == ConnectionState.Closed)
            {
                Con.Open();
            }
            using (SqlDataAdapter da = new(cmnd))
            {
                da.Fill(table);
            }

        }
        Con.Close();

        return table;

    }
    private static SqlConnection GetSqlConnection(string? connectionString = null) => new(connectionString ?? Configuration.ConnectionString);
}


