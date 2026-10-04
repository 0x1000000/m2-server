using System.Security.Cryptography;
using System.Text;

namespace M2Server.Lib.Infrastructure;

public sealed class Security(DataStore store)
{
    public bool HasPassword => store.Read(x => !string.IsNullOrEmpty(x.Settings.PasswordHash));

    public void SetPassword(string password)
    {
        if (password.Length < 8)
        {
            throw new ArgumentException("Use at least 8 characters.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        store.Update(x =>
            {
                x.Settings.PasswordHash = Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
                x.Tokens.Clear();
                return 0;
            }
        );
    }

    public void RevokeSessions()
    {
        store.Update(x =>
            {
                x.Tokens.Clear();
                return 0;
            }
        );
    }

    public string? Login(string password)
    {
        if (!store.Read(x => x.Settings.WebEnabled ?? true))
        {
            return null;
        }

        var saved = store.Read(x => x.Settings.PasswordHash);
        if (saved is null)
        {
            return null;
        }

        var parts = saved.Split(':');
        if (parts.Length != 2)
        {
            return null;
        }

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return null;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        store.Update(x =>
            {
                x.Tokens[Hash(token)] = DateTimeOffset.UtcNow.AddYears(1);
                return 0;
            }
        );
        return token;
    }

    public bool Valid(string? authorization)
    {
        if (!store.Read(x => x.Settings.WebEnabled ?? true))
        {
            return false;
        }

        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return false;
        }

        var digest = Hash(authorization[7..]);
        return store.Read(x => x.Tokens.TryGetValue(digest, out var expiry) && expiry > DateTimeOffset.UtcNow);
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}