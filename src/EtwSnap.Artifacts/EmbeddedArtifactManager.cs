namespace EtwSnap.Artifacts;

public sealed record EmbeddedStreamInspection(
    NamedStreamInfo Stream,
    Guid? SessionId,
    EmbeddedBundleInspection? Bundle,
    string? Error)
{
    public bool IsValid => Bundle is not null && Error is null;
}

public sealed record EmbeddedArtifactAddResult(
    string EtlPath,
    string StreamName,
    EmbeddedBundleInspection Bundle);

public sealed record EmbeddedArtifactExtraction(
    string OutputDirectory,
    string EtlPath,
    IReadOnlyList<Guid> SessionIds);

public sealed class EmbeddedArtifactManager
{
    private readonly NamedStreamStore _streams;
    private readonly EmbeddedBundle _bundles;

    public EmbeddedArtifactManager(NamedStreamStore? streams = null, EmbeddedBundle? bundles = null)
    {
        _streams = streams ?? new NamedStreamStore();
        _bundles = bundles ?? new EmbeddedBundle();
    }

    public async Task<IReadOnlyList<EmbeddedStreamInspection>> InspectAsync(
        string etlPath,
        CancellationToken cancellationToken)
    {
        var canonicalEtl = ValidateEtl(etlPath, forMutation: false);
        var result = new List<EmbeddedStreamInspection>();
        foreach (var streamInfo in _streams.EnumerateEtwSnapStreams(canonicalEtl))
        {
            var parsed = EmbeddedArtifactConstants.TryParseStreamName(streamInfo.Name, out var sessionId);
            if (!parsed)
            {
                result.Add(new EmbeddedStreamInspection(
                    streamInfo,
                    null,
                    null,
                    "The ETWSnap stream name does not contain a valid full session ID."));
                continue;
            }
            try
            {
                await using var stream = _streams.OpenRead(canonicalEtl, streamInfo.Name);
                var inspection = await _bundles.InspectAsync(
                    stream,
                    canonicalEtl,
                    sessionId,
                    cancellationToken).ConfigureAwait(false);
                result.Add(new EmbeddedStreamInspection(streamInfo, inspection.Descriptor.SessionId, inspection, null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or OverflowException or System.Text.Json.JsonException)
            {
                result.Add(new EmbeddedStreamInspection(streamInfo, sessionId, null, exception.Message));
            }
        }
        return result;
    }

    public async Task<EmbeddedArtifactAddResult> AddAsync(
        string etlPath,
        ValidatedSessionArtifactFolder session,
        CancellationToken cancellationToken)
    {
        var canonicalEtl = ValidateEtl(etlPath, forMutation: true);
        var streamName = EmbeddedArtifactConstants.GetStreamName(session.Manifest.SessionId);
        if (_streams.Exists(canonicalEtl, streamName))
        {
            throw new EmbeddedArtifactException($"The ETL already contains artifacts for session {session.Manifest.SessionId:N}.");
        }

        await _streams.PreflightAsync(Path.GetDirectoryName(canonicalEtl)!, cancellationToken).ConfigureAwait(false);
        var temporaryZip = Path.Combine(
            Path.GetDirectoryName(canonicalEtl)!,
            $".etwsnap-{session.Manifest.SessionId:N}-{Guid.NewGuid():N}.zip.tmp");
        var streamCreated = false;
        try
        {
            await _bundles.CreateAsync(
                session.DirectoryPath,
                canonicalEtl,
                session.Manifest.SessionId,
                session.Manifest.Provider!.Id,
                session.Manifest.SchemaVersion,
                temporaryZip,
                cancellationToken).ConfigureAwait(false);
            await _streams.WriteFromFileAsync(canonicalEtl, streamName, temporaryZip, cancellationToken).ConfigureAwait(false);
            streamCreated = true;
            await using var stream = _streams.OpenRead(canonicalEtl, streamName);
            var inspection = await _bundles.InspectAsync(
                stream,
                canonicalEtl,
                session.Manifest.SessionId,
                cancellationToken).ConfigureAwait(false);
            return new EmbeddedArtifactAddResult(canonicalEtl, streamName, inspection);
        }
        catch
        {
            if (streamCreated)
            {
                try
                {
                    _streams.Delete(canonicalEtl, streamName);
                }
                catch
                {
                }
            }
            throw;
        }
        finally
        {
            File.Delete(temporaryZip);
        }
    }

    public async Task<EmbeddedArtifactExtraction> ExtractAsync(
        string etlPath,
        IReadOnlyList<EmbeddedStreamInspection> selected,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var canonicalEtl = ValidateEtl(etlPath, forMutation: false);
        if (selected.Count == 0)
        {
            throw new EmbeddedArtifactException("No embedded artifact sessions were selected.");
        }
        if (selected.Any(item => !item.IsValid))
        {
            throw new EmbeddedArtifactException("Every selected embedded artifact stream must be valid before extraction.");
        }

        var canonicalRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(canonicalRoot);
        NamedStreamStore.RejectReparsePoint(canonicalRoot);
        var sourceName = Path.GetFileNameWithoutExtension(canonicalEtl);
        var directoryName = selected.Count == 1
            ? $"{sourceName}-{selected[0].SessionId!.Value:N}"
            : $"{sourceName}-artifacts";
        var destination = Path.Combine(canonicalRoot, directoryName);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EmbeddedArtifactException($"The extraction destination already exists: {destination}");
        }

        Directory.CreateDirectory(destination);
        var marker = Path.Combine(destination, ".reserved");
        await File.WriteAllBytesAsync(marker, [], cancellationToken).ConfigureAwait(false);
        try
        {
            var extractedEtl = Path.Combine(
                destination,
                selected.Count == 1 ? "trace.etl" : Path.GetFileName(canonicalEtl));
            await CopyPrimaryStreamAsync(canonicalEtl, extractedEtl, cancellationToken).ConfigureAwait(false);
            foreach (var item in selected)
            {
                var sessionRoot = selected.Count == 1
                    ? destination
                    : Path.Combine(destination, "sessions", item.SessionId!.Value.ToString("N"));
                await ExtractBundleAsync(canonicalEtl, item, sessionRoot, cancellationToken).ConfigureAwait(false);
            }

            var extractedEtlHash = await EmbeddedBundle.HashFileAsync(extractedEtl, cancellationToken).ConfigureAwait(false);
            if (selected.Any(item => !string.Equals(
                item.Bundle!.Descriptor.PrimaryEtlSha256,
                extractedEtlHash,
                StringComparison.OrdinalIgnoreCase)))
            {
                throw new EmbeddedArtifactException("The extracted ETL failed primary-stream verification.");
            }
            File.Delete(marker);
            return new EmbeddedArtifactExtraction(
                destination,
                extractedEtl,
                selected.Select(item => item.SessionId!.Value).ToArray());
        }
        catch
        {
            throw;
        }
    }

    public void Remove(string etlPath, IEnumerable<EmbeddedStreamInspection> selected)
    {
        var canonicalEtl = ValidateEtl(etlPath, forMutation: true);
        foreach (var item in selected)
        {
            _streams.Delete(canonicalEtl, item.Stream.Name);
        }
    }

    private async Task ExtractBundleAsync(
        string etlPath,
        EmbeddedStreamInspection inspection,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var descriptor = inspection.Bundle!.Descriptor;
        var entries = descriptor.Entries
            .OrderBy(entry => entry.Path == EmbeddedArtifactConstants.ManifestFileName ? 1 : 0)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in entries)
        {
            var destinationPath = SessionArtifactFolder.ResolveContainedPath(destination, entry.Path);
            await using var stream = _streams.OpenRead(etlPath, inspection.Stream.Name);
            await _bundles.ExtractEntryAsync(stream, descriptor, entry.Path, destinationPath, cancellationToken).ConfigureAwait(false);
        }

        foreach (var entry in descriptor.Entries)
        {
            var path = SessionArtifactFolder.ResolveContainedPath(destination, entry.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Length ||
                !string.Equals(
                    await EmbeddedBundle.HashFileAsync(path, cancellationToken).ConfigureAwait(false),
                    entry.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new EmbeddedArtifactException($"The extracted artifact failed verification: {entry.Path}");
            }
        }
    }

    private static async Task CopyPrimaryStreamAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static string ValidateEtl(string etlPath, bool forMutation)
    {
        var canonicalPath = Path.GetFullPath(etlPath);
        if (!File.Exists(canonicalPath) || !string.Equals(Path.GetExtension(canonicalPath), ".etl", StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddedArtifactException($"A regular existing .etl file is required: {canonicalPath}");
        }
        if (forMutation)
        {
            NamedStreamStore.RejectReparsePoint(canonicalPath);
        }
        return canonicalPath;
    }
}