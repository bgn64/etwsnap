using System.Text.Json;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Tracing;

namespace EtwSnap.Host.Recovery;

internal interface IRecoveryStore
{
    Task BeginAsync(Guid sessionId, DateTimeOffset startedAtUtc, StartCaptureRequest request, CancellationToken cancellationToken);
    Task MarkAsync(Guid sessionId, string status, string? outputDirectory, string? error, CancellationToken cancellationToken);
    Task MarkArtifactsAsync(Guid sessionId, RecoveryArtifactState artifacts, CancellationToken cancellationToken);
}

internal sealed record RecoveryArtifactState(
    ArtifactTransport RequestedTransport,
    ArtifactTransport? ActualTransport,
    string? StagingDirectory,
    string? FinalEtlPath,
    string? FinalZipPath,
    string? StreamName);

internal sealed class RecoveryStore : IRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;

    public RecoveryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtwSnap",
            "Sessions"))
    {
    }

    internal RecoveryStore(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public Task BeginAsync(Guid sessionId, DateTimeOffset startedAtUtc, StartCaptureRequest request, CancellationToken cancellationToken) =>
        WriteAsync(new RecoveryRecord(
            sessionId,
            "Starting",
            startedAtUtc,
            request.Trace ? $"EtwSnap_{sessionId:N}" : null,
            null,
            null), cancellationToken);

    public async Task MarkAsync(
        Guid sessionId,
        string status,
        string? outputDirectory,
        string? error,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        await WriteAsync(existing with { Status = status, OutputDirectory = outputDirectory, Error = error }, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkArtifactsAsync(
        Guid sessionId,
        RecoveryArtifactState artifacts,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await WriteAsync(existing with { Artifacts = artifacts }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RecoverAbandonedAsync(IWprController wpr, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "session.json", SearchOption.AllDirectories))
        {
            RecoveryRecord? record;
            try
            {
                await using var stream = File.OpenRead(path);
                record = await JsonSerializer.DeserializeAsync<RecoveryRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

                        if (record is null ||
                                record.Status is not ("Starting" or "Capturing" or "Stopping" or "Persisting") &&
                                !(record.Status == "Failed" &&
                                    record.Artifacts is not null &&
                                    Directory.Exists(record.Artifacts.StagingDirectory)))
            {
                continue;
            }

            string? error = null;
            if (record.WprInstance is not null)
            {
                try
                {
                    await wpr.CancelInstanceAsync(record.WprInstance, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
            }

            var recovered = await RecoverArtifactsAsync(record, error, cancellationToken).ConfigureAwait(false);
            await WriteAsync(recovered, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<RecoveryRecord> RecoverArtifactsAsync(
        RecoveryRecord record,
        string? cleanupError,
        CancellationToken cancellationToken)
    {
        var artifacts = record.Artifacts;
        if (artifacts is null)
        {
            return record with { Status = "HostTerminated", Error = cleanupError };
        }

        string? recoveryError = null;
        if (!string.IsNullOrWhiteSpace(artifacts.FinalZipPath) && File.Exists(artifacts.FinalZipPath))
        {
            try
            {
                var finalEtl = !string.IsNullOrWhiteSpace(artifacts.FinalEtlPath) && File.Exists(artifacts.FinalEtlPath)
                    ? artifacts.FinalEtlPath
                    : null;
                await using var archive = File.OpenRead(artifacts.FinalZipPath);
                await new EtwSnap.Artifacts.EmbeddedBundle().InspectAsync(
                    archive,
                    finalEtl,
                    record.SessionId,
                    cancellationToken).ConfigureAwait(false);
                return record with
                {
                    Status = cleanupError is null ? "Complete" : "Partial",
                    OutputDirectory = artifacts.FinalZipPath,
                    Error = cleanupError,
                    Artifacts = artifacts with { ActualTransport = ArtifactTransport.Sidecar },
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException or JsonException)
            {
                recoveryError = $"The published sidecar artifact ZIP could not be verified and was left unchanged: {exception.Message}";
            }
        }

        if (!string.IsNullOrWhiteSpace(artifacts.FinalEtlPath) &&
            !string.IsNullOrWhiteSpace(artifacts.StreamName) &&
            File.Exists(artifacts.FinalEtlPath))
        {
            try
            {
                var streams = new EtwSnap.Artifacts.NamedStreamStore();
                await using var stream = streams.OpenRead(artifacts.FinalEtlPath, artifacts.StreamName);
                await new EtwSnap.Artifacts.EmbeddedBundle().InspectAsync(
                    stream,
                    artifacts.FinalEtlPath,
                    record.SessionId,
                    cancellationToken).ConfigureAwait(false);
                return record with
                {
                    Status = cleanupError is null ? "Complete" : "Partial",
                    OutputDirectory = artifacts.FinalEtlPath,
                    Error = cleanupError,
                    Artifacts = artifacts with { ActualTransport = ArtifactTransport.Embedded },
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException or JsonException)
            {
                recoveryError = $"The published embedded ETL could not be verified and was left unchanged: {exception.Message}";
            }
        }

        if (!string.IsNullOrWhiteSpace(artifacts.StagingDirectory) && Directory.Exists(artifacts.StagingDirectory))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(artifacts.FinalZipPath))
                {
                    throw new IOException("The recovery record does not contain a final artifact ZIP path.");
                }
                if (File.Exists(artifacts.FinalZipPath))
                {
                    throw new IOException($"The recovery artifact ZIP already exists: {artifacts.FinalZipPath}");
                }

                var tracePath = Path.Combine(artifacts.StagingDirectory, "trace.etl");
                var existingTracePath = File.Exists(tracePath)
                    ? tracePath
                    : !string.IsNullOrWhiteSpace(artifacts.FinalEtlPath) && File.Exists(artifacts.FinalEtlPath)
                        ? artifacts.FinalEtlPath
                        : null;
                if (existingTracePath is not null)
                {
                    await SanitizePrimaryStreamAsync(existingTracePath, cancellationToken).ConfigureAwait(false);
                }
                var recoveryZip = Path.Combine(artifacts.StagingDirectory, ".recovery.etwsnap.zip.tmp");
                var traceForBundle = existingTracePath;
                await new EtwSnap.Artifacts.EmbeddedBundle().CreateAsync(
                    artifacts.StagingDirectory,
                    traceForBundle,
                    record.SessionId,
                    EtwSnap.Contracts.EtwSnapConstants.ProviderId,
                    EtwSnap.Contracts.EtwSnapConstants.ManifestSchemaVersion,
                    recoveryZip,
                    cancellationToken).ConfigureAwait(false);
                string? finalEtl = !string.IsNullOrWhiteSpace(artifacts.FinalEtlPath) && File.Exists(artifacts.FinalEtlPath)
                    ? artifacts.FinalEtlPath
                    : null;
                if (File.Exists(tracePath) && !string.IsNullOrWhiteSpace(artifacts.FinalEtlPath) && finalEtl is null)
                {
                    File.Move(tracePath, artifacts.FinalEtlPath, overwrite: false);
                    finalEtl = artifacts.FinalEtlPath;
                }
                File.Move(recoveryZip, artifacts.FinalZipPath, overwrite: false);
                await using (var archive = File.OpenRead(artifacts.FinalZipPath))
                {
                    await new EtwSnap.Artifacts.EmbeddedBundle().InspectAsync(
                        archive,
                        finalEtl,
                        record.SessionId,
                        cancellationToken).ConfigureAwait(false);
                }
                Directory.Delete(artifacts.StagingDirectory, recursive: true);
                var warning = CombineErrors(
                    cleanupError,
                    recoveryError,
                    "Recovered abandoned staging as a sidecar artifact ZIP.");
                return record with
                {
                    Status = "Partial",
                    OutputDirectory = artifacts.FinalZipPath,
                    Error = warning,
                    Artifacts = artifacts with
                    {
                        ActualTransport = ArtifactTransport.Sidecar,
                        StagingDirectory = null,
                    },
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                recoveryError = CombineErrors(
                    recoveryError,
                    $"Embedded staging was preserved for manual recovery: {exception.Message}");
            }
        }

        return record with
        {
            Status = "HostTerminated",
            Error = CombineErrors(cleanupError, recoveryError),
        };
    }

    private static async Task SanitizePrimaryStreamAsync(string tracePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(tracePath))
        {
            return;
        }
        var cleanTrace = Path.Combine(Path.GetDirectoryName(tracePath)!, $".trace-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var source = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(cleanTrace, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }
            File.Move(cleanTrace, tracePath, overwrite: true);
        }
        finally
        {
            File.Delete(cleanTrace);
        }
    }

    private static string? CombineErrors(params string?[] errors)
    {
        var messages = errors.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
    }

    private async Task<RecoveryRecord?> ReadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RecoveryRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(RecoveryRecord record, CancellationToken cancellationToken)
    {
        var path = GetPath(record.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetPath(Guid sessionId) => Path.Combine(_root, sessionId.ToString("N"), "session.json");
}

internal sealed record RecoveryRecord(
    Guid SessionId,
    string Status,
    DateTimeOffset StartedAtUtc,
    string? WprInstance,
    string? OutputDirectory,
    string? Error,
    RecoveryArtifactState? Artifacts = null);
