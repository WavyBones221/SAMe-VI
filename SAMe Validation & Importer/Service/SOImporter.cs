using SAMe_VI.Object.Models;

namespace SAMe_VI.Service
{
    internal class SOImporter() : IImporter<SalesOrder>
    {
        public async Task<bool> ImportAsync(SalesOrder so, CancellationToken ct) 
        {
            return false;
        }
    }
}