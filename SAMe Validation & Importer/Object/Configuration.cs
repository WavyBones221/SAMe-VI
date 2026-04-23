using Microsoft.Extensions.Configuration;

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

        //KeyValuePair<string, string> = <EmailAddress, Password>
        public static KeyValuePair<string, string>? EmailCredentials = null;
        public static string? EmailAddress = null;
        public static string[]? EmailRecipients = null;
        public static string? RunReportDir = null;

        public static Sharepoint? SharepointConfig = null;
        public static Resource? Resource = null;

        public static void SetConfiguration()
        {
            ConfigurationBuilder builder = new();
            IConfiguration root = builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile)).Build() ?? throw new Exception($"{ConfigurationFile} Not Found");
            IConfigurationSection database = root.GetSection("Database");
            IConfigurationSection file = root.GetSection("File");

            //DB & File Configurations

            ConnectionString = database.GetValue<string>("ConnectionString") ?? throw new Exception($"Database:ConnectionString not found in {ConfigurationFile}");
            DatabaseName = database.GetValue<string>("DatabaseName") ?? throw new Exception($"Database: DatabaseName not found in {ConfigurationFile}");
            InputDir = file.GetValue<string>("InputDir") ?? throw new Exception($"File:InputDir not found in {ConfigurationFile}");
            LogDir = file.GetValue<string>("LogDir") ?? throw new Exception($"File:LogDir not found in {ConfigurationFile}");
            AttachmentTempDir = file.GetValue<string>("AttachmentTempDir") ?? throw new Exception($"File:AttachmentTempDir not found in {ConfigurationFile}");

            //LOGGING [Nullable] - do not include what you do not wish to see, i.e if you dont want this emailing to anyone dont include any of the email related fields 

            EmailCredentials = root.GetSection("EmailCredentials").Get<KeyValuePair<string, string>>(); if (EmailCredentials == null) Console.WriteLine("[Warning] Email Credentials Not Found");
            EmailAddress = root.GetSection("EmailAddress").GetValue<string>("Value"); if (EmailAddress == null) Console.WriteLine("[Warning] Email Address Not Found");
            EmailRecipients = root.GetSection("EmailRecipients").Get<string[]>(); if (EmailRecipients == null) Console.WriteLine("[Warning] Email Recipients Not Found");
            RunReportDir = root.GetSection("RunReportDir").GetValue<string>("Value"); if (RunReportDir == null) Console.WriteLine("[Warning] Report Directory Not Found");

            //MS Account Details

            SharepointConfig = root.GetSection("Sharepoint").Get<Sharepoint>(); if (SharepointConfig == null) Console.WriteLine("[Warning] Sharepoint Configuration Not Found");
            Resource = root.GetSection("Resource").Get<Resource>() ?? throw new Exception("Resource Configuration Not Found");
        }
    }
}
