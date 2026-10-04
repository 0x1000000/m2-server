using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml.Linq;

namespace M2Server.Lib.Services;

[SupportedOSPlatform("windows")]
public static class StartupService
{
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string? ScheduledExecutablePath(out bool elevated)
    {
        elevated = false;
        using var process
            = Process.Start(
                new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList = { "/Query", "/TN", Constants.ScheduledTaskName, "/XML" }
                }
            ) ?? throw new InvalidOperationException("Could not query startup task.");
        var xml = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var task = XDocument.Parse(xml);
        elevated = task.Descendants().Any(element => element.Name.LocalName == "RunLevel" && element.Value == "HighestAvailable");
        return task.Descendants().FirstOrDefault(element => element.Name.LocalName == "Command")?.Value;
    }

    public static void Configure(bool enabled, string path)
    {
        if (enabled && !File.Exists(path))
        {
            throw new FileNotFoundException("Executable not found.", path);
        }

        if (!enabled && !TaskExists())
        {
            return;
        }

        var args = enabled
            ? $"/Create /F /TN \"{Constants.ScheduledTaskName}\" /SC ONLOGON /RL LIMITED /TR \"\\\"{path}\\\"\""
            : $"/Delete /F /TN \"{Constants.ScheduledTaskName}\"";

        var startInfo = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private static bool TaskExists()
    {
        using var process = Process.Start(
            new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{Constants.ScheduledTaskName}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            }
        )!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}