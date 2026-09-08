using System.Runtime.InteropServices;
using System.Text;
using EtwSnap.Contracts.Models;

namespace EtwSnap.Host.Targets;

internal interface ITargetValidator
{
    bool IsValid(CaptureTarget target);
}

internal sealed class TargetEnumerator : ITargetValidator
{
    public IReadOnlyList<TargetDescriptor> EnumerateWindows()
    {
        var targets = new List<TargetDescriptor>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(handle);
            if (titleLength <= 0 || !NativeMethods.GetWindowRect(handle, out var bounds))
            {
                return true;
            }

            var title = new StringBuilder(titleLength + 1);
            _ = NativeMethods.GetWindowText(handle, title, title.Capacity);
            targets.Add(new TargetDescriptor(
                targets.Count + 1,
                CaptureTargetKind.Window,
                handle.ToInt64(),
                title.ToString(),
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top,
                false));
            return true;
        }, IntPtr.Zero);

        return targets;
    }

    public IReadOnlyList<TargetDescriptor> EnumerateMonitors()
    {
        var targets = new List<TargetDescriptor>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfo(handle, ref info))
            {
                return true;
            }

            targets.Add(new TargetDescriptor(
                targets.Count + 1,
                CaptureTargetKind.Monitor,
                handle.ToInt64(),
                info.DeviceName,
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);

        return targets;
    }

    public bool IsValid(CaptureTarget target) => target.Kind switch
    {
        CaptureTargetKind.PrimaryMonitor => EnumerateMonitors().Any(monitor => monitor.IsPrimary),
        CaptureTargetKind.Monitor => EnumerateMonitors().Any(monitor => monitor.Handle == target.Handle),
        CaptureTargetKind.Window => NativeMethods.IsWindow(new IntPtr(target.Handle)),
        _ => false,
    };

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr bounds, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
        internal static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr parameter);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfoEx
        {
            internal int Size;
            internal Rect Monitor;
            internal Rect Work;
            internal uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string DeviceName;
        }
    }
}
