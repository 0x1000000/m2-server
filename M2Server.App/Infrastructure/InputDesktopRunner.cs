using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace M2Server.App.Infrastructure;

internal static class InputDesktopRunner
{
    private const uint DesktopAllAccess = 0x000F01FF;
    private const int UserObjectName = 2;

    public static T Run<T>(Func<T> action, ILogger logger)
    {
        T? result = default;
        Exception? failure = null;
        nint desktop = 0;

        var thread = new Thread(() =>
            {
                try
                {
                    desktop = OpenInputDesktop(0, false, DesktopAllAccess);
                    if (desktop == 0)
                    {
                        throw Win32Failure("OpenInputDesktop");
                    }

                    if (!SetThreadDesktop(desktop))
                    {
                        throw Win32Failure("SetThreadDesktop");
                    }

                    var name = new StringBuilder(256);
                    if (GetUserObjectInformation(desktop, UserObjectName, name, name.Capacity * sizeof(char), out _))
                    {
                        logger.LogInformation("Profile activation is using input desktop {Desktop}", name.ToString());
                    }
                    else
                    {
                        logger.LogWarning(
                            "Profile activation is using the input desktop, but its name could not be read: Windows error {WindowsError}",
                            Marshal.GetLastPInvokeError()
                        );
                    }

                    result = action();
                }
                catch (Exception error)
                {
                    failure = error;
                    logger.LogError(error, "Profile activation could not run on the input desktop");
                }
            }
        ) { Name = "M2 Server profile activation", IsBackground = true };

        thread.Start();
        thread.Join();
        if (desktop != 0 && !CloseDesktop(desktop))
        {
            logger.LogWarning(
                "Could not close the input desktop handle: Windows error {WindowsError}",
                Marshal.GetLastPInvokeError()
            );
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private static Win32Exception Win32Failure(string operation)
    {
        var code = Marshal.GetLastPInvokeError();
        return new Win32Exception(code, $"{operation} failed ({code}: {new Win32Exception(code).Message}).");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(nint desktop);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        nint userObject,
        int index,
        StringBuilder information,
        int length,
        out int neededLength);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);
}