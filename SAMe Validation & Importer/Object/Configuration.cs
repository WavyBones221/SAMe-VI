using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SAMe_VI.Object
{
    public static class Configuration
    {
        private static readonly string ConfigurationFile = "application.properties.json";
        public static string ConnectionString = string.Empty;
        public static string DatabaseName = string.Empty;
        public static string InputDir = string.Empty;
        public static string LogDir = string.Empty;
        public static string AttachmentTempDir = string.Empty;
        public static string DISConnectionString = string.Empty;

        public static Resource? Resource = null;

        public static void SetConfiguration()
        {
            ConfigurationBuilder builder = new();
            IConfiguration root = builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory,ConfigurationFile)).Build() ?? throw new Exception($"{ConfigurationFile} Not Found");
            IConfigurationSection database = root.GetSection("Database");
            IConfigurationSection file = root.GetSection("File");
            ConnectionString = database.GetValue<string>("ConnectionString") ?? throw new Exception($"Database:ConnectionString not found in {ConfigurationFile}");
            DatabaseName = database.GetValue<string>("DatabaseName") ?? throw new Exception($"Database: DatabaseName not found in {ConfigurationFile}");
            InputDir = file.GetValue<string>("InputDir") ?? throw new Exception($"File:InputDir not found in {ConfigurationFile}");
            LogDir = file.GetValue<string>("LogDir") ?? throw new Exception($"File:LogDir not found in {ConfigurationFile}");
            AttachmentTempDir = file.GetValue<string>("AttachmentTempDir") ?? throw new Exception($"File:AttachmentTempDir not found in {ConfigurationFile}");

            Resource = root.GetSection("Resource").Get<Resource>() ?? throw new Exception("Resource Configuration Not Found");
        }
    }
}
