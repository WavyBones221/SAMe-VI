using Newtonsoft.Json.Linq;
using SAMe_Azure_Foundary_Library.LLM.Controller;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SAMe_VI.Service.Mapping;
using SAMe_VI.Service.Validators;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SAMe_VI.Service.Routing.OperationHandlers
{
    internal sealed class SOHandler : IFileHandler
    {
        internal readonly static string FileExtension = ".so";
        private readonly ConcurrentQueue<string> _inbox = new();

        private readonly IValidator<SalesOrder> _validator;
        private readonly IImporter<SalesOrder> _importer = new SOImporter();

        private readonly JsonSerializerOptions _jsonOptions;

        public SOHandler(IValidator<SalesOrder> validator)
        {
            _validator = validator;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            _jsonOptions.Converters.Add(new ConfidenceValueConverterFactory());
        }

        public void Enqueue(string filePath)
        {
            _inbox.Enqueue(filePath);
        }

        public async Task ProcessAllAsync(CancellationToken ct = default)
        {
            while (_inbox.TryDequeue(out string? file))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file, ct);
                    SalesOrderRaw? raw = JsonSerializer.Deserialize<SalesOrderRaw>(json, _jsonOptions);

                    if (raw == null)
                    {
                        Console.WriteLine($"{file}: Could not deserialize SalesOrderRaw.");
                        continue;
                    }

                    SalesOrder domain = SalesOrderMapper.ToDomain(raw);
                    JObject pResult = JObject.Parse(json);
                    ValidationResult result = await _validator.ValidateAsync(domain, ct);
                    result.AttachToJson(pResult, out JObject enriched);

                    if (!result.IsValid)
                    {
                        Console.WriteLine($"{file}: Validation failed:");
                        foreach (string e in result.Errors)
                        {
                            Console.WriteLine($" - {e}");
                        }
                        if (result.Warnings.Count > 0)
                        {
                            Console.WriteLine("Warnings:");
                            foreach (string w in result.Warnings)
                            {
                                Console.WriteLine($" - {w}");
                            }
                        }

                        string filePath = Path.Combine(Configuration.LogDir, Path.GetFileName(file));

                        await File.WriteAllTextAsync(filePath, enriched.ToString(), Encoding.UTF8, ct);

                        Guid blobId = Guid.NewGuid();

                        //AnalysisController ac = new(resourceID:,analyser:,key:,storageConnectionString:, storageContainerName:);

                        //await ac.UploadToBlobStorage(filePath,$"{blobId}.SO", storageContainerName:"", _:ct);
                        //await ac.AddTagsToFileAsync($"{blobId}", new Dictionary<string, string>
                        //{
                        //    { "status", "manual-review" },
                        //    { "file-type", "sales-order" }
                        //},  "");


                        continue;
                    }

                    if (result.Warnings.Count > 0)
                    {
                        Console.WriteLine($"{file}: Warnings:");
                        foreach (string w in result.Warnings)
                        {
                            Console.WriteLine($" - {w}");
                        }
                    }

                    
                    //await _importer.ImportAsync(domain, ct);
                }
                catch (JsonException jex)
                {
                    Console.WriteLine($"{file}: JSON error -> {jex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{file}: Unexpected error -> {ex.Message}");
                }
            }
        }
    }
}
