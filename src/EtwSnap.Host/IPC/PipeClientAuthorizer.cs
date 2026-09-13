using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using EtwSnap.Contracts.Protocol;
using Microsoft.Win32.SafeHandles;

namespace EtwSnap.Host.IPC;

internal sealed class PipeClientAuthorizer(
    string expectedSid,
    int expectedSessionId,
    HostPrivilegeScope expectedPrivilegeScope)
{
    public bool IsAuthorized(NamedPipeServerStream pipe)
    {
        try
        {
            var identity = ReadIdentity(pipe);
            return string.Equals(identity.Sid, expectedSid, StringComparison.Ordinal) &&
                identity.SessionId == expectedSessionId &&
                identity.PrivilegeScope == expectedPrivilegeScope;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static PipeClientIdentity ReadIdentity(NamedPipeServerStream pipe)
    {
        PipeClientIdentity? clientIdentity = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true)
                ?? throw new InvalidOperationException("The named-pipe client did not provide an impersonation token.");
            var sid = identity.User?.Value
                ?? throw new InvalidOperationException("The named-pipe client token does not have a user SID.");
            var sessionId = checked((int)GetTokenUInt32(identity.AccessToken, TokenInformationClass.TokenSessionId));
            var privilegeScope = GetTokenUInt32(identity.AccessToken, TokenInformationClass.TokenElevation) != 0
                ? HostPrivilegeScope.Elevated
                : HostPrivilegeScope.Standard;
            clientIdentity = new PipeClientIdentity(sid, sessionId, privilegeScope);
        });
        return clientIdentity
            ?? throw new InvalidOperationException("The named-pipe client identity could not be read.");
    }

    private static uint GetTokenUInt32(SafeAccessTokenHandle token, TokenInformationClass informationClass)
    {
        if (!GetTokenInformation(token, informationClass, out var value, sizeof(uint), out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return value;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        out uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    private enum TokenInformationClass
    {
        TokenSessionId = 12,
        TokenElevation = 20,
    }

    private sealed record PipeClientIdentity(
        string Sid,
        int SessionId,
        HostPrivilegeScope PrivilegeScope);
}