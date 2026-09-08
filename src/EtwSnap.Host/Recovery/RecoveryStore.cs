using System.Text.Json;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Tracing;

namespace EtwSnap.Host.Recovery;

internal interface IRecoveryStore
{
    Task BeginAsync(Guid sessionId, DateTimeOffset startedAtUtc, StartCaptureRequest request, CancellationToken cancellationToken);
    Task MarkAsync(Guid sessionId, string status, string? outputDirectory, string? error, CancellationToken cancellationToken);
}

internal sealed class RecoveryStore : IRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EtwSnap",
        "Sessions");

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

            if (record is null || record.Status is not ("Starting" or "Capturing" or "Stopping" or "Persisting"))
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

            await WriteAsync(record with { Status = "HostTerminated", Error = error }, cancellationToken).ConfigureAwait(false);
        }
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
    string? Error);
