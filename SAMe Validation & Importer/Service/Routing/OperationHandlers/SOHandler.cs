using Azure.Storage.Blobs;
using Newtonsoft.Json.Linq;
using SAMe_Azure_Foundary_Library.LLM.Controller;
using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using SAMe_VI.Service.Importers;
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
        private readonly IImporter<SalesOrder> _importer;

        private readonly JsonSerializerOptions _jsonOptions;

        public SOHandler(IValidator<SalesOrder> validator, IImporter<SalesOrder> importer)
        {
            _validator = validator;
            _importer = importer;

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
                    bool needsDownloading = false;
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

                    string status;

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
                        status = "manual-review";
                    }
                    else
                    {
                        needsDownloading = true;
                        status = "import-ready";
                    }

                    if (result.Warnings.Count > 0)
                    {
                        Console.WriteLine($"{file}: Warnings:");
                        foreach (string w in result.Warnings)
                        {
                            Console.WriteLine($" - {w}");
                        }
                    }

                    string filePath = Path.Combine(Configuration.LogDir, Path.GetFileName(file));

                    await File.WriteAllTextAsync(filePath, enriched.ToString(), Encoding.UTF8, ct);

                    Guid blobId = Guid.NewGuid();

                    AnalysisController ac = new(resourceID: Configuration.Resource!.ResourceID!,
                                                analyser: Configuration.Resource.Analyser!,
                                                key: Configuration.Resource.Key!,
                                                storageConnectionString: Configuration.Resource.Storage!.StorageConnectionString!,
                                                storageContainerName: Configuration.Resource.Storage.StorageContainerName
                                               );

                    await ac.UploadToBlobStorage(filePath, $"{blobId}.SO",  _: ct);
                    await ac.AddTagsToFileAsync($"{blobId}.SO", new Dictionary<string, string>
                        {
                            { "status", status },
                            { "file-type", "sales-order" }
                        }, Configuration.Resource.Storage.StorageContainerName);

                    if (needsDownloading) 
                    {
                        if (!await ac.DownloadFileFromBlob(domain.FileBlobID, Configuration.AttachmentTempDir!, ct: ct)) 
                        {
                            Console.WriteLine($"{file}: Could not retrieve original document from Blob Storage, "+
                                              $"{Environment.NewLine}SalesOrder will be imported without an attachment "+
                                              $"{Environment.NewLine}Please collect the file:[{domain.FileBlobID}] manually ");
                        }
                    }


                    //This could be seperated from this solution, or be made to independantly called by this solution.
                    try
                    {
                        if (await _importer.ImportAsync(domain, ct))
                        {
                            //del from blob 
                            //del from local
                            //give pdf to Ellis's thing
                        }
                        else
                        {
                            //del from local
                            //change tag to manual review? + give import error tag
                            //output db error
                        }
                    }
                    catch (InvalidOperationException ioe) 
                    {
                        Console.WriteLine($"{file}: Invalid Operation -> {ioe.Message}");
                        //missing delivery address or sales order headers
                    }
                }
                catch (JsonException jex)
                {
                    Console.WriteLine($"{file}: JSON error -> {jex.Message}");
                    //db error
                }
            }
        }
    }
}
