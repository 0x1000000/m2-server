namespace M2Server.Lib;

/// <summary>Stable application, storage, and inter-process identifiers shared by all M2 Server hosts.</summary>
public static class Constants
{
    public const string ProductName = "M2 Server";
    public const string GitHubUrl = "https://github.com/0x1000000/m2-server";
    public const string PublisherName = "0x1000000";
    public const string TrayExecutableName = "M2Server.Tray.exe";
    public const string InstallerPrepareArgument = "--installer-prepare";
    public const string InstallerPurgeArgument = "--installer-purge";
    public const string ProfileListRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    public const string ProfileImagePathValue = "ProfileImagePath";
    public const string LocalDataRelativePath = @"AppData\Local";
    public const string CleanupLogPrefix = "M2Server-uninstall-";
    public const string PowerShellRelativePath = @"WindowsPowerShell\v1.0\powershell.exe";
    public const string ServiceControlExecutableName = "sc.exe";
    public const string ProductHeading = "M2 Server";
    public const string SingleInstanceMutexName = "M2Server.SingleInstance";
    public const string UiSingleInstanceMutexName = "M2Server.Ui.SingleInstance";
    public const string TrayRefreshCommand = "REFRESH";
    public const string TrayWindowName = "M2 Server Tray";
    public const string TrayNotificationPipePrefix = @"\\.\pipe\M2Server.Tray.Notify.";
    public const string DataDirectoryName = ProductName;
    public const string DataFileName = "data.json";
    public const string ServiceDataDirectoryFileName = "service-data-directory.txt";
    public const string WebServiceName = "M2ServerWeb";
    public const string WebServiceExecutableName = "M2Server.Web.exe";
    public const string AppExecutableName = "M2Server.App.exe";
    public const string ConsoleDesktopName = @"winsta0\default";
    public const string InstallArgument = "install";
    public const string UninstallArgument = "uninstall";
    public const string ScheduledTaskName = ProductName;
    public const string AvaloniaIconUri = "avares://M2Server.App/Assets/mm.ico";

    //Arguments
    public const string DataDirectoryArgument = "--data-dir";
    public const string UiArgument = "--ui";
    public const string SaveConfigArgument = "--save-config";
    public const string ApplyProfileArgument = "--apply-profile";
    public const string RunScriptArgument = "--run-script";
    public const string ParentNotifiesArgument = "--parent-notifies";
}