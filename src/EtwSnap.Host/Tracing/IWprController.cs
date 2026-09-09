namespace EtwSnap.Host.Tracing;

internal interface IWprController
{
    Task<WprSession> StartAsync(Guid sessionId, string? userProfileSelector, CancellationToken cancellationToken);
    Task StopAsync(WprSession session, string outputPath, CancellationToken cancellationToken);
    Task CancelAsync(WprSession session, CancellationToken cancellationToken);
    Task CancelInstanceAsync(string instanceName, CancellationToken cancellationToken);
}

internal sealed record WprSession(
    string InstanceName,
    byte[] StagedSupplementalProfileBytes,
    string SupplementalProfileHash,
    string? UserProfilePath,
    string? UserProfileSelector,
    string? UserProfileHash,
    string StagingDirectory);

internal sealed class WprException(string message) : Exception(message);
