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

#region validation logic
        
        public async Task ProcessAllAsync(CancellationToken ct = default)
        {
            //try
            //{
                while (_inbox.TryDequeue(out string? file))
                {
                    try
                    {
                        bool Importable = false;
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

                            //del from directory
                            File.Delete(file);

                        }
                        else
                        {
                            Importable = true;
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

                        await ac.UploadToBlobStorage(filePath, $"{blobId}.SO", _: ct);
                        await ac.AddTagsToFileAsync($"{blobId}.SO", new Dictionary<string, string>
                    {
                        //this is all for filtering on the front end, could probably be extracted for reusability
                        { "status", status },
                        { "file-type", "sales-order" },
                        { "Division", domain.DivisionCode },
                        { "CustomerCode", domain.CustomerCode.Value }

                    }, Configuration.Resource.Storage.StorageContainerName);

        #endregion
                        #region Import Logic

                        //This could be seperated from this solution, or be made to independantly called by this solution on ssms job agent or something??
                        //idk man probably could do with seperating though- anyways this would be the seperation point, everything above in this controller is validation related.
                        //all below is import

                        if (Importable)
                        {
                            if (!await ac.DownloadFileFromBlob(domain.FileBlobID, Path.Combine(Configuration.AttachmentTempDir!, domain.FileBlobID), ct: ct))
                            {
                                Console.WriteLine($"{file}: Could not retrieve original document from Blob Storage, " +
                                                  $"{Environment.NewLine}SalesOrder will be imported without an attachment " +
                                                  $"{Environment.NewLine}Please collect the file:[{domain.FileBlobID}] manually ");
                            }

                            try
                            {
                                if (await _importer.ImportAsync(domain, ct))
                                {

                                    //del from blob, only reason these will error is either file didnt exist or something went wrong with log object creation, either way, dont care in the grand scheme of things
                                    try
                                    {
                                        //dl from blob into a temp

                                        //upload to sharepoint, this will fail if the folder on sharepoint doesnt exist. Also delete from the processing folder since the processing on this file has finished
                                        try
                                        {
                                            
                                            await SharepointService.UploadFile($"/sites/IntactiQ/Shared Documents/Order Processing/Azure AI Importer/Sales Orders/{domain.CustomerCode}/Complete", Path.Combine(Configuration.AttachmentTempDir!, domain.FileBlobID));
                                            await SharepointService.DeleteFile($"/sites/IntactiQ/Shared Documents/Order Processing/Azure AI Importer/Sales Orders/{domain.CustomerCode}/Processing/{domain.FileBlobID}");
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine($"{file}: Failed to upload attachment to SharePoint -> {e.Message} " +
                                                $"{Environment.NewLine}Please check the SharePoint folder structure contains: [/sites/IntactiQ/Shared Documents/Order Processing/Azure AI Importer/Sales Orders/{domain.CustomerCode}/Complete]");
                                        }
                                        //if success, del from temp. else leave in temp

                                        //del from blob
                                        await ac.DeleteFileInBlobStorage(domain.FileBlobID, Configuration.Resource.Storage.StorageContainerName, ct);
                                    }
                                    catch (Exception) { }
                                    try
                                    {
                                        await ac.DeleteFileInBlobStorage(Path.ChangeExtension(domain.FileBlobID, "json"), Configuration.Resource.Storage.StorageContainerName, ct);
                                    }
                                    catch (Exception) { }

                                    //Depending on where the file came from, either was put into the system the normal way by uploading to the blob, or if a file was instead dropped into the temp file dir, just need to do this just in case
                                    try
                                    {
                                        await ac.DeleteFileInBlobStorage($"{blobId}.SO", Configuration.Resource.Storage.StorageContainerName, ct);
                                    }
                                    catch (Exception) { }
                                    try
                                    {
                                        await ac.DeleteFileInBlobStorage($"{Path.GetFileNameWithoutExtension(file)}.SO", Configuration.Resource.Storage.StorageContainerName, ct);
                                    }
                                    catch (Exception) { }

                                    //del from local, can be changed to move
                                    File.Delete(file);
                                }
                                else
                                {
                                    //del from local
                                    File.Delete(file);
                                    //change tag to manual review? + give import error tag
                                    await ac.AddTagsToFileAsync($"{blobId}.SO", new Dictionary<string, string>
                                {
                                    { "status", "error" },
                                    { "file-type", "sales-order" }
                                },
                                    Configuration.Resource.Storage.StorageContainerName);
                                    //output db error.............   No.
                                }
                            }
                            catch (InvalidOperationException joe)
                            {                                                   //joe message
                                Console.WriteLine($"{file}: Invalid Operation -> {joe.Message}");
                                //missing delivery address or sales order headers
                                continue;
                            }
                        }
                    }
                    catch (JsonException jex)
                    {
                        Console.WriteLine($"{file}: JSON error -> {jex.Message}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{file}: Unexpected error -> {ex.Message} StackTrace -> {ex.StackTrace}");
                        continue;
                    }
                }
            //} catch (Exception ex) { Console.WriteLine(ex.Message );
             //   throw;
            //}
        }
    }
}
#endregion