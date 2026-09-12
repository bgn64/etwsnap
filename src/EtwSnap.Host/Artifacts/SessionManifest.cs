using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Artifacts;

internal sealed record SessionManifest(
    int SchemaVersion,
    Guid SessionId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset StoppedAtUtc,
    long QpcFrequency,
    StartCaptureRequest Capture,
    ProviderManifest Provider,
    TraceManifest? Trace,
    StatisticsManifest Statistics,
    IReadOnlyList<FrameManifest> Frames,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

internal sealed record ProviderManifest(string Name, Guid Id);

internal sealed record TraceManifest(
    string InstanceName,
    string SupplementalProfilePath,
    string SupplementalProfileHash,
    string? UserProfilePath,
    string? UserProfileSelector,
    string? UserProfileHash);

internal sealed record StatisticsManifest(
    ulong ObservedFrames,
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong NativeErrors,
    int ExportedFrames,
    int FailedFrames);

internal sealed record FrameManifest(
    ulong FrameNumber,
    long PresentationTime100ns,
    long CallbackQpc,
    uint Width,
    uint Height,
    uint PixelFormat,
    string Path);
