namespace SAMe_VI.Service.Routing.OperationHandlers
{
    /// <summary>
    /// Please do not use this for anything other than Operation Handlers
    /// I dont know why you would but just a warning
    /// </summary>
    internal interface IHandlerModule
    {
        internal abstract void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services);
        internal abstract IEnumerable<IFileHandler> BuildHandlers(IServiceProvider provider);
    }
}