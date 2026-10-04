using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace M2Server.Lib.Services;

public static class AvailablePortService
{
    public const int MinimumPort = 49152;
    public const int MaximumPort = 65535;

    public static int Choose()
    {
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(MinimumPort, MaximumPort + 1);
            try
            {
                using var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // Another listener already owns this port.
            }
        }

        throw new InvalidOperationException("No available web port was found in the safe range.");
    }
}