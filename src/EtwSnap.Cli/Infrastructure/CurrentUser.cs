using System.Diagnostics;
using System.Security.Principal;
using EtwSnap.Contracts.Protocol;

namespace EtwSnap.Cli.Infrastructure;

internal static class CurrentUser
{
    public static string Sid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("The current Windows user does not have a SID.");

    public static int SessionId => Process.GetCurrentProcess().SessionId;

    public static HostPrivilegeScope PrivilegeScope
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                ? HostPrivilegeScope.Elevated
                : HostPrivilegeScope.Standard;
        }
    }
}
