using System.Security.Principal;

namespace EtwSnap.Host.Infrastructure;

internal static class CurrentUser
{
    public static string Sid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("The current Windows user does not have a SID.");
}
