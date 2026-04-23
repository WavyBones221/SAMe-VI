using Microsoft.Extensions.DependencyInjection;
using SAMe_VI.Object.Models;
using SAMe_VI.Repository;
using SAMe_VI.Service.Importers;
using SAMe_VI.Service.Validators;

namespace SAMe_VI.Service.Routing.OperationHandlers
{
    internal sealed class SOHandlerModule : IHandlerModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<ISORepository, SORepository>();
            services.AddSingleton<IValidator<SalesOrder>, SOValidator>();
            services.AddSingleton<IImporter<SalesOrder>, SOImporter>();
            services.AddSingleton<SOHandler>();
            services.AddSingleton<IFileHandler>(sp => sp.GetRequiredService<SOHandler>());
        }

        public IEnumerable<IFileHandler> BuildHandlers(IServiceProvider provider)
        {
            SOHandler so = provider.GetRequiredService<SOHandler>();
            return [so];
        }
    }
}