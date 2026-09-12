using System.Text.Json.Serialization;

namespace EtwSnap.Contracts.Models;

public enum CaptureTargetKind
{
    PrimaryMonitor,
    Monitor,
    Window,
}

public sealed record CaptureTarget(CaptureTargetKind Kind, long Handle = 0);

public sealed record StartCaptureRequest(
    bool Trace,
    string? Profile,
    CaptureTarget Target,
    int FramesPerSecond,
    long BufferMegabytes,
    bool CaptureCursor);

public enum ArtifactTransport
{
    Sidecar,
    Embedded,
}

public sealed record StopCaptureRequest
{
    [JsonConstructor]
    public StopCaptureRequest(string outputRoot, ArtifactTransport artifactTransport)
    {
        OutputRoot = outputRoot;
        ArtifactTransport = artifactTransport;
    }

    public StopCaptureRequest(string outputRoot) : this(outputRoot, ArtifactTransport.Sidecar)
    {
    }

    public string OutputRoot { get; init; }
    public ArtifactTransport ArtifactTransport { get; init; }

    public void Deconstruct(out string outputRoot) => outputRoot = OutputRoot;

    public void Deconstruct(out string outputRoot, out ArtifactTransport artifactTransport)
    {
        outputRoot = OutputRoot;
        artifactTransport = ArtifactTransport;
    }
}

public sealed record EmptyRequest;

public sealed record StartCaptureResult(Guid SessionId, DateTimeOffset StartedAtUtc, bool Tracing);

public sealed record StopCaptureResult
{
    [JsonConstructor]
    public StopCaptureResult(
        Guid sessionId,
        string? artifactZipPath,
        string? tracePath,
        int exportedFrames,
        long evictedFrames,
        ArtifactTransport requestedArtifactTransport,
        ArtifactTransport actualArtifactTransport,
        string artifactSha256,
        string? artifactWarning)
    {
        SessionId = sessionId;
        ArtifactZipPath = artifactZipPath;
        TracePath = tracePath;
        ExportedFrames = exportedFrames;
        EvictedFrames = evictedFrames;
        RequestedArtifactTransport = requestedArtifactTransport;
        ActualArtifactTransport = actualArtifactTransport;
        ArtifactSha256 = artifactSha256;
        ArtifactWarning = artifactWarning;
    }

    public Guid SessionId { get; init; }
    public string? ArtifactZipPath { get; init; }
    public string? TracePath { get; init; }
    public int ExportedFrames { get; init; }
    public long EvictedFrames { get; init; }
    public ArtifactTransport RequestedArtifactTransport { get; init; }
    public ArtifactTransport ActualArtifactTransport { get; init; }
    public string ArtifactSha256 { get; init; }
    public string? ArtifactWarning { get; init; }
}

public sealed record CancelCaptureResult(Guid SessionId);

public enum CaptureSessionState
{
    Idle,
    Starting,
    Capturing,
    StoppingCapture,
    StoppingTrace,
    Persisting,
    Cancelling,
    Faulted,
}

public sealed record SessionStatus(
    CaptureSessionState State,
    Guid? SessionId,
    DateTimeOffset? StartedAtUtc,
    bool Tracing,
    long AcceptedFrames,
    long RetainedFrames,
    long EvictedFrames,
    string? LastError);

public sealed record TargetDescriptor(
    int Index,
    CaptureTargetKind Kind,
    long Handle,
    string Name,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record TargetList(IReadOnlyList<TargetDescriptor> Targets);

public sealed record PingResult(int ProtocolVersion, string HostVersion);
