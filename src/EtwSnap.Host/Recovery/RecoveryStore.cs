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
                                    record.Artifacts?.RequestedTransport == ArtifactTransport.Embedded &&
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
        if (artifacts?.RequestedTransport != ArtifactTransport.Embedded)
        {
            return record with { Status = "HostTerminated", Error = cleanupError };
        }

        string? recoveryError = null;
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
                if (string.IsNullOrWhiteSpace(artifacts.FinalEtlPath))
                {
                    throw new IOException("The recovery record does not contain a final ETL path.");
                }
                var fallbackDirectory = Path.ChangeExtension(artifacts.FinalEtlPath, null)
                    ?? throw new IOException("The recovery fallback path is invalid.");
                if (Directory.Exists(fallbackDirectory) || File.Exists(fallbackDirectory))
                {
                    throw new IOException($"The recovery fallback destination already exists: {fallbackDirectory}");
                }

                var tracePath = Path.Combine(artifacts.StagingDirectory, "trace.etl");
                await SanitizePrimaryStreamAsync(tracePath, cancellationToken).ConfigureAwait(false);
                File.Delete(Path.Combine(artifacts.StagingDirectory, ".bundle.zip.tmp"));
                File.Delete(Path.Combine(artifacts.StagingDirectory, ".reserved"));
                Directory.Move(artifacts.StagingDirectory, fallbackDirectory);
                var warning = CombineErrors(
                    cleanupError,
                    recoveryError,
                    "Recovered abandoned embedded staging as a normal session folder.");
                return record with
                {
                    Status = "Partial",
                    OutputDirectory = fallbackDirectory,
                    Error = warning,
                    Artifacts = artifacts with
                    {
                        ActualTransport = ArtifactTransport.Folder,
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
