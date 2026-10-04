using System.Runtime.Versioning;
using M2Server.Lib.Services;
using Microsoft.Extensions.DependencyInjection;

namespace M2Server.Lib.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddM2ServerCore(
        this IServiceCollection services,
        string? dataDirectory = null,
        bool persistChanges = true)
    {
        services.AddActivationLogging(dataDirectory);
        services.AddSingleton(_ => new DataStore(dataDirectory, persistChanges));
        services.AddSingleton<DisplayService>();
        services.AddSingleton<ScriptService>();
        services.AddSingleton<Security>();

        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddM2ServerDesktop(this IServiceCollection services)
    {
        services.AddSingleton<CertificateService>();
        services.AddSingleton<DesktopWorkflow>();

        return services;
    }
}