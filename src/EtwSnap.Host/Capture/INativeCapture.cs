using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Capture;

internal interface INativeCaptureFactory : IDisposable
{
    INativeCaptureSession Create(Guid sessionId, StartCaptureRequest request);
}

internal interface INativeCaptureSession : IDisposable
{
    void Start();
    void Stop();
    NativeCaptureStats GetStats();
    ulong GetFrameCount();
    NativeFrameInfo GetFrameInfo(ulong index);
    void CopyFrameBgra(ulong index, byte[] destination, uint stride);
}

internal sealed record NativeCaptureStats(
    ulong ObservedFrames,
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong ErrorCount);

internal sealed record NativeFrameInfo(
    ulong FrameNumber,
    long PresentationTime100ns,
    long CallbackQpc,
    uint Width,
    uint Height,
    uint PixelFormat,
    ulong RequiredBytes);
