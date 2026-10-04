using System.Diagnostics;
using System.Text.Json;
using M2Server.Lib;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;

namespace M2Server.App;

internal static class ConfigurationSaveService
{
    private const string DuplicateItemIdError = "Duplicate item ID.";
    private const string InvalidProfileError = "Each profile needs a name and one enabled primary monitor.";
    private const string InvalidScriptError = "Each script needs a name.";

    public static async Task SaveAsync(DataStore draft)
    {
        var previous = draft.ReadSaved(document => document);
        var current = draft.Read(document => document);
        Validate(current);
        var startupChanges = previous.Settings.AutoStartup != current.Settings.AutoStartup || (current.Settings.AutoStartup &&
            (previous.Settings.StartupPath != current.Settings.StartupPath ||
             StartupTaskNeedsUpdate(current.Settings.StartupPath)));
        var webMachineChanges = ServiceControl.UsesDataDirectory(draft.DirectoryPath) &&
                                (previous.Settings.AllowLanAccess != current.Settings.AllowLanAccess ||
                                 previous.Settings.AllowPublicLanAccess != current.Settings.AllowPublicLanAccess ||
                                 previous.Settings.HttpsPort != current.Settings.HttpsPort ||
                                 previous.Settings.CertificateThumbprint != current.Settings.CertificateThumbprint);
        var machineChanges = startupChanges || webMachineChanges;
        if (!machineChanges)
        {
            draft.Save();
            return;
        }

        var file = Path.Combine(Path.GetTempPath(), "M2Server-" + Guid.NewGuid().ToString("N") + ".json");
        var oldFile = Path.Combine(Path.GetTempPath(), "M2Server-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(current, AppDocumentJsonContext.Default.AppDocument));
            await File.WriteAllTextAsync(oldFile, JsonSerializer.Serialize(previous, AppDocumentJsonContext.Default.AppDocument));
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            info.ArgumentList.Add(Constants.SaveConfigArgument);
            info.ArgumentList.Add(file);
            info.ArgumentList.Add(oldFile);
            info.ArgumentList.Add(draft.DirectoryPath);
            draft.Save();
            try
            {
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start elevated save.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Saving configuration failed (exit code {process.ExitCode}).");
                }
            }
            catch
            {
                draft.RestoreSaved(previous);
                throw;
            }
        }
        finally
        {
            File.Delete(file);
            File.Delete(oldFile);
        }
    }

    public static void SaveElevated(string source, string previousSource, string dataDirectory)
    {
        var document = JsonSerializer.Deserialize(File.ReadAllText(source), AppDocumentJsonContext.Default.AppDocument) ??
                       throw new InvalidDataException("The configuration is empty.");
        var previous = JsonSerializer.Deserialize(File.ReadAllText(previousSource), AppDocumentJsonContext.Default.AppDocument) ??
                       throw new InvalidDataException("The previous configuration is empty.");
        Validate(document);
        var servicePath = Path.Combine(AppContext.BaseDirectory, Constants.WebServiceExecutableName);
        if (previous.Settings.AutoStartup != document.Settings.AutoStartup || (document.Settings.AutoStartup &&
                                                                               (previous.Settings.StartupPath !=
                                                                                document.Settings.StartupPath ||
                                                                                StartupTaskNeedsUpdate(
                                                                                    document.Settings.StartupPath
                                                                                ))))
        {
            StartupService.Configure(document.Settings.AutoStartup, document.Settings.StartupPath);
        }

        if (ServiceControl.UsesDataDirectory(dataDirectory) && previous.Settings.AllowLanAccess &&
            (!document.Settings.AllowLanAccess || previous.Settings.HttpsPort != document.Settings.HttpsPort))
        {
            FirewallService.RemovePrivateLan(previous.Settings.HttpsPort);
        }

        if (document.Settings.AllowLanAccess && ServiceControl.UsesDataDirectory(dataDirectory))
        {
            FirewallService.AllowPrivateLan(document.Settings.HttpsPort, servicePath);
        }

        if (ServiceControl.UsesDataDirectory(dataDirectory) && previous.Settings.AllowPublicLanAccess &&
            (!document.Settings.AllowPublicLanAccess || previous.Settings.HttpsPort != document.Settings.HttpsPort))
        {
            FirewallService.RemovePublicLan(previous.Settings.HttpsPort);
        }

        if (document.Settings.AllowPublicLanAccess && ServiceControl.UsesDataDirectory(dataDirectory))
        {
            FirewallService.AllowPublicLan(document.Settings.HttpsPort, servicePath);
        }

        if (ServiceControl.UsesDataDirectory(dataDirectory) && (previous.Settings.HttpsPort != document.Settings.HttpsPort ||
                                                                previous.Settings.CertificateThumbprint !=
                                                                document.Settings.CertificateThumbprint ||
                                                                previous.Settings.AllowLanAccess !=
                                                                document.Settings.AllowLanAccess ||
                                                                previous.Settings.AllowPublicLanAccess !=
                                                                document.Settings.AllowPublicLanAccess))
        {
            ServiceControl.RestartIfRunning();
        }
    }

    private static bool StartupTaskNeedsUpdate(string expectedPath)
    {
        var scheduledPath = StartupService.ScheduledExecutablePath(out var elevated);
        return elevated || scheduledPath is null || !string.Equals(
            Path.GetFullPath(scheduledPath.Trim('"')),
            Path.GetFullPath(expectedPath),
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static void Validate(AppDocument document)
    {
        var settingsError = SettingsViewState.ValidatePersistedSettings(document.Settings);
        if (settingsError is not null)
        {
            throw new InvalidDataException(settingsError);
        }

        if (string.IsNullOrWhiteSpace(document.Settings.CertificateThumbprint))
        {
            document.Settings.CertificateThumbprint = null;
        }
        else
        {
            document.Settings.CertificateThumbprint
                = CertificateService.NormalizeThumbprint(document.Settings.CertificateThumbprint);
            if (document.Settings.WebEnabled == true)
            {
                using (new CertificateService().GetServerCertificate(document.Settings.CertificateThumbprint))
                {
                }
            }
        }

        if (document.Presets.Select(p => p.Id).Distinct().Count() != document.Presets.Count ||
            document.Scripts.Select(s => s.Id).Distinct().Count() != document.Scripts.Count)
        {
            throw new InvalidDataException(DuplicateItemIdError);
        }

        foreach (var preset in document.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Monitors.Count(m => m.Enabled && m.Primary) != 1)
            {
                throw new InvalidDataException(InvalidProfileError);
            }
        }

        if (document.Scripts.Any(s => string.IsNullOrWhiteSpace(s.Name)))
        {
            throw new InvalidDataException(InvalidScriptError);
        }
    }
}