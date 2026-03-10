using System.Collections.ObjectModel;
using System.Reflection;

namespace SAMe_VI.Service.Routing
{
    internal sealed class DocumentRouter
    {
        private readonly Dictionary<string, IFileHandler> _byExtension;
        private readonly IReadOnlyCollection<string> _registeredExtensions;

        private DocumentRouter(Dictionary<string, IFileHandler> byExtension)
        {
            _byExtension = byExtension;
            _registeredExtensions = new ReadOnlyCollection<string>([.. _byExtension.Keys]);
        }

        public static DocumentRouter WithHandlers(IFileHandler[] handlers)
        {
            ArgumentNullException.ThrowIfNull(handlers);

            Dictionary<string, IFileHandler> map = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < handlers.Length; i++)
            {
                IFileHandler handler = handlers[i];
                if (handler == null)
                {
                    continue;
                }

                IEnumerable<string> exts = ResolveExtensions(handler);
                foreach (string ext in exts)
                {
                    string normalised = NormalizeExtension(ext);

                    if (normalised.Length == 0)
                    {
                        throw new InvalidOperationException("Handler exposed an empty or whitespace FileExtension.");
                    }

                    if (map.ContainsKey(normalised))
                    {
                        throw new InvalidOperationException($"A handler is already registered for extension {normalised}.");
                    }

                    map[normalised] = handler;
                }
            }

            if (map.Count == 0)
            {
                throw new InvalidOperationException("No handlers were registered. Provide at least one handler with a valid FileExtension.");
            }

            return new DocumentRouter(map);
        }

        public bool TryGetHandlerByExtension(string extension, out IFileHandler handler)
        {
            string normalised = NormalizeExtension(extension);
            return _byExtension.TryGetValue(normalised, out handler!);
        }

        public bool TryGetHandlerByFilePath(string filePath, out IFileHandler handler)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            string extension = Path.GetExtension(filePath);
            return TryGetHandlerByExtension(extension, out handler);
        }

        public bool TryRouteFile(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            if (TryGetHandlerByFilePath(filePath, out IFileHandler handler))
            {
                handler.Enqueue(filePath);
                return true;
            }

            return false;
        }

        public async Task ProcessAllAsync(CancellationToken ct = default)
        {
            HashSet<IFileHandler> uniqueHandlers = [.. _byExtension.Values];
            foreach (IFileHandler handler in uniqueHandlers)
            {
                await handler.ProcessAllAsync(ct);
            }
        }

        public bool SupportsExtension(string extension)
        {
            return TryGetHandlerByExtension(extension, out _);
        }

        public IReadOnlyCollection<string> RegisteredExtensions
        {
            get { return _registeredExtensions; }
        }

        /// <summary>
        /// This handles any type of property or field you decide to call FileExtension/FileExtensions in the respective Handler.
        /// Ensure the values include the dot (.) and avoid duplicates across handlers.
        /// </summary>
        private static IEnumerable<string> ResolveExtensions(IFileHandler handler)
        {
            Type t = handler.GetType();

            FieldInfo? single = t.GetField("FileExtension", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (single != null && single.FieldType == typeof(string))
            {
                string? ext = (string?)single.GetValue(null);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return [ext!];
                }
            }

            FieldInfo? multi = t.GetField("FileExtensions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (multi != null && typeof(IEnumerable<string>).IsAssignableFrom(multi.FieldType))
            {
                IEnumerable<string>? exts = (IEnumerable<string>?)multi.GetValue(null);
                if (exts != null)
                {
                    return exts;
                }
            }

            PropertyInfo? instSingle = t.GetProperty("FileExtension", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (instSingle != null && instSingle.PropertyType == typeof(string))
            {
                string? ext = (string?)instSingle.GetValue(handler);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return [ext!];
                }
            }

            PropertyInfo? instMulti = t.GetProperty("FileExtensions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (instMulti != null && typeof(IEnumerable<string>).IsAssignableFrom(instMulti.PropertyType))
            {
                IEnumerable<string>? exts = (IEnumerable<string>?)instMulti.GetValue(handler);
                if (exts != null)
                {
                    return exts;
                }
            }

            throw new NotSupportedException($"Handler {t.FullName} does not expose FileExtension(s).");
        }

        private static string NormalizeExtension(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext))
            {
                return string.Empty;
            }

            string trimmed = ext.Trim();
            if (!trimmed.StartsWith('.'))
            {
                trimmed = '.' + trimmed;
            }

            return trimmed;
        }
    }
}