using System.Security.Cryptography;
using System.Text;

namespace EtwSnap.Contracts.Protocol;

public enum HostPrivilegeScope
{
    Standard,
    Elevated,
}

public static class UserScopeNames
{
    public static string PipeName(string userSid, int sessionId, HostPrivilegeScope privilegeScope)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        var scope = privilegeScope switch
        {
            HostPrivilegeScope.Standard => "standard",
            HostPrivilegeScope.Elevated => "elevated",
            _ => throw new ArgumentOutOfRangeException(nameof(privilegeScope)),
        };
        return $"{ProtocolConstants.PipeNamePrefix}-{Hash(userSid)}-{sessionId}-{scope}";
    }

    private static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
