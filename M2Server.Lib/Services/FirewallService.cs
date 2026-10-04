using System.Diagnostics;
using System.Runtime.Versioning;

namespace M2Server.Lib.Services;

[SupportedOSPlatform("windows")]
public static class FirewallService
{
    public static void AllowPrivateLan(int port, string executablePath)
    {
        AllowLan(port, executablePath, "private");
    }

    public static void AllowPublicLan(int port, string executablePath)
    {
        AllowLan(port, executablePath, "public");
    }

    public static void RemovePrivateLan(int port)
    {
        RemoveLan(port, "private");
    }

    public static void RemovePublicLan(int port)
    {
        RemoveLan(port, "public");
    }

    private static void AllowLan(int port, string executablePath, string profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Application executable not found.", executablePath);
        }

        var ruleName = RuleName(port, profile);
        if (RuleExists(ruleName))
        {
            RunElevated($"advfirewall firewall delete rule name=\"{ruleName}\"");
        }

        var remoteScope = profile == "private" ? "remoteip=localsubnet " : "";
        var arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow " +
                        $"protocol=TCP localport={port} {remoteScope}profile={profile} " +
                        $"program=\"{executablePath}\" enable=yes";
        RunElevated(arguments);
    }

    private static void RemoveLan(int port, string profile)
    {
        var ruleName = RuleName(port, profile);
        if (RuleExists(ruleName))
        {
            RunElevated($"advfirewall firewall delete rule name=\"{ruleName}\"");
        }
    }

    private static string RuleName(int port, string profile)
    {
        return profile == "private" ? $"{Constants.ProductName} HTTPS {port}" : $"{Constants.ProductName} HTTPS Public {port}";
    }

    private static bool RuleExists(string ruleName)
    {
        using var query = Process.Start(
            new ProcessStartInfo("netsh.exe", $"advfirewall firewall show rule name=\"{ruleName}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            }
        )!;
        query.StandardOutput.ReadToEnd();
        query.WaitForExit();
        return query.ExitCode == 0;
    }

    private static void RunElevated(string arguments)
    {
        var administrator = StartupService.IsAdministrator;
        var startInfo = new ProcessStartInfo("netsh.exe", arguments)
        {
            UseShellExecute = !administrator,
            CreateNoWindow = administrator,
            RedirectStandardError = administrator,
            RedirectStandardOutput = administrator
        };
        if (!administrator)
        {
            startInfo.Verb = "runas";
        }

        using var process = Process.Start(startInfo)!;
        var output = administrator ? process.StandardOutput.ReadToEnd() : "";
        var error = administrator ? process.StandardError.ReadToEnd() : "";
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? string.IsNullOrWhiteSpace(output) ? "Windows Firewall did not accept the rule." : output
                    : error
            );
        }
    }
}