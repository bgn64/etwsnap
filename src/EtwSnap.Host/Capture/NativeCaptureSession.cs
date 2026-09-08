using System.Runtime.InteropServices;

namespace EtwSnap.Host.Capture;

internal sealed class NativeCaptureSession(NativeMethods.CaptureSafeHandle handle) : INativeCaptureSession
{
    private bool _disposed;

    public void Start()
    {
        ThrowIfDisposed();
        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_Start(handle), "start capture");
    }

    public void Stop()
    {
        ThrowIfDisposed();
        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_Stop(handle), "stop capture");
    }

    public NativeCaptureStats GetStats()
    {
        ThrowIfDisposed();
        var stats = new NativeMethods.Stats
        {
            StructSize = checked((uint)Marshal.SizeOf<NativeMethods.Stats>()),
            ApiVersion = NativeMethods.ApiVersion,
        };
        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_GetStats(handle, ref stats), "read capture statistics");
        return new NativeCaptureStats(
            stats.ObservedFrames,
            stats.AcceptedFrames,
            stats.RetainedFrames,
            stats.EvictedFrames,
            stats.DroppedFrames,
            stats.ErrorCount);
    }

    public ulong GetFrameCount()
    {
        ThrowIfDisposed();
        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_GetFrameCount(handle, out var count), "read retained frame count");
        return count;
    }

    public NativeFrameInfo GetFrameInfo(ulong index)
    {
        ThrowIfDisposed();
        var info = new NativeMethods.FrameInfo
        {
            StructSize = checked((uint)Marshal.SizeOf<NativeMethods.FrameInfo>()),
            ApiVersion = NativeMethods.ApiVersion,
        };
        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_GetFrameInfo(handle, index, ref info), "read frame metadata");
        return new NativeFrameInfo(
            info.FrameNumber,
            info.PresentationTime100ns,
            info.CallbackQpc,
            info.Width,
            info.Height,
            info.PixelFormat,
            info.RequiredBytes);
    }

    public void CopyFrameBgra(ulong index, byte[] destination, uint stride)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);

        var pin = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            NativeCall.ThrowIfFailed(
                NativeMethods.EtwSnap_CopyFrameBgra(
                    handle,
                    index,
                    pin.AddrOfPinnedObject(),
                    checked((ulong)destination.LongLength),
                    stride),
                "copy frame pixels");
        }
        finally
        {
            pin.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        handle.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
