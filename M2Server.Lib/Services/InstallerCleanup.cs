using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using M2Server.Lib.Infrastructure;
using Microsoft.Win32;

namespace M2Server.Lib.Services;

public interface IInstallerCleanupSystem
{
    string ProgramDataDirectory { get; }

    void RemoveService();

    void StopApplications(string installDirectory);

    void RemoveStartupTask();

    void RemoveFirewallRules();

    IEnumerable<string> ProfileDirectories();

    void DeleteData(string baseDirectory, bool userProfile);
}

public static class InstallerCleanup
{
    public static bool Execute(string phase, string installDirectory, IInstallerCleanupSystem system, Action<string> log)
    {
        if (!Path.IsPathFullyQualified(installDirectory) || Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar)
                .Equals(
                    Path.GetPathRoot(installDirectory)?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                ))
        {
            throw new ArgumentException("The installation directory must be an absolute non-root path.");
        }

        var success = true;

        void Attempt(string operation, Action action)
        {
            try
            {
                action();
                log($"OK: {operation}");
            }
            catch (Exception error)
            {
                success = false;
                log($"FAILED: {operation}: {error}");
            }
        }

        if (phase == Constants.InstallerPrepareArgument)
        {
            Attempt("Remove service", system.RemoveService);
            Attempt("Remove startup task", system.RemoveStartupTask);
            Attempt("Stop applications", () => system.StopApplications(installDirectory));
            Attempt("Remove firewall rules", system.RemoveFirewallRules);
        }
        else if (phase == Constants.InstallerPurgeArgument)
        {
            Attempt(
                "Enumerate user profiles",
                () =>
                {
                    foreach (var profile in system.ProfileDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        Attempt($"Remove application data in {profile}", () => system.DeleteData(profile, true));
                    }
                }
            );
            Attempt("Remove ProgramData", () => system.DeleteData(system.ProgramDataDirectory, false));
        }
        else
        {
            throw new ArgumentException("Unknown installer cleanup phase.", nameof(phase));
        }

        return success;
    }

    [SupportedOSPlatform("windows")]
    public static int Run(string phase, string installDirectory)
    {
        if (!StartupService.IsAdministrator)
        {
            return 5;
        }

        // System temp is outside the data directories being deleted; each run gets its own log.
        var logPath = Path.Combine(Path.GetTempPath(), $"{Constants.CleanupLogPrefix}{Guid.NewGuid():N}.log");
        try
        {
            using var writer
                = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };

            void Log(string message)
            {
                writer.WriteLine($"{DateTime.UtcNow:O} {message}");
                Console.Error.WriteLine(message);
            }

            Log($"{phase}; installation directory: {installDirectory}");
            return Execute(phase, installDirectory, new WindowsInstallerCleanupSystem(), Log) ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Cleanup failed ({logPath}): {error}");
            return 1;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsInstallerCleanupSystem : IInstallerCleanupSystem
{
    public string ProgramDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public void RemoveService()
    {
        using var service = new ServiceController(Constants.WebServiceName);
        try
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status != ServiceControllerStatus.StopPending)
                {
                    service.Stop();
                }

                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        catch (InvalidOperationException error) when (error.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            return;
        }

        var code = Run(
            Path.Combine(Environment.SystemDirectory, Constants.ServiceControlExecutableName),
            "delete",
            Constants.WebServiceName
        );
        if (code is not (0 or 1060 or 1072))
        {
            throw new Win32Exception(code, "Could not remove M2 Server service.");
        }
    }

    public void StopApplications(string installDirectory)
    {
        var root = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var executable in new[]
                 {
                     Constants.TrayExecutableName, Constants.AppExecutableName, Constants.WebServiceExecutableName
                 })
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited)
                    {
                        continue;
                    }

                    var path = process.MainModule?.FileName ??
                               throw new IOException("Cannot determine application process path.");
                    if (!Path.GetFullPath(path).Equals(Path.Combine(root, executable), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                    }

                    if (!process.WaitForExit(10000))
                    {
                        throw new IOException($"Process {process.Id} did not stop.");
                    }
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }
        }
    }

    public void RemoveStartupTask()
    {
        PowerShell(
            $"Get-ScheduledTask -ErrorAction Stop | Where-Object {{ $_.TaskPath -eq '\\' -and $_.TaskName -eq '{Constants.ScheduledTaskName}' }} | Unregister-ScheduledTask -Confirm:$false -ErrorAction Stop"
        );
    }

    public void RemoveFirewallRules()
    {
        PowerShell(
            $"Get-NetFirewallRule -ErrorAction Stop | Where-Object {{ $_.DisplayName -match '^{Constants.ProductName} HTTPS (Public )?([0-9]+)$' -and [int]$Matches[2] -ge 1 -and [int]$Matches[2] -le 65535 }} | Remove-NetFirewallRule -ErrorAction Stop"
        );
    }

    public IEnumerable<string> ProfileDirectories()
    {
        using var profiles = Registry.LocalMachine.OpenSubKey(Constants.ProfileListRegistryPath) ??
                             throw new IOException("Windows profile registration is unavailable.");
        foreach (var name in profiles.GetSubKeyNames())
        {
            using var profile = profiles.OpenSubKey(name) ?? throw new IOException($"Could not read profile {name}.");
            if (profile.GetValue(Constants.ProfileImagePathValue) is not string path || string.IsNullOrWhiteSpace(path))
            {
                throw new IOException($"Profile {name} has no directory.");
            }

            yield return Environment.ExpandEnvironmentVariables(path);
        }
    }

    public void DeleteData(string baseDirectory, bool userProfile)
    {
        ApplicationDataCleanup.Delete(baseDirectory, userProfile);
    }

    private static void PowerShell(string script)
    {
        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(
                "$ErrorActionPreference='Stop'; try { " + script + " } catch { [Console]::Error.WriteLine($_); exit 1 }"
            )
        );
        var code = Run(
            Path.Combine(Environment.SystemDirectory, Constants.PowerShellRelativePath),
            "-NoProfile",
            "-NonInteractive",
            "-EncodedCommand",
            encoded
        );
        if (code != 0)
        {
            throw new IOException($"Windows cleanup command failed ({code}).");
        }
    }

    private static int Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
        if (!process.WaitForExit(60000))
        {
            process.Kill();
            throw new IOException($"Cleanup command timed out: {executable}");
        }

        return process.ExitCode;
    }
}