using System.Runtime.InteropServices;

namespace ETWSnap.Service.Recording;

/// <summary>
/// P/Invoke interop layer for the windows.graphics.capture.dll
/// </summary>
public static class ScreenCaptureInterop
{
    private const string DllName = "windows.graphics.capture.dll";

    /// <summary>
    /// Callback delegate for frame events
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FrameArrivedCallback(int width, int height, long timestamp, IntPtr userContext);

    /// <summary>
    /// Creates a capture manager for the specified window
    /// </summary>
    /// <param name="windowHandle">Handle to the window to capture</param>
    /// <param name="frameIntervalMs">Interval between frames in milliseconds</param>
    /// <returns>Handle to the capture manager, or IntPtr.Zero on failure</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Capture_Create(IntPtr windowHandle, int frameIntervalMs);

    /// <summary>
    /// Creates a capture manager for the specified monitor
    /// </summary>
    /// <param name="monitorHandle">Handle to the monitor to capture (HMONITOR)</param>
    /// <param name="frameIntervalMs">Interval between frames in milliseconds</param>
    /// <returns>Handle to the capture manager, or IntPtr.Zero on failure</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Capture_CreateForMonitor(IntPtr monitorHandle, int frameIntervalMs);

    /// <summary>
    /// Starts the capture
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    /// <returns>True if started successfully, false otherwise</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Capture_Start(IntPtr captureHandle);

    /// <summary>
    /// Stops the capture
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Capture_Stop(IntPtr captureHandle);

    /// <summary>
    /// Destroys the capture manager and frees resources
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Capture_Destroy(IntPtr captureHandle);

    /// <summary>
    /// Sets the callback function for frame events
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    /// <param name="callback">Callback function to invoke when frames arrive</param>
    /// <param name="userContext">User-defined context pointer</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Capture_SetFrameCallback(IntPtr captureHandle, FrameArrivedCallback callback, IntPtr userContext);

    /// <summary>
    /// Enables or disables cursor capture
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    /// <param name="enabled">True to capture cursor, false otherwise</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Capture_SetCursorEnabled(IntPtr captureHandle, bool enabled);

    /// <summary>
    /// Checks if cursor capture is enabled
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    /// <returns>True if cursor capture is enabled, false otherwise</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Capture_IsCursorEnabled(IntPtr captureHandle);

    // Windows API functions for window management
    
    /// <summary>
    /// Gets the handle to the foreground window
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Gets the handle to the desktop window
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Finds a window by class name and/or window name
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// Gets the text of a window's title bar
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    // Monitor enumeration
    public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
}
