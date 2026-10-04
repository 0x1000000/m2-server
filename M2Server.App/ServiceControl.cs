using System.Diagnostics;
using System.ServiceProcess;
using M2Server.Lib;
using M2Server.Lib.Infrastructure;
using TimeoutException = System.TimeoutException;

namespace M2Server.App;

internal static class ServiceControl
{
    public static string Status()
    {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == Constants.WebServiceName);
        return service is null ? "Not installed" : service.Status.ToString();
    }

    public static bool UsesDataDirectory(string directory)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(ServiceDataDirectory.Read()),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static async Task ChangeAsync(bool install, string? dataDirectory = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, Constants.WebServiceExecutableName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The web service executable is missing.", path);
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add(install ? Constants.InstallArgument : Constants.UninstallArgument);
        if (install)
        {
            info.ArgumentList.Add(dataDirectory ?? ServiceDataDirectory.CurrentUser);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start service control.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Service command failed (exit code {process.ExitCode}).");
        }

        if (!install)
        {
            for (var attempt = 0; attempt < 150 && Status() != "Not installed"; attempt++)
            {
                await Task.Delay(200);
            }

            if (Status() != "Not installed")
            {
                throw new TimeoutException("Windows has not finished removing the web service.");
            }
        }
    }

    public static void RestartIfRunning()
    {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == Constants.WebServiceName);
        if (service is null || service.Status != ServiceControllerStatus.Running)
        {
            return;
        }

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }
}