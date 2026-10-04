using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M2Server.Lib.Infrastructure;

public static class ActivationLogExtension
{
    public static IServiceCollection AddActivationLogging(this IServiceCollection services, string? directory = null)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information)
            .AddProvider(
                new DailyFileLoggerProvider(
                    directory ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Constants.DataDirectoryName
                    )
                )
            )
        );
        return services;
    }
}