using EtwSnap.Artifacts;
using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Artifacts;

internal sealed record EmbeddedArtifactReservation(
    ArtifactReservation Staging,
    string? FinalEtlPath,
    string FinalZipPath,
    string? Warning);

internal sealed record ArtifactPublication(
    ArtifactTransport Transport,
    string? ArtifactZipPath,
    string? TracePath,
    string ArtifactSha256,
    string? Warning);

internal interface IEmbeddedArtifactPublisher
{
    Task<EmbeddedArtifactReservation> ReserveAsync(
        string outputRoot,
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        ArtifactTransport transport,
        bool tracing,
        CancellationToken cancellationToken);

    Task<ArtifactPublication> PublishAsync(
        EmbeddedArtifactReservation reservation,
        SessionMetadata session,
        ArtifactTransport requestedTransport,
        string? traceFailure,
        CancellationToken cancellationToken);
}

internal sealed class EmbeddedArtifactPublisher : IEmbeddedArtifactPublisher
{
    private readonly NamedStreamStore _streams = new();
    private readonly EmbeddedBundle _bundles = new();

    public async Task<EmbeddedArtifactReservation> ReserveAsync(
        string outputRoot,
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        ArtifactTransport transport,
        bool tracing,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        NamedStreamStore.RejectReparsePoint(root);
        if (transport == ArtifactTransport.Embedded)
        {
            await _streams.PreflightAsync(root, cancellationToken).ConfigureAwait(false);
        }

        var name = SessionArtifactWriter.GetDirectoryName(sessionId, startedAtUtc);
        var stagingRoot = Path.Combine(root, ".etwsnap-staging");
        Directory.CreateDirectory(stagingRoot);
        NamedStreamStore.RejectReparsePoint(stagingRoot);
        var stagingDirectory = Path.Combine(stagingRoot, name);
        var finalEtl = tracing ? Path.Combine(root, name + ".etl") : null;
        var finalZip = Path.Combine(root, EmbeddedArtifactConstants.GetArtifactFileName(name));
        if (Directory.Exists(stagingDirectory) || File.Exists(stagingDirectory) ||
            File.Exists(finalZip) || finalEtl is not null && File.Exists(finalEtl))
        {
            throw new IOException($"The session output already exists for {name}.");
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
        return new EmbeddedArtifactReservation(staging, finalEtl, finalZip, null);
    }

    public async Task<ArtifactPublication> PublishAsync(
        EmbeddedArtifactReservation reservation,
        SessionMetadata session,
        ArtifactTransport requestedTransport,
        string? traceFailure,
        CancellationToken cancellationToken)
    {
        if (traceFailure is not null && File.Exists(reservation.Staging.TracePath))
        {
            File.Delete(reservation.Staging.TracePath);
        }
        var tracePath = File.Exists(reservation.Staging.TracePath) ? reservation.Staging.TracePath : null;
        var bundlePath = Path.Combine(reservation.Staging.DirectoryPath, ".artifact.etwsnap.zip.tmp");
        await _bundles.CreateAsync(
            reservation.Staging.DirectoryPath,
            tracePath,
            session.SessionId,
            EtwSnap.Contracts.EtwSnapConstants.ProviderId,
            EtwSnap.Contracts.EtwSnapConstants.ManifestSchemaVersion,
            bundlePath,
            cancellationToken).ConfigureAwait(false);
        await VerifyArchiveAsync(bundlePath, tracePath, session.SessionId, cancellationToken).ConfigureAwait(false);
        var artifactSha256 = await EmbeddedBundle.HashFileAsync(bundlePath, cancellationToken).ConfigureAwait(false);

        if (requestedTransport == ArtifactTransport.Embedded && tracePath is not null && traceFailure is null)
        {
            try
            {
                return await PublishEmbeddedAsync(
                    reservation,
                    bundlePath,
                    artifactSha256,
                    session.SessionId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                await SanitizeTraceAsync(reservation, cancellationToken).ConfigureAwait(false);
                var warning = $"Embedded artifact publication failed; saved a sidecar artifact ZIP instead. {exception.Message}";
                return await PublishSidecarAsync(
                    reservation,
                    bundlePath,
                    artifactSha256,
                    session.SessionId,
                    warning,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var fallbackWarning = traceFailure is null
            ? reservation.Warning
            : $"Trace stop failed; saved the screenshot artifact ZIP without an ETL. {traceFailure}";
        return await PublishSidecarAsync(
            reservation,
            bundlePath,
            artifactSha256,
            session.SessionId,
            fallbackWarning,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtifactPublication> PublishEmbeddedAsync(
        EmbeddedArtifactReservation reservation,
        string bundlePath,
        string artifactSha256,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (reservation.FinalEtlPath is null)
        {
            throw new InvalidOperationException("Embedded publication requires a traced session.");
        }
        var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
        await _streams.WriteFromFileAsync(reservation.Staging.TracePath, streamName, bundlePath, cancellationToken).ConfigureAwait(false);
        await VerifyStreamAsync(reservation.Staging.TracePath, streamName, sessionId, cancellationToken).ConfigureAwait(false);
        File.Move(reservation.Staging.TracePath, reservation.FinalEtlPath, overwrite: false);
        await VerifyStreamAsync(reservation.FinalEtlPath, streamName, sessionId, cancellationToken).ConfigureAwait(false);
        File.Delete(bundlePath);
        TryDeleteStagingDirectory(reservation.Staging.DirectoryPath);
        TryDeleteEmptyStagingRoot(Path.GetDirectoryName(reservation.Staging.DirectoryPath)!);
        return new ArtifactPublication(ArtifactTransport.Embedded, null, reservation.FinalEtlPath, artifactSha256, null);
    }

    private async Task<ArtifactPublication> PublishSidecarAsync(
        EmbeddedArtifactReservation reservation,
        string bundlePath,
        string artifactSha256,
        Guid sessionId,
        string? warning,
        CancellationToken cancellationToken)
    {
        string? finalEtl = null;
        if (File.Exists(reservation.Staging.TracePath) && reservation.FinalEtlPath is not null)
        {
            File.Move(reservation.Staging.TracePath, reservation.FinalEtlPath, overwrite: false);
            finalEtl = reservation.FinalEtlPath;
        }
        File.Move(bundlePath, reservation.FinalZipPath, overwrite: false);
        await VerifyArchiveAsync(reservation.FinalZipPath, finalEtl, sessionId, cancellationToken).ConfigureAwait(false);
        TryDeleteStagingDirectory(reservation.Staging.DirectoryPath);
        TryDeleteEmptyStagingRoot(Path.GetDirectoryName(reservation.Staging.DirectoryPath)!);
        return new ArtifactPublication(ArtifactTransport.Sidecar, reservation.FinalZipPath, finalEtl, artifactSha256, warning);
    }

    private async Task VerifyStreamAsync(
        string etlPath,
        string streamName,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var stream = _streams.OpenRead(etlPath, streamName);
        await _bundles.InspectAsync(stream, etlPath, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyArchiveAsync(
        string archivePath,
        string? etlPath,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(archivePath);
        await _bundles.InspectAsync(stream, etlPath, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SanitizeTraceAsync(
        EmbeddedArtifactReservation reservation,
        CancellationToken cancellationToken)
    {
        var sourcePath = reservation.FinalEtlPath is not null && File.Exists(reservation.FinalEtlPath)
            ? reservation.FinalEtlPath
            : reservation.Staging.TracePath;
        if (!File.Exists(sourcePath))
        {
            return;
        }
        var cleanTrace = Path.Combine(reservation.Staging.DirectoryPath, $".trace-{Guid.NewGuid():N}.tmp");
        await CopyPrimaryStreamAsync(sourcePath, cleanTrace, cancellationToken).ConfigureAwait(false);
        File.Move(cleanTrace, reservation.Staging.TracePath, overwrite: true);
        if (reservation.FinalEtlPath is not null && File.Exists(reservation.FinalEtlPath))
        {
            File.Delete(reservation.FinalEtlPath);
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

    private static void TryDeleteStagingDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
