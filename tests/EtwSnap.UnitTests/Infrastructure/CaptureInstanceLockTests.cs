using EtwSnap.Host.Infrastructure;

namespace EtwSnap.UnitTests.Infrastructure;

public sealed class CaptureInstanceLockTests
{
    [Fact]
    public void AllowsOnlyOneOwnerAcrossHostsAndReleasesOnDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-capture-lock-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "capture.lock");
        try
        {
            using var first = CaptureInstanceLock.TryAcquire(1, path);
            Assert.NotNull(first);
            Assert.Null(CaptureInstanceLock.TryAcquire(1, path));

            first.Dispose();
            using var replacement = CaptureInstanceLock.TryAcquire(1, path);
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

    [Fact]
    public void DefaultPathsSeparateWindowsSessions()
    {
        Assert.NotEqual(CaptureInstanceLock.GetDefaultPath(12), CaptureInstanceLock.GetDefaultPath(13));
    }
}