using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EtwSnap.Host.Capture;

internal static class NativeMethods
{
    internal const uint ApiVersion = 1;
    private const string LibraryName = "EtwSnap.Native.dll";

    internal enum Result : int
    {
        Success = 0,
        InvalidArgument = 1,
        InvalidState = 2,
        NotSupported = 3,
        BufferTooSmall = 4,
        Failure = 5,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct CreateOptions
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal uint TargetKind;
        internal uint CaptureCursor;
        internal ulong TargetHandle;
        internal uint FramesPerSecond;
        internal uint Reserved;
        internal ulong BufferBytes;
        internal Guid SessionId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FrameInfo
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong FrameNumber;
        internal long PresentationTime100ns;
        internal long CallbackQpc;
        internal uint Width;
        internal uint Height;
        internal uint PixelFormat;
        internal uint Reserved;
        internal ulong RequiredBytes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct Stats
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong ObservedFrames;
        internal ulong AcceptedFrames;
        internal ulong RetainedFrames;
        internal ulong EvictedFrames;
        internal ulong DroppedFrames;
        internal ulong ErrorCount;
    }

    internal sealed class CaptureSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private CaptureSafeHandle() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            EtwSnap_Destroy(handle);
            return true;
        }
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint EtwSnap_GetApiVersion();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_Initialize();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void EtwSnap_Shutdown();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_Create(in CreateOptions options, out CaptureSafeHandle session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_Start(CaptureSafeHandle session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_Stop(CaptureSafeHandle session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void EtwSnap_Destroy(IntPtr session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_GetStats(CaptureSafeHandle session, ref Stats stats);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_GetFrameCount(CaptureSafeHandle session, out ulong frameCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_GetFrameInfo(CaptureSafeHandle session, ulong index, ref FrameInfo info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern Result EtwSnap_CopyFrameBgra(
        CaptureSafeHandle session,
        ulong index,
        IntPtr destination,
        ulong destinationBytes,
        uint destinationStride);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern Result EtwSnap_GetLastError(IntPtr destination, uint capacity, out uint requiredLength);
}
