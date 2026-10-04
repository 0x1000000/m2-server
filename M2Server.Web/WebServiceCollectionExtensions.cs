using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace M2Server.Web;

public static class WebServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddM2ServerWebHost(this IServiceCollection services, string dataDirectory)
    {
        services.AddSingleton<IFileProvider>(_ => WebAssets.CreateFileProvider());
        services.AddSingleton(provider => new ConsoleExecutorManager(
                dataDirectory,
                provider.GetService<ILogger<ConsoleExecutorManager>>()
            )
        );
        services.AddSingleton<ApiServer>();

        return services;
    }
}