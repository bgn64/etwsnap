using EtwSnap.Host.Infrastructure;

namespace EtwSnap.UnitTests.Infrastructure;

public sealed class HostInstanceLockTests
{
    [Fact]
    public void AllowsOnlyOneOwnerAndReleasesOnDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-lock-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "host.lock");
        try
        {
            using var first = HostInstanceLock.TryAcquire(path);
            Assert.NotNull(first);
            Assert.Null(HostInstanceLock.TryAcquire(path));

            first.Dispose();
            using var replacement = HostInstanceLock.TryAcquire(path);
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
