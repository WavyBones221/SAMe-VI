using SAMe_VI.Service.Routing;

namespace SAMe_VI.Controller
{
    internal class OperationController
    {
        internal static void CollectFilesFromFolder(string directory, DocumentRouter router)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string ext = Path.GetExtension(file);

                if (router.TryGetHandlerByExtension(ext, out IFileHandler handler))
                {
                    handler.Enqueue(file);
                }
                else
                {
                    Console.WriteLine("Unrecognised file type -> " + file);
                }
            }
        }
    }
}
