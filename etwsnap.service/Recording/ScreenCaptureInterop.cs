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
}
