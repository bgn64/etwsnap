namespace EtwSnap.Host.Infrastructure;

internal sealed class HostInstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private HostInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    public static HostInstanceLock? TryAcquire(string? lockPath = null)
    {
        var path = lockPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtwSnap",
            "host.lock");
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The lock path must have a parent directory.", nameof(lockPath));
        Directory.CreateDirectory(directory);

        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new HostInstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
