using System.Runtime.InteropServices;

namespace ETWSnap.Service.Recording;

/// <summary>
/// P/Invoke interop layer for the windows.graphics.capture.dll
/// </summary>
public static class ScreenCaptureInterop
{
    private const string DllName = "windows.graphics.capture.dll";

    /// <summary>
    /// Structure representing a captured frame
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameData
    {
        public IntPtr PixelData;     // Pointer to BGRA8 pixel data
        public int Width;            // Width in pixels
        public int Height;           // Height in pixels
        public int RowPitch;         // Bytes per row
        public long Timestamp;       // Milliseconds since epoch
        public int FrameNumber;      // Sequential frame number
    }

    /// <summary>
    /// Creates a capture manager for the specified window
    /// </summary>
    /// <param name="windowHandle">Handle to the window to capture</param>
    /// <param name="frameIntervalMs">Interval between frames in milliseconds</param>
    /// <param name="maxFrames">Maximum frames to store in circular buffer</param>
    /// <returns>Handle to the capture manager, or IntPtr.Zero on failure</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Capture_Create(IntPtr windowHandle, int frameIntervalMs, int maxFrames);

    /// <summary>
    /// Creates a capture manager for the specified monitor
    /// </summary>
    /// <param name="monitorHandle">Handle to the monitor to capture (HMONITOR)</param>
    /// <param name="frameIntervalMs">Interval between frames in milliseconds</param>
    /// <param name="maxFrames">Maximum frames to store in circular buffer</param>
    /// <returns>Handle to the capture manager, or IntPtr.Zero on failure</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Capture_CreateForMonitor(IntPtr monitorHandle, int frameIntervalMs, int maxFrames);

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
    /// Gets all captured frames from the buffer
    /// </summary>
    /// <param name="captureHandle">Handle to the capture manager</param>
    /// <param name="outFrames">Pointer to receive array of frames</param>
    /// <param name="outCount">Pointer to receive number of frames</param>
    /// <returns>True on success, false on failure</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Capture_GetFrames(IntPtr captureHandle, out IntPtr outFrames, out int outCount);

    /// <summary>
    /// Frees frame data allocated by Capture_GetFrames
    /// </summary>
    /// <param name="frames">Array of frames to free</param>
    /// <param name="count">Number of frames in array</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Capture_FreeFrames(IntPtr frames, int count);

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
