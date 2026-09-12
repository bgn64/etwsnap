using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;

namespace EtwSnap.WpaPlugin.Parsing;

public abstract record EtwSnapEvent(Timestamp Timestamp, Guid SessionId) : IKeyedDataType<Type>
{
    public string SourcePath { get; init; } = string.Empty;

    public Type GetKey() => GetType();
}

public sealed record RecordingStartedEvent(
    Timestamp Timestamp,
    Guid SessionId,
    ulong QpcFrequency,
    uint FramesPerSecond,
    ulong BufferBytes,
    uint TargetKind,
    ulong TargetHandle) : EtwSnapEvent(Timestamp, SessionId);

public sealed record FrameCapturedEvent(
    Timestamp Timestamp,
    Guid SessionId,
    ulong FrameNumber,
    long PresentationTime100ns,
    long CallbackQpc,
    uint Width,
    uint Height,
    uint PixelFormat) : EtwSnapEvent(Timestamp, SessionId);

public sealed record FrameCaptureErrorEvent(
    Timestamp Timestamp,
    Guid SessionId,
    ulong FrameNumber,
    int HResult) : EtwSnapEvent(Timestamp, SessionId);

public sealed record RecordingStoppedEvent(
    Timestamp Timestamp,
    Guid SessionId,
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong ErrorCount) : EtwSnapEvent(Timestamp, SessionId);

public sealed record ArtifactReferenceEvent(
    Timestamp Timestamp,
    Guid SessionId,
    uint ContractVersion,
    string ArtifactPath,
    string ArtifactFileName,
    uint ManifestSchemaVersion,
    uint BundleSchemaVersion,
    uint RequestedTransport) : EtwSnapEvent(Timestamp, SessionId);

public sealed record ArtifactCommittedEvent(
    Timestamp Timestamp,
    Guid SessionId,
    uint ContractVersion,
    string ArtifactPath,
    string ArtifactFileName,
    uint ManifestSchemaVersion,
    uint BundleSchemaVersion,
    uint RequestedTransport,
    uint ActualTransport,
    string ManifestSha256,
    string ArtifactSha256,
    string Status,
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong ErrorCount,
    ulong ExportedFrames,
    ulong FailedFrames) : EtwSnapEvent(Timestamp, SessionId);