using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace EtwSnap.Artifacts;

public sealed class EmbeddedBundle
{
    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<EmbeddedBundleDescriptor> CreateAsync(
        string sessionDirectory,
        string primaryEtlPath,
        Guid sessionId,
        Guid providerId,
        int manifestSchemaVersion,
        string destinationZipPath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(sessionDirectory);
        NamedStreamStore.RejectReparsePoint(root);
        var manifestPath = Path.Combine(root, EmbeddedArtifactConstants.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new EmbeddedArtifactException($"The session manifest does not exist: {manifestPath}");
        }

        var files = EnumerateArtifactFiles(root)
            .Select(path => new { FullPath = path, RelativePath = NormalizeEntryName(Path.GetRelativePath(root, path)) })
            .Where(file => !string.Equals(file.FullPath, Path.GetFullPath(primaryEtlPath), StringComparison.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file.RelativePath, "trace.etl", StringComparison.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file.RelativePath, ".reserved", StringComparison.Ordinal))
            .Where(file => !file.RelativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0 || files.Length > EmbeddedArtifactConstants.MaximumEntryCount)
        {
            throw new EmbeddedArtifactException("The session contains an invalid number of artifact entries.");
        }

        var entries = new List<EmbeddedBundleEntry>(files.Length);
        long expandedBytes = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = new FileInfo(file.FullPath).Length;
            ValidateEntrySize(file.RelativePath, length);
            expandedBytes = checked(expandedBytes + length);
            if (expandedBytes > EmbeddedArtifactConstants.MaximumExpandedBytes)
            {
                throw new EmbeddedArtifactException("The session artifacts exceed the maximum expanded bundle size.");
            }
            entries.Add(new EmbeddedBundleEntry(file.RelativePath, length, await HashFileAsync(file.FullPath, cancellationToken).ConfigureAwait(false)));
        }

        var descriptor = new EmbeddedBundleDescriptor(
            EmbeddedArtifactConstants.BundleSchemaVersion,
            sessionId,
            providerId,
            DateTimeOffset.UtcNow,
            manifestSchemaVersion,
            await HashFileAsync(manifestPath, cancellationToken).ConfigureAwait(false),
            await HashFileAsync(primaryEtlPath, cancellationToken).ConfigureAwait(false),
            entries);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationZipPath))!);
        await using (var output = new FileStream(destinationZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            var descriptorEntry = archive.CreateEntry(EmbeddedArtifactConstants.DescriptorFileName, CompressionLevel.NoCompression);
            descriptorEntry.LastWriteTime = DeterministicZipTimestamp;
            await using (var descriptorStream = descriptorEntry.Open())
            {
                await JsonSerializer.SerializeAsync(
                    descriptorStream,
                    descriptor,
                    EmbeddedBundleJson.Options,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.NoCompression);
                entry.LastWriteTime = DeterministicZipTimestamp;
                await using var entryStream = entry.Open();
                await using var source = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }
        }

        await using var verification = File.OpenRead(destinationZipPath);
        await InspectAsync(verification, primaryEtlPath, sessionId, cancellationToken).ConfigureAwait(false);
        return descriptor;
    }

    public async Task<EmbeddedBundleInspection> InspectAsync(
        Stream bundleStream,
        string primaryEtlPath,
        Guid? expectedSessionId,
        CancellationToken cancellationToken)
    {
        if (!bundleStream.CanRead || !bundleStream.CanSeek)
        {
            throw new EmbeddedArtifactException("The embedded bundle stream must be readable and seekable.");
        }

        bundleStream.Position = 0;
        using var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 || archive.Entries.Count > EmbeddedArtifactConstants.MaximumEntryCount + 1)
        {
            throw new EmbeddedArtifactException("The bundle contains an invalid number of entries.");
        }

        var entriesByName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long archiveExpandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeEntryName(entry.FullName);
            if (!string.Equals(normalized, entry.FullName, StringComparison.Ordinal))
            {
                throw new EmbeddedArtifactException($"The bundle entry path is not canonical: {entry.FullName}");
            }
            if (!entriesByName.TryAdd(normalized, entry))
            {
                throw new EmbeddedArtifactException($"The bundle contains a duplicate entry: {normalized}");
            }
            ValidateEntrySize(normalized, entry.Length);
            archiveExpandedBytes = checked(archiveExpandedBytes + entry.Length);
            if (archiveExpandedBytes > EmbeddedArtifactConstants.MaximumExpandedBytes)
            {
                throw new EmbeddedArtifactException("The bundle exceeds the maximum expanded size.");
            }
        }

        if (!entriesByName.TryGetValue(EmbeddedArtifactConstants.DescriptorFileName, out var descriptorEntry))
        {
            throw new EmbeddedArtifactException("The bundle descriptor is missing.");
        }
        if (descriptorEntry.Length > EmbeddedArtifactConstants.MaximumDescriptorBytes)
        {
            throw new EmbeddedArtifactException("The bundle descriptor exceeds the size limit.");
        }

        EmbeddedBundleDescriptor descriptor;
        await using (var descriptorStream = descriptorEntry.Open())
        {
            descriptor = await JsonSerializer.DeserializeAsync<EmbeddedBundleDescriptor>(
                descriptorStream,
                EmbeddedBundleJson.Options,
                cancellationToken).ConfigureAwait(false)
                ?? throw new EmbeddedArtifactException("The bundle descriptor is invalid.");
        }

        if (descriptor.SchemaVersion != EmbeddedArtifactConstants.BundleSchemaVersion ||
            descriptor.ProviderId == Guid.Empty ||
            expectedSessionId is not null && descriptor.SessionId != expectedSessionId)
        {
            throw new EmbeddedArtifactException("The bundle identity or schema is not supported.");
        }

        var indexedPaths = new HashSet<string>(StringComparer.Ordinal);
        long expandedBytes = 0;
        byte[]? manifestBytes = null;
        if (descriptor.Entries is null || descriptor.Entries.Count == 0 ||
            descriptor.Entries.Count > EmbeddedArtifactConstants.MaximumEntryCount ||
            !descriptor.Entries.Any(entry => string.Equals(entry.Path, EmbeddedArtifactConstants.ManifestFileName, StringComparison.Ordinal)))
        {
            throw new EmbeddedArtifactException("The bundle entry index is invalid.");
        }
        var indexedManifest = descriptor.Entries.Single(
            entry => string.Equals(entry.Path, EmbeddedArtifactConstants.ManifestFileName, StringComparison.Ordinal));
        if (indexedManifest.Length > EmbeddedArtifactConstants.MaximumManifestBytes)
        {
            throw new EmbeddedArtifactException("The session manifest exceeds the size limit.");
        }

        foreach (var expected in descriptor.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizeEntryName(expected.Path);
            if (!string.Equals(path, expected.Path, StringComparison.Ordinal))
            {
                throw new EmbeddedArtifactException($"The bundle entry index path is not canonical: {expected.Path}");
            }
            if (!indexedPaths.Add(path) || !entriesByName.TryGetValue(path, out var entry))
            {
                throw new EmbeddedArtifactException($"The bundle entry index is invalid: {path}");
            }
            if (entry.Length != expected.Length)
            {
                throw new EmbeddedArtifactException($"The bundle entry length does not match: {path}");
            }
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > EmbeddedArtifactConstants.MaximumExpandedBytes)
            {
                throw new EmbeddedArtifactException("The bundle exceeds the maximum expanded size.");
            }

            await using var stream = entry.Open();
            using var memory = path == EmbeddedArtifactConstants.ManifestFileName ? new MemoryStream() : null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                if (memory is not null)
                {
                    await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new EmbeddedArtifactException($"The bundle entry hash does not match: {path}");
            }
            if (memory is not null)
            {
                manifestBytes = memory.ToArray();
            }
        }

        if (entriesByName.Keys.Any(path => path != EmbeddedArtifactConstants.DescriptorFileName && !indexedPaths.Contains(path)))
        {
            throw new EmbeddedArtifactException("The bundle contains an unindexed entry.");
        }
        if (manifestBytes is null ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(), descriptor.ManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddedArtifactException("The manifest is missing or its hash does not match.");
        }
        var etlHash = await HashFileAsync(primaryEtlPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(etlHash, descriptor.PrimaryEtlSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddedArtifactException("The embedded artifacts do not belong to this ETL primary stream.");
        }

        return new EmbeddedBundleInspection(descriptor, manifestBytes, bundleStream.Length, expandedBytes);
    }

    public async Task ExtractEntryAsync(
        Stream bundleStream,
        EmbeddedBundleDescriptor descriptor,
        string entryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeEntryName(entryPath);
        var expected = descriptor.Entries.SingleOrDefault(entry => string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
            ?? throw new EmbeddedArtifactException($"The requested entry is not indexed: {normalizedPath}");
        bundleStream.Position = 0;
        using var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(normalizedPath)
            ?? throw new EmbeddedArtifactException($"The requested entry is missing: {normalizedPath}");

        var canonicalDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(canonicalDestination)!);
        var temporaryPath = canonicalDestination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = entry.Open())
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            var info = new FileInfo(temporaryPath);
            if (info.Length != expected.Length ||
                !string.Equals(await HashFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new EmbeddedArtifactException($"The extracted entry failed verification: {normalizedPath}");
            }
            File.Move(temporaryPath, canonicalDestination, overwrite: false);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static string NormalizeEntryName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw new EmbeddedArtifactException($"The bundle entry path is not relative: {path}");
        }
        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new EmbeddedArtifactException($"The bundle entry path is unsafe: {path}");
        }
        return string.Join('/', segments);
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidateEntrySize(string path, long length)
    {
        if (length < 0 || length > EmbeddedArtifactConstants.MaximumEntryBytes)
        {
            throw new EmbeddedArtifactException($"The bundle entry exceeds the size limit: {path}");
        }
    }

    private static IEnumerable<string> EnumerateArtifactFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new EmbeddedArtifactException($"Session artifacts cannot traverse reparse points: {path}");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
                else
                {
                    yield return path;
                }
            }
        }
    }
}