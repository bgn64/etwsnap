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

public sealed record StopCaptureRequest(string OutputRoot);

public sealed record EmptyRequest;

public sealed record StartCaptureResult(Guid SessionId, DateTimeOffset StartedAtUtc, bool Tracing);

public sealed record StopCaptureResult(
    Guid SessionId,
    string OutputDirectory,
    string ManifestPath,
    string? TracePath,
    int ExportedFrames,
    long EvictedFrames);

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
