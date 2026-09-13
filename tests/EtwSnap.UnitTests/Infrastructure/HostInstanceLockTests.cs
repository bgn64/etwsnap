using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.Infrastructure;

namespace EtwSnap.UnitTests.Infrastructure;

public sealed class HostInstanceLockTests
{
    [Fact]
    public void DefaultPathsSeparateSessionsAndPrivilegeScopes()
    {
        var standard = HostInstanceLock.GetDefaultPath(HostPrivilegeScope.Standard, 12);
        var elevated = HostInstanceLock.GetDefaultPath(HostPrivilegeScope.Elevated, 12);
        var otherSession = HostInstanceLock.GetDefaultPath(HostPrivilegeScope.Standard, 13);

        Assert.NotEqual(standard, elevated);
        Assert.NotEqual(standard, otherSession);
        Assert.Contains("standard", standard, StringComparison.Ordinal);
        Assert.Contains("elevated", elevated, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsOnlyOneOwnerAndReleasesOnDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-lock-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "host.lock");
        try
        {
            using var first = HostInstanceLock.TryAcquire(HostPrivilegeScope.Standard, 1, path);
            Assert.NotNull(first);
            Assert.Null(HostInstanceLock.TryAcquire(HostPrivilegeScope.Standard, 1, path));

            first.Dispose();
            using var replacement = HostInstanceLock.TryAcquire(HostPrivilegeScope.Standard, 1, path);
            Assert.NotNull(replacement);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
