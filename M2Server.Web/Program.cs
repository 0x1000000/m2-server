using System.Diagnostics;
using System.Runtime.Versioning;
using M2Server.Lib;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using Microsoft.Extensions.DependencyInjection;

namespace M2Server.Web;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] is Constants.InstallerPrepareArgument or Constants.InstallerPurgeArgument)
        {
            return InstallerCleanup.Run(args[0], args[1]);
        }

        if (args.Length >= 1 && args[0] is Constants.InstallArgument or Constants.UninstallArgument)
        {
            return ServiceCommand(args);
        }

        var dataDirectory = ServiceDataDirectory.Read();
        var services = new ServiceCollection();
        services.AddM2ServerCore(dataDirectory);
        services.AddM2ServerDesktop();
        services.AddM2ServerWebHost(dataDirectory);
        using var provider = services.BuildServiceProvider(true);
        var api = provider.GetRequiredService<ApiServer>();
        await api.StartAsync();
        await api.WaitForShutdownAsync();
        await api.StopAsync();
        return 0;
    }

    private static int ServiceCommand(string[] args)
    {
        if (!StartupService.IsAdministrator)
        {
            return 5;
        }

        if (args[0] == Constants.InstallArgument)
        {
            if (args.Length != 2 || !Path.IsPathFullyQualified(args[1]))
            {
                return 2;
            }

            var dataDirectory = Path.GetFullPath(args[1]);
            var configFile = Path.Combine(dataDirectory, Constants.DataFileName);
            if (!File.Exists(configFile))
            {
                return 2;
            }

            var config = new DataStore(dataDirectory, false).Read(x => x.Settings);
            if (config.WebEnabled == false || string.IsNullOrEmpty(config.PasswordHash))
            {
                return 3;
            }

            var path = Environment.ProcessPath!;
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, Constants.AppExecutableName)))
            {
                return 4;
            }

            var create = RunSc(
                "create",
                Constants.WebServiceName,
                "binPath=",
                path,
                "start=",
                "auto",
                "obj=",
                "LocalSystem",
                "DisplayName=",
                "M2 Server Web"
            );
            if (create != 0)
            {
                return create;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ServiceDataDirectory.ReferencePath)!);
                ProtectReference(Path.GetDirectoryName(ServiceDataDirectory.ReferencePath)!, true);
                File.WriteAllText(ServiceDataDirectory.ReferencePath, dataDirectory);
                ProtectReference(ServiceDataDirectory.ReferencePath, false);
                if (config.AllowLanAccess)
                {
                    FirewallService.AllowPrivateLan(config.HttpsPort, path);
                }

                if (config.AllowPublicLanAccess)
                {
                    FirewallService.AllowPublicLan(config.HttpsPort, path);
                }

                var start = RunSc("start", Constants.WebServiceName);
                if (start == 0)
                {
                    File.Delete(Path.Combine(Path.GetDirectoryName(ServiceDataDirectory.ReferencePath)!, Constants.DataFileName));
                    return 0;
                }

                RunSc("delete", Constants.WebServiceName);
                if (config.AllowLanAccess)
                {
                    FirewallService.RemovePrivateLan(config.HttpsPort);
                }

                if (config.AllowPublicLanAccess)
                {
                    FirewallService.RemovePublicLan(config.HttpsPort);
                }

                File.Delete(ServiceDataDirectory.ReferencePath);
                return start;
            }
            catch
            {
                RunSc("delete", Constants.WebServiceName);
                if (config.AllowLanAccess)
                {
                    FirewallService.RemovePrivateLan(config.HttpsPort);
                }

                if (config.AllowPublicLanAccess)
                {
                    FirewallService.RemovePublicLan(config.HttpsPort);
                }

                File.Delete(ServiceDataDirectory.ReferencePath);
                throw;
            }
        }

        RunSc("stop", Constants.WebServiceName);
        var savedSettings = new DataStore(ServiceDataDirectory.Read(), false).Read(x => x.Settings);
        FirewallService.RemovePrivateLan(savedSettings.HttpsPort);
        FirewallService.RemovePublicLan(savedSettings.HttpsPort);
        var result = RunSc("delete", Constants.WebServiceName);
        if (result == 0)
        {
            File.Delete(ServiceDataDirectory.ReferencePath);
        }

        return result;
    }

    private static int RunSc(params string[] arguments)
    {
        var info = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Service control failed.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void ProtectReference(string path, bool directory)
    {
        var info = new ProcessStartInfo("icacls.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add(path);
        info.ArgumentList.Add("/inheritance:r");
        info.ArgumentList.Add("/grant:r");
        var inherit = directory ? "(OI)(CI)" : "";
        info.ArgumentList.Add("*S-1-5-18:" + inherit + "F");
        info.ArgumentList.Add("*S-1-5-32-544:" + inherit + "F");
        info.ArgumentList.Add("*S-1-5-11:" + inherit + "RX");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not protect service reference.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not set service reference permissions.");
        }
    }
}