using SAMe_VI.Object.Models;

namespace SAMe_VI.Service.Importers
{
    internal interface IImporter<T> where T : class
    {
        internal abstract Task<bool> ImportAsync(T so, CancellationToken ct);
    }
}