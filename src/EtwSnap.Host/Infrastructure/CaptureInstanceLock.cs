using EtwSnap.Contracts.Protocol;

namespace EtwSnap.Host.Infrastructure;

internal sealed class CaptureInstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private CaptureInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    public static CaptureInstanceLock? TryAcquire(int sessionId, string? lockPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        var path = lockPath ?? GetDefaultPath(sessionId);
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The lock path must have a parent directory.", nameof(lockPath));
        Directory.CreateDirectory(directory);

        try
        {
            return new CaptureInstanceLock(new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static string GetDefaultPath(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtwSnap",
            $"capture-v{ProtocolConstants.CurrentVersion}-{sessionId}.lock");
    }

    public void Dispose() => _stream.Dispose();
}