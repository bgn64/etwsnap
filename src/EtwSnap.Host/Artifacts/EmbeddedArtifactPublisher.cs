using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Artifacts;

internal sealed record EmbeddedArtifactReservation(
    ArtifactReservation Staging,
    string FinalEtlPath,
    string FallbackDirectoryPath);

internal sealed record ArtifactPublication(
    ArtifactTransport Transport,
    string? OutputDirectory,
    string? ManifestPath,
    string? TracePath,
    string ArtifactPath,
    string? Warning);

internal interface IEmbeddedArtifactPublisher
{
    Task<EmbeddedArtifactReservation> ReserveAsync(
        string outputRoot,
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken);

    Task<ArtifactPublication> PublishAsync(
        EmbeddedArtifactReservation reservation,
        SessionMetadata session,
        string? fallbackReason,
        CancellationToken cancellationToken);
}

internal sealed class EmbeddedArtifactPublisher : IEmbeddedArtifactPublisher
{
    private readonly EtwSnap.Artifacts.NamedStreamStore _streams = new();
    private readonly EtwSnap.Artifacts.EmbeddedBundle _bundles = new();

    public async Task<EmbeddedArtifactReservation> ReserveAsync(
        string outputRoot,
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        EtwSnap.Artifacts.NamedStreamStore.RejectReparsePoint(root);
        await _streams.PreflightAsync(root, cancellationToken).ConfigureAwait(false);

        var name = SessionArtifactWriter.GetDirectoryName(sessionId, startedAtUtc);
        var stagingRoot = Path.Combine(root, ".etwsnap-staging");
        Directory.CreateDirectory(stagingRoot);
        EtwSnap.Artifacts.NamedStreamStore.RejectReparsePoint(stagingRoot);
        var stagingDirectory = Path.Combine(stagingRoot, name);
        var fallbackDirectory = Path.Combine(root, name);
        var finalEtl = Path.Combine(root, name + ".etl");
        if (Directory.Exists(stagingDirectory) || Directory.Exists(fallbackDirectory) ||
            File.Exists(stagingDirectory) || File.Exists(fallbackDirectory) || File.Exists(finalEtl))
        {
            throw new IOException($"The embedded session output already exists for {name}.");
        }

        Directory.CreateDirectory(stagingDirectory);
        var marker = Path.Combine(stagingDirectory, ".reserved");
        using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        var staging = new ArtifactReservation(
            stagingDirectory,
            Path.Combine(stagingDirectory, "frames"),
            Path.Combine(stagingDirectory, "manifest.json"),
            Path.Combine(stagingDirectory, "trace.etl"),
            marker,
            null,
            DeferFinalization: true);
        return new EmbeddedArtifactReservation(staging, finalEtl, fallbackDirectory);
    }

    public async Task<ArtifactPublication> PublishAsync(
        EmbeddedArtifactReservation reservation,
        SessionMetadata session,
        string? fallbackReason,
        CancellationToken cancellationToken)
    {
        var bundlePath = Path.Combine(reservation.Staging.DirectoryPath, ".bundle.zip.tmp");
        try
        {
            if (fallbackReason is not null)
            {
                throw new IOException(fallbackReason);
            }
            if (!File.Exists(reservation.Staging.TracePath))
            {
                throw new IOException("WPR did not produce a trace to receive embedded artifacts.");
            }

            await _bundles.CreateAsync(
                reservation.Staging.DirectoryPath,
                reservation.Staging.TracePath,
                session.SessionId,
                EtwSnap.Contracts.EtwSnapConstants.ProviderId,
                EtwSnap.Contracts.EtwSnapConstants.ManifestSchemaVersion,
                bundlePath,
                cancellationToken).ConfigureAwait(false);
            var streamName = EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(session.SessionId);
            await _streams.WriteFromFileAsync(
                reservation.Staging.TracePath,
                streamName,
                bundlePath,
                cancellationToken).ConfigureAwait(false);
            await VerifyAsync(reservation.Staging.TracePath, streamName, session.SessionId, cancellationToken).ConfigureAwait(false);

            File.Move(reservation.Staging.TracePath, reservation.FinalEtlPath, overwrite: false);
            await VerifyAsync(reservation.FinalEtlPath, streamName, session.SessionId, cancellationToken).ConfigureAwait(false);
            Directory.Delete(reservation.Staging.DirectoryPath, recursive: true);
            TryDeleteEmptyStagingRoot(Path.GetDirectoryName(reservation.Staging.DirectoryPath)!);
            return new ArtifactPublication(
                ArtifactTransport.Embedded,
                null,
                null,
                reservation.FinalEtlPath,
                reservation.FinalEtlPath,
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return await PublishFallbackAsync(reservation, bundlePath, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task VerifyAsync(
        string etlPath,
        string streamName,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var stream = _streams.OpenRead(etlPath, streamName);
        await _bundles.InspectAsync(stream, etlPath, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ArtifactPublication> PublishFallbackAsync(
        EmbeddedArtifactReservation reservation,
        string bundlePath,
        Exception cause,
        CancellationToken cancellationToken)
    {
        File.Delete(bundlePath);
        var traceSource = File.Exists(reservation.FinalEtlPath)
            ? reservation.FinalEtlPath
            : File.Exists(reservation.Staging.TracePath) ? reservation.Staging.TracePath : null;
        if (traceSource is not null)
        {
            var cleanTrace = Path.Combine(reservation.Staging.DirectoryPath, $".trace-{Guid.NewGuid():N}.tmp");
            await CopyPrimaryStreamAsync(traceSource, cleanTrace, cancellationToken).ConfigureAwait(false);
            File.Move(cleanTrace, reservation.Staging.TracePath, overwrite: true);
            if (File.Exists(reservation.FinalEtlPath))
            {
                File.Delete(reservation.FinalEtlPath);
            }
        }

        File.Delete(reservation.Staging.ReservationMarker);
        Directory.Move(reservation.Staging.DirectoryPath, reservation.FallbackDirectoryPath);
        TryDeleteEmptyStagingRoot(Path.GetDirectoryName(reservation.Staging.DirectoryPath)!);
        var tracePath = Path.Combine(reservation.FallbackDirectoryPath, Path.GetFileName(reservation.Staging.TracePath));
        var manifestPath = Path.Combine(reservation.FallbackDirectoryPath, Path.GetFileName(reservation.Staging.ManifestPath));
        var warning = $"Embedded artifact publication failed; saved a normal session folder instead. {cause.Message}";
        return new ArtifactPublication(
            ArtifactTransport.Folder,
            reservation.FallbackDirectoryPath,
            manifestPath,
            File.Exists(tracePath) ? tracePath : null,
            reservation.FallbackDirectoryPath,
            warning);
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

    private static void TryDeleteEmptyStagingRoot(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }
}