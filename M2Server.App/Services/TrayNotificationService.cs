using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using M2Server.Lib;
using Microsoft.Win32.SafeHandles;

namespace M2Server.App.Services;

internal sealed class TrayNotificationService : INotificationService
{
    private const int MessageLength = 256;
    private const uint WriteDataAndSynchronize = 0x00100002;
    private const uint OpenExisting = 3;

    public void Show(string message, bool warning = false)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var name = Constants.TrayNotificationPipePrefix + process.SessionId;
            if (!WaitNamedPipe(name, 100))
            {
                return;
            }

            using var pipe = CreateFile(
                name,
                WriteDataAndSynchronize,
                0,
                0,
                OpenExisting,
                0,
                0
            );
            if (pipe.IsInvalid)
            {
                return;
            }

            var packet = new byte[sizeof(int) + MessageLength * sizeof(char)];
            BinaryPrimitives.WriteInt32LittleEndian(packet, warning ? 1 : 0);
            var length = Math.Min(message.Length, MessageLength - 1);
            if (length > 0 && char.IsHighSurrogate(message[length - 1]))
            {
                length--;
            }

            Encoding.Unicode.GetBytes(message.AsSpan(0, length), packet.AsSpan(sizeof(int)));
            WriteFile(pipe, packet, (uint)packet.Length, out _, 0);
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException
                                          or InvalidOperationException)
        {
            // Notifications are optional when the tray is unavailable.
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WaitNamedPipe(string name, uint timeout);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string name,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        uint numberOfBytesToWrite,
        out uint numberOfBytesWritten,
        nint overlapped);
}