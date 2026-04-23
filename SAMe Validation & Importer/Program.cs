using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SAMe_VI.Controller;
using SAMe_VI.Logging;
using SAMe_VI.Object;
using SAMe_VI.Service.Routing;
using SAMe_VI.Service.Routing.OperationHandlers;
using System.Reflection;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Configuration.SetConfiguration();
        //Can be changed how captured, for now just writing to console which is captured by the job agent, can change to write to file or something else if needed
        ConsoleOutputBuilder capturedOut = new (Console.Out);
        Console.SetOut(capturedOut);

        try
        {
            IHandlerModule[] modules = DiscoverModules();

            using (IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    try
                    {
                        for (int i = 0; i < modules.Length; i++)
                        {
                            try
                            {
                                modules[i].RegisterServices(services);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Service Could not be Registered : {modules[i].GetType().FullName} {Environment.NewLine}[Message] : {ex.Message}");
                                continue;
                            }
                        }

                        services.AddSingleton(sp =>
                        {
                            List<IFileHandler> list = [];
                            for (int i = 0; i < modules.Length; i++)
                            {
                                try
                                {
                                    IEnumerable<IFileHandler> group = modules[i].BuildHandlers(sp);
                                    list.AddRange(group);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"{Environment.NewLine}One or more Handlers Could not be built for : {modules[i].GetType().FullName} {Environment.NewLine}[Message] : {ex.Message}");
                                    continue;
                                }
                            }

                            IFileHandler[] handlers = [.. list];
                            return DocumentRouter.WithHandlers(handlers);
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{Environment.NewLine}An error occurred during service registration. {Environment.NewLine}[Message] : {ex.Message}");
                        return;
                    }
                })
                .Build())
            {
                string inputDir = Configuration.InputDir;

                if (!Directory.Exists(inputDir))
                {
                    Directory.CreateDirectory(inputDir);
                }
                ///@TODO: Change this to pull from blob please - EDIT: this is done but also has functionality to allow files to be dropped in the folder if needed,
                ///                                                  > this is for ease of use for now, can remove local folder functionality later if not needed
                DocumentRouter router = host.Services.GetRequiredService<DocumentRouter>();

                await OperationController.CollectFilesFromFolder(inputDir, router);

                await router.ProcessAllAsync();

                await host.StopAsync();

                await capturedOut.PostRunReportAsync();
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bad News, Something unaccounted for has happened, Causing the application to exit {Environment.NewLine}[Message] : {ex.Message} @ [Source] : {ex.Source}");
            await capturedOut.PostRunReportAsync();
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