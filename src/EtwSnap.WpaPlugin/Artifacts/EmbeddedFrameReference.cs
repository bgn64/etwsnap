using System.Collections.Concurrent;

namespace EtwSnap.WpaPlugin.Artifacts;

public sealed record EmbeddedFrameReference(
    string EtlPath,
    string StreamName,
    Guid SessionId,
    string EntryPath,
    EtwSnap.Artifacts.EmbeddedBundleDescriptor Descriptor)
{
    public string DisplayPath => $"{EtlPath}:{StreamName}!/{EntryPath}";

    public string Materialize() => EmbeddedFrameMaterializer.Materialize(this);
}

internal static class EmbeddedFrameMaterializer
{
    private const long MaximumCacheBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan MaximumCacheAge = TimeSpan.FromDays(30);
    private static readonly ConcurrentDictionary<string, object> EntryGates = new(StringComparer.OrdinalIgnoreCase);
    private static int _cleanupStarted;

    public static string Materialize(EmbeddedFrameReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var indexed = reference.Descriptor.Entries.SingleOrDefault(
            entry => string.Equals(entry.Path, reference.EntryPath, StringComparison.Ordinal))
            ?? throw new EtwSnap.Artifacts.EmbeddedArtifactException($"The embedded frame is not indexed: {reference.EntryPath}");
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtwSnap",
            "WpaCache");
        var destination = Path.Combine(
            cacheRoot,
            reference.Descriptor.PrimaryEtlSha256,
            reference.SessionId.ToString("N"),
            reference.EntryPath.Replace('/', Path.DirectorySeparatorChar));
        EnsureCachePath(cacheRoot, destination);
        var gate = EntryGates.GetOrAdd(destination, static _ => new object());
        lock (gate)
        {
            if (IsValidCachedFile(destination, indexed))
            {
                File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
                return destination;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            using var stream = new EtwSnap.Artifacts.NamedStreamStore().OpenRead(reference.EtlPath, reference.StreamName);
            new EtwSnap.Artifacts.EmbeddedBundle().ExtractEntryAsync(
                stream,
                reference.Descriptor,
                reference.EntryPath,
                destination,
                CancellationToken.None).GetAwaiter().GetResult();
            if (Interlocked.Exchange(ref _cleanupStarted, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(static state => Cleanup((string)state!), cacheRoot);
            }
            return destination;
        }
    }

    private static bool IsValidCachedFile(string path, EtwSnap.Artifacts.EmbeddedBundleEntry expected)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expected.Length)
        {
            return false;
        }
        var hash = EtwSnap.Artifacts.EmbeddedBundle.HashFileAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        return string.Equals(hash, expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void Cleanup(string cacheRoot)
    {
        try
        {
            if (!Directory.Exists(cacheRoot) ||
                (File.GetAttributes(cacheRoot) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }
            var files = Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastAccessTimeUtc)
                .ToArray();
            var cutoff = DateTime.UtcNow - MaximumCacheAge;
            long retainedBytes = 0;
            foreach (var file in files)
            {
                if (file.LastAccessTimeUtc < cutoff || checked(retainedBytes + file.Length) > MaximumCacheBytes)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    retainedBytes += file.Length;
                }
            }
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref _cleanupStarted, 0);
        }
    }

    private static void EnsureCachePath(string cacheRoot, string destination)
    {
        Directory.CreateDirectory(cacheRoot);
        EtwSnap.Artifacts.NamedStreamStore.RejectReparsePoint(cacheRoot);
        var current = cacheRoot;
        foreach (var segment in Path.GetRelativePath(cacheRoot, Path.GetDirectoryName(destination)!).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            EtwSnap.Artifacts.NamedStreamStore.RejectReparsePoint(current);
        }
    }
}