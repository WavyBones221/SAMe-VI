using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SAMe_VI.Controller;
using SAMe_VI.Object;
using SAMe_VI.Service.Routing;
using SAMe_VI.Service.Routing.OperationHandlers;
using System.Reflection;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            IHandlerModule[] modules = DiscoverModules();

            using (IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    for (int i = 0; i < modules.Length; i++)
                    {
                        modules[i].RegisterServices(services);
                    }

                    services.AddSingleton(sp =>
                    {
                        List<IFileHandler> list = [];
                        for (int i = 0; i < modules.Length; i++)
                        {
                            IEnumerable<IFileHandler> group = modules[i].BuildHandlers(sp);
                            list.AddRange(group);
                        }

                        IFileHandler[] handlers = [.. list];
                        return DocumentRouter.WithHandlers(handlers);
                    });
                })
                .Build())
            {
                Configuration.SetConfiguration();
                string inputDir = Configuration.InputDir;

                if (!Directory.Exists(inputDir))
                {
                    Directory.CreateDirectory(inputDir);
                }

                DocumentRouter router = host.Services.GetRequiredService<DocumentRouter>();

                OperationController.CollectFilesFromFolder(inputDir, router);

                await router.ProcessAllAsync();

                await host.StopAsync();

                Environment.Exit(0);
            }
        }
        catch(Exception ex) 
        {
            //Ran on the ssms job agent, this will be captured by the job log. IF anything happens @TODO: log this better
            Console.WriteLine(ex.ToString());
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// This method makes it easier for adding new proceses, 
    /// Just make the correct object types and this should pick them up 
    /// </summary>
    /// <returns></returns>
    private static IHandlerModule[] DiscoverModules()
    {
        List<IHandlerModule> found = [];
        Assembly assembly = Assembly.GetExecutingAssembly();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        for (int i = 0; i < types.Length; i++)
        {
            Type t = types[i];
            if (t == null)
            {
                continue;
            }

            if (typeof(IHandlerModule).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            {
                if (Activator.CreateInstance(t) is IHandlerModule module)
                {
                    found.Add(module);
                }
            }
        }
        
        return [.. found];
    }
}