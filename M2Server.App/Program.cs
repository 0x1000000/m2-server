using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using M2Server.App.Infrastructure;
using M2Server.App.Services;
using M2Server.Lib;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M2Server.App;

internal static class Program
{
    internal static string? DataDirectory { get; private set; }

    internal static INotificationService Notifications { get; } = new TrayNotificationService();

    [STAThread]
    private static void Main(string[] args)
    {
        var dataArgument = Array.FindIndex(
            args,
            arg => string.Equals(arg, Constants.DataDirectoryArgument, StringComparison.OrdinalIgnoreCase)
        );
        if (dataArgument >= 0 && dataArgument + 1 < args.Length)
        {
            DataDirectory = Path.GetFullPath(args[dataArgument + 1]);
            args = args.Where((_, index) => index != dataArgument && index != dataArgument + 1).ToArray();
        }

        if (args.Length == 4 && args[0] == Constants.SaveConfigArgument)
        {
            if (!StartupService.IsAdministrator)
            {
                throw new UnauthorizedAccessException("Saving configuration requires elevation.");
            }

            ConfigurationSaveService.SaveElevated(args[1], args[2], args[3]);
            return;
        }

        try
        {
            if (args.Length is 2 or 3 && args[0] == Constants.ApplyProfileArgument &&
                (args.Length == 2 || args[2] == Constants.ParentNotifiesArgument) && Guid.TryParse(args[1], out var profileId))
            {
                var parentNotifies = args.Length == 3;
                var services = new ServiceCollection();
                services.AddM2ServerCore(DataDirectory, false);
                using var provider = services.BuildServiceProvider(true);
                var store = provider.GetRequiredService<DataStore>();
                var profile = store.Read(x => x.Presets.FirstOrDefault(p => p.Id == profileId));
                if (profile is null)
                {
                    Environment.ExitCode = 2;
                    if (!parentNotifies)
                    {
                        Notifications.Show("Profile not found.", true);
                    }

                    return;
                }

                var displayService = provider.GetRequiredService<DisplayService>();
                var logger = provider.GetRequiredService<ILogger<DisplayService>>();
                logger.LogInformation(
                    "Profile activation process context: dataDirectory={DataDirectory}, identity={Identity}, processSessionId={ProcessSessionId}, interactive={Interactive}",
                    DataDirectory,
                    WindowsIdentity.GetCurrent().Name,
                    Process.GetCurrentProcess().SessionId,
                    Environment.UserInteractive
                );
                var result = InputDesktopRunner.Run(() => displayService.Activate(profile), logger);
                Environment.ExitCode = result.Success ? 0 : 1;
                if (!parentNotifies)
                {
                    Notifications.Show(result.Message, !result.Success);
                }

                return;
            }

            if (args.Length is 2 or 3 && args[0] == Constants.RunScriptArgument && Guid.TryParse(args[1], out var scriptId))
            {
                var parentNotifies = args.Length == 3 && args[2] == Constants.ParentNotifiesArgument;
                if (args.Length == 3 && !parentNotifies)
                {
                    return;
                }

                if (!StartupService.IsAdministrator)
                {
                    var info = new ProcessStartInfo(Environment.ProcessPath!)
                    {
                        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                    };
                    info.ArgumentList.Add(Constants.RunScriptArgument);
                    info.ArgumentList.Add(scriptId.ToString());
                    info.ArgumentList.Add(Constants.ParentNotifiesArgument);
                    using var elevated = Process.Start(info)!;
                    elevated.WaitForExit();
                    Environment.ExitCode = elevated.ExitCode;
                    Notifications.Show(elevated.ExitCode == 0 ? "Script completed." : "Script failed.", elevated.ExitCode != 0);
                    return;
                }

                var store = new DataStore(DataDirectory, false);
                var script = store.Read(x => x.Scripts.FirstOrDefault(s => s.Id == scriptId));
                if (script is null)
                {
                    Environment.ExitCode = 2;
                    if (!parentNotifies)
                    {
                        Notifications.Show("Script not found.", true);
                    }

                    return;
                }

                var output = new ScriptService().RunAsync(script).GetAwaiter().GetResult();
                Environment.ExitCode = output.StartsWith("Exit code 0", StringComparison.Ordinal) ? 0 : 1;
                if (!parentNotifies)
                {
                    Notifications.Show(
                        Environment.ExitCode == 0 ? "Script completed." : "Script failed.",
                        Environment.ExitCode != 0
                    );
                }

                return;
            }
        }
        catch (Exception error) when (args.Length > 0 &&
                                      (args[0] == Constants.ApplyProfileArgument || args[0] == Constants.RunScriptArgument))
        {
            Environment.ExitCode = 1;
            if (!args.Contains(Constants.ParentNotifiesArgument))
            {
                Notifications.Show(error.Message, true);
            }

            return;
        }

        using var uiMutex = new Mutex(true, Constants.UiSingleInstanceMutexName, out var firstUiInstance);
        if (!firstUiInstance)
        {
            return;
        }

        App.DataDirectory = DataDirectory;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}