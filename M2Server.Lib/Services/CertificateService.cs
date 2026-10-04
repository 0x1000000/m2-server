using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace M2Server.Lib.Services;

[SupportedOSPlatform("windows")]
public sealed class CertificateService
{
    public const string CertificateNotFoundError = "The HTTPS certificate was not found in Local Machine / Personal.";
    public const string PrivateKeyRequiredError = "The HTTPS certificate must have a private key accessible to the service.";
    public const string CertificateExpiredError = "The HTTPS certificate is not currently valid.";

    public string? LanIp => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        )
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a))
        .Select(a => a.ToString())
        .FirstOrDefault();

    public X509Certificate2 GetServerCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var normalized = NormalizeThumbprint(thumbprint);
        var certificate = store.Certificates.FirstOrDefault(c => string.Equals(
                NormalizeThumbprint(c.Thumbprint),
                normalized,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (certificate is null)
        {
            throw new InvalidOperationException(CertificateNotFoundError);
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(PrivateKeyRequiredError);
        }

        if (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
            certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            throw new InvalidOperationException(CertificateExpiredError);
        }

        return certificate;
    }

    public static string NormalizeThumbprint(string thumbprint)
    {
        return new string(thumbprint.Where(c => !char.IsWhiteSpace(c) && c != ':').ToArray());
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}