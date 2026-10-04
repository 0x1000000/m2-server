using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using M2Server.Lib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M2Server.Web;

public sealed class ConsoleExecutorManager(string dataDirectory, ILogger<ConsoleExecutorManager>? logger = null) : IDisposable
{
    private readonly bool _isLocalSystem = IsLocalSystem();
    private readonly ILogger<ConsoleExecutorManager> _logger = logger ?? NullLogger<ConsoleExecutorManager>.Instance;
    private readonly SemaphoreSlim _requests = new(1, 1);

    public void Dispose()
    {
        this._requests.Dispose();
    }

    public async Task<CommandResult> RunAsync(string argument, Guid id, CancellationToken cancellationToken = default)
    {
        if (argument is not (Constants.ApplyProfileArgument or Constants.RunScriptArgument))
        {
            throw new ArgumentOutOfRangeException(nameof(argument));
        }

        await this._requests.WaitAsync(cancellationToken);
        try
        {
            var consoleSessionId = WTSGetActiveConsoleSessionId();
            this._logger.LogInformation(
                "Starting app command {Argument} {Id}; dataDirectory={DataDirectory}, identity={Identity}, processSessionId={ProcessSessionId}, activeConsoleSessionId={ActiveConsoleSessionId}, launchAsSystem={LaunchAsSystem}",
                argument,
                id,
                dataDirectory,
                WindowsIdentity.GetCurrent().Name,
                Process.GetCurrentProcess().SessionId,
                consoleSessionId == uint.MaxValue ? null : consoleSessionId,
                this._isLocalSystem
            );
            var appPath = Path.Combine(AppContext.BaseDirectory, Constants.AppExecutableName);
            if (!File.Exists(appPath))
            {
                this._logger.LogError(
                    "Cannot start app command {Argument} {Id}: executable is missing at {AppPath}",
                    argument,
                    id,
                    appPath
                );
                return new CommandResult(null, "M2Server.App.exe is missing beside the web host.");
            }

            var result = this._isLocalSystem
                ? await this.RunInConsoleSessionAsync(appPath, argument, id, dataDirectory, cancellationToken)
                : await RunLocallyAsync(appPath, argument, id, dataDirectory, cancellationToken);
            this._logger.Log(
                result.ExitCode == 0 ? LogLevel.Information : LogLevel.Warning,
                "App command {Argument} {Id} completed with exitCode={ExitCode}; error={Error}",
                argument,
                id,
                result.ExitCode,
                result.Error
            );
            return result;
        }
        catch (OperationCanceledException)
        {
            this._logger.LogWarning("App command {Argument} {Id} timed out or was cancelled", argument, id);
            return new CommandResult(null, "The app command timed out or was cancelled.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception
                                          or InvalidOperationException)
        {
            this._logger.LogError(error, "Could not run app command {Argument} {Id}", argument, id);
            return new CommandResult(null, "The app command could not run: " + error.Message);
        }
        finally
        {
            this._requests.Release();
        }
    }

    private static async Task<CommandResult> RunLocallyAsync(
        string appPath,
        string argument,
        Guid id,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add(argument);
        info.ArgumentList.Add(id.ToString("D"));
        if (argument == Constants.ApplyProfileArgument)
        {
            info.ArgumentList.Add(Constants.ParentNotifiesArgument);
        }

        info.ArgumentList.Add(Constants.DataDirectoryArgument);
        info.ArgumentList.Add(dataDirectory);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch M2Server.App.exe.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
            return new CommandResult(process.ExitCode, null);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    private async Task<CommandResult> RunInConsoleSessionAsync(
        string appPath,
        string argument,
        Guid id,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        var process = this.StartChild(appPath, argument, id, dataDirectory);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            uint waitResult;
            while ((waitResult = WaitForSingleObject(process, 0)) == 0x102)
            {
                await Task.Delay(100, timeout.Token);
            }

            if (waitResult != 0)
            {
                throw Win32Failure("WaitForSingleObject");
            }

            if (!GetExitCodeProcess(process, out var exitCode))
            {
                throw Win32Failure("GetExitCodeProcess");
            }

            return new CommandResult(checked((int)exitCode), null);
        }
        finally
        {
            if (WaitForSingleObject(process, 0) == 0x102)
            {
                TerminateProcess(process, 1);
            }

            CloseHandle(process);
        }
    }

    private nint StartChild(string appPath, string argument, Guid id, string dataDirectory)
    {
        var notifyArgument = argument == Constants.ApplyProfileArgument ? $" {Constants.ParentNotifiesArgument}" : "";
        var line = new StringBuilder(
            $"\"{appPath}\" {argument} {id:D}{notifyArgument} {Constants.DataDirectoryArgument} \"{dataDirectory}\""
        );
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = Constants.ConsoleDesktopName };
        var session = WTSGetActiveConsoleSessionId();
        if (session == uint.MaxValue)
        {
            throw new InvalidOperationException("Windows has no active console session.");
        }

        if (!OpenProcessToken(GetCurrentProcess(), 0xF01FF, out var current))
        {
            throw Win32Failure("OpenProcessToken");
        }

        try
        {
            if (!DuplicateTokenEx(
                    current,
                    0xF01FF,
                    0,
                    2,
                    1,
                    out var token
                ))
            {
                throw Win32Failure("DuplicateTokenEx");
            }

            try
            {
                if (!SetTokenInformation(token, 12, ref session, sizeof(uint)))
                {
                    throw Win32Failure("SetTokenInformation");
                }

                using var childIdentity = new WindowsIdentity(token);
                if (childIdentity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true)
                {
                    throw new InvalidOperationException("The console child token is not LocalSystem.");
                }

                if (!GetTokenInformation(token, 12, out var tokenSession, sizeof(uint), out _))
                {
                    throw Win32Failure("GetTokenInformation");
                }

                if (tokenSession != session)
                {
                    throw new InvalidOperationException("The console child token does not belong to the active console session.");
                }

                this._logger.LogInformation(
                    "Verified console child token: identity={Identity}, sessionId={SessionId}, desktop={Desktop}",
                    childIdentity.Name,
                    tokenSession,
                    startup.Desktop
                );
                if (!CreateProcessAsUser(
                        token,
                        appPath,
                        line,
                        0,
                        0,
                        false,
                        0x08000000,
                        0,
                        AppContext.BaseDirectory,
                        ref startup,
                        out var child
                    ))
                {
                    throw Win32Failure("CreateProcessAsUser");
                }

                CloseHandle(child.Thread);
                return child.Process;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(current);
        }
    }

    private static Win32Exception Win32Failure(string operation)
    {
        var code = Marshal.GetLastPInvokeError();
        return new Win32Exception(code, $"{operation} failed ({code}: {new Win32Exception(code).Message}).");
    }

    private static bool IsLocalSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        nint token,
        uint access,
        nint attributes,
        int level,
        int type,
        out nint duplicate);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(nint token, int infoClass, ref uint value, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, out uint value, int length, out int returnedLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        nint token,
        string application,
        StringBuilder command,
        nint processAttributes,
        nint threadAttributes,
        bool inheritHandles,
        uint flags,
        nint environment,
        string currentDirectory,
        ref StartupInfo startup,
        out ProcessInfo process);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    public sealed record CommandResult(int? ExitCode, string? Error);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public nint Reserved3, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }
}