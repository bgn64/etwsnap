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

public sealed record EmbeddedArtifactExport(
    string EtlPath,
    IReadOnlyList<string> ArtifactZipPaths,
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
        ValidatedArtifactArchive archive,
        CancellationToken cancellationToken)
    {
        var canonicalEtl = ValidateEtl(etlPath, forMutation: true);
        var sessionId = archive.Manifest.SessionId;
        var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
        if (_streams.Exists(canonicalEtl, streamName))
        {
            throw new EmbeddedArtifactException($"The ETL already contains artifacts for session {sessionId:N}.");
        }

        await _streams.PreflightAsync(Path.GetDirectoryName(canonicalEtl)!, cancellationToken).ConfigureAwait(false);
        var streamCreated = false;
        try
        {
            await _streams.WriteFromFileAsync(canonicalEtl, streamName, archive.ArchivePath, cancellationToken).ConfigureAwait(false);
            streamCreated = true;
            await using var stream = _streams.OpenRead(canonicalEtl, streamName);
            var inspection = await _bundles.InspectAsync(
                stream,
                canonicalEtl,
                sessionId,
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
    }

    public async Task<EmbeddedArtifactExport> ExportAsync(
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
            throw new EmbeddedArtifactException("Every selected embedded artifact stream must be valid before export.");
        }

        var canonicalRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(canonicalRoot);
        NamedStreamStore.RejectReparsePoint(canonicalRoot);
        var stem = Path.GetFileNameWithoutExtension(canonicalEtl);
        var finalEtl = Path.Combine(canonicalRoot, stem + ".etl");
        var zipPaths = selected.Select(item => Path.Combine(
            canonicalRoot,
            selected.Count == 1
                ? EmbeddedArtifactConstants.GetArtifactFileName(stem)
                : EmbeddedArtifactConstants.GetArtifactFileName($"{stem}.{item.SessionId!.Value:N}")))
            .ToArray();
        if (File.Exists(finalEtl) || zipPaths.Any(File.Exists))
        {
            throw new EmbeddedArtifactException("One or more artifact export destinations already exist.");
        }

        var temporaryEtl = finalEtl + $".{Guid.NewGuid():N}.tmp";
        var temporaryZips = zipPaths.Select(path => path + $".{Guid.NewGuid():N}.tmp").ToArray();
        var publishedPaths = new List<string>();
        try
        {
            await CopyPrimaryStreamAsync(canonicalEtl, temporaryEtl, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < selected.Count; ++index)
            {
                await using (var source = _streams.OpenRead(canonicalEtl, selected[index].Stream.Name))
                await using (var destination = new FileStream(temporaryZips[index], FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                }
                await using var verification = File.OpenRead(temporaryZips[index]);
                await _bundles.InspectAsync(
                    verification,
                    temporaryEtl,
                    selected[index].SessionId,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryEtl, finalEtl, overwrite: false);
            publishedPaths.Add(finalEtl);
            for (var index = 0; index < zipPaths.Length; ++index)
            {
                File.Move(temporaryZips[index], zipPaths[index], overwrite: false);
                publishedPaths.Add(zipPaths[index]);
            }
            return new EmbeddedArtifactExport(
                finalEtl,
                zipPaths,
                selected.Select(item => item.SessionId!.Value).ToArray());
        }
        catch
        {
            foreach (var path in publishedPaths.AsEnumerable().Reverse())
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
            throw;
        }
        finally
        {
            File.Delete(temporaryEtl);
            foreach (var path in temporaryZips)
            {
                File.Delete(path);
            }
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
