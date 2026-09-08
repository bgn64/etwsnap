using System.Security.Cryptography;
using System.Text;

namespace EtwSnap.Contracts.Protocol;

public static class UserScopeNames
{
    public static string PipeName(string userSid) => $"{ProtocolConstants.PipeNamePrefix}-{Hash(userSid)}";

    private static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
