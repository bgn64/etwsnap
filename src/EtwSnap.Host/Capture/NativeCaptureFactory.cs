using System.Runtime.InteropServices;
using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Capture;

internal sealed class NativeCaptureFactory : INativeCaptureFactory
{
    private static readonly object InitializationGate = new();
    private static int _referenceCount;
    private bool _disposed;

    public NativeCaptureFactory()
    {
        lock (InitializationGate)
        {
            if (_referenceCount == 0)
            {
                var version = NativeMethods.EtwSnap_GetApiVersion();
                if (version != NativeMethods.ApiVersion)
                {
                    throw new InvalidOperationException($"Native capture API {version} does not match host API {NativeMethods.ApiVersion}.");
                }
                NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_Initialize(), "initialize native capture");
            }
            ++_referenceCount;
        }
    }

    public INativeCaptureSession Create(Guid sessionId, StartCaptureRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var options = new NativeMethods.CreateOptions
        {
            StructSize = checked((uint)Marshal.SizeOf<NativeMethods.CreateOptions>()),
            ApiVersion = NativeMethods.ApiVersion,
            TargetKind = (uint)request.Target.Kind,
            CaptureCursor = request.CaptureCursor ? 1u : 0u,
            TargetHandle = unchecked((ulong)request.Target.Handle),
            FramesPerSecond = checked((uint)request.FramesPerSecond),
            BufferBytes = checked((ulong)request.BufferMegabytes * 1024UL * 1024UL),
            SessionId = sessionId,
        };

        NativeCall.ThrowIfFailed(NativeMethods.EtwSnap_Create(in options, out var handle), "create capture session");
        return new NativeCaptureSession(handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (InitializationGate)
        {
            _disposed = true;
            if (--_referenceCount == 0)
            {
                NativeMethods.EtwSnap_Shutdown();
            }
        }
    }
}

internal static class NativeCall
{
    public static void ThrowIfFailed(NativeMethods.Result result, string operation)
    {
        if (result == NativeMethods.Result.Success)
        {
            return;
        }

        throw new NativeCaptureException(result, $"Failed to {operation}: {ReadError()}" );
    }

    private static string ReadError()
    {
        _ = NativeMethods.EtwSnap_GetLastError(IntPtr.Zero, 0, out var requiredLength);
        if (requiredLength <= 1)
        {
            return "No native diagnostic was provided.";
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredLength * sizeof(char)));
        try
        {
            var result = NativeMethods.EtwSnap_GetLastError(buffer, requiredLength, out _);
            return result == NativeMethods.Result.Success
                ? Marshal.PtrToStringUni(buffer) ?? "No native diagnostic was provided."
                : "The native diagnostic could not be read.";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

internal sealed class NativeCaptureException(NativeMethods.Result result, string message) : Exception(message)
{
    public NativeMethods.Result Result { get; } = result;
}
