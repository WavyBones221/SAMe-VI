using SAMe_Azure_Foundary_Library.LLM.Controller;
using SAMe_VI.Object;
using SAMe_VI.Service.Routing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SAMe_VI.Controller
{
    internal static class OperationController
    {
        internal static async Task CollectFilesFromFolder(string directory, DocumentRouter router, CancellationToken ct = default)
        {
            AnalysisController analysisController = CreateAnalysisController();

            await CollectFilesFromBlobAsync(analysisController, directory, ct).ConfigureAwait(false);

            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                await TryDeleteFromBlobStorageAsync(analysisController, file).ConfigureAwait(false);

                string extension = Path.GetExtension(file);

                if (router.TryGetHandlerByExtension(extension, out IFileHandler handler))
                {
                    handler.Enqueue(file);
                }
                else
                {
                    Console.WriteLine("Unrecognised file type -> " + file);
                }
            }
        }

        private static async Task CollectFilesFromBlobAsync(AnalysisController analysisController, string directory, CancellationToken ct)
        {
            KeyValuePair<string, string> tag = new("status", "validating");
            await analysisController.GetFilesByTag(tag, directory, ct: ct).ConfigureAwait(false);
        }

        private static async Task TryDeleteFromBlobStorageAsync(AnalysisController analysisController, string file)
        {
            try
            {
                string containerName = Configuration.Resource!.Storage!.StorageContainerName!;
                await analysisController.DeleteFileInBlobStorage(file, containerName).ConfigureAwait(false);
            }
            catch (Exception) { }
        }

        private static AnalysisController CreateAnalysisController()
        {
            return new AnalysisController(
                resourceID: Configuration.Resource!.ResourceID!,
                analyser: Configuration.Resource.Analyser!,
                key: Configuration.Resource.Key!,
                storageConnectionString: Configuration.Resource.Storage!.StorageConnectionString!,
                storageContainerName: Configuration.Resource.Storage.StorageContainerName!);
        }
    }
}