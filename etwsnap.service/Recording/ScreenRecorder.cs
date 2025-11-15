using System.Runtime.InteropServices;

namespace ETWSnap.Service.Recording;

/// <summary>
/// Screen recorder implementation using Windows.Graphics.Capture
/// </summary>
public class ScreenRecorder : IScreenRecorder
{
    private readonly object _lockObj = new();
    private IntPtr _captureHandle = IntPtr.Zero;
    private RecordingOptions _options;
    private bool _isRecording = false;
    private DateTime? _startTime;
    private string? _sessionId;

    public ScreenRecorder()
    {
        _options = new RecordingOptions();
    }

    public bool IsRecording
    {
        get
        {
            lock (_lockObj)
            {
                return _isRecording;
            }
        }
    }

    public RecordingOptions Options
    {
        get
        {
            lock (_lockObj)
            {
                return _options;
            }
        }
    }

    public bool Start(RecordingOptions? options = null)
    {
        lock (_lockObj)
        {
            if (_isRecording)
            {
                Console.WriteLine("[ScreenRecorder] Already recording");
                return false;
            }

            // Update options if provided
            if (options != null)
            {
                _options = options;
            }

            // Calculate max frames based on buffer size and estimated frame size
            // Assume average of 1920x1080 at 4 bytes per pixel (~8MB per frame)
            int estimatedFrameSize = 1920 * 1080 * 4;
            int maxFrames = Math.Max(10, (int)(_options.MaxBufferSizeBytes / estimatedFrameSize));

            // Determine what to capture: monitor takes precedence over window
            if (_options.MonitorHandle != IntPtr.Zero)
            {
                // Capture specified monitor
                Console.WriteLine($"[ScreenRecorder] Capturing monitor handle: 0x{_options.MonitorHandle:X}");
                _captureHandle = ScreenCaptureInterop.Capture_CreateForMonitor(_options.MonitorHandle, _options.FrameIntervalMs, maxFrames);
            }
            else if (_options.WindowHandle != IntPtr.Zero)
            {
                // Capture specified window
                Console.WriteLine($"[ScreenRecorder] Capturing window handle: 0x{_options.WindowHandle:X}");
                _captureHandle = ScreenCaptureInterop.Capture_Create(_options.WindowHandle, _options.FrameIntervalMs, maxFrames);
            }
            else
            {
                // Capture primary monitor by default
                var primaryMonitor = GetPrimaryMonitor();
                Console.WriteLine($"[ScreenRecorder] Capturing primary monitor: 0x{primaryMonitor:X}");
                _captureHandle = ScreenCaptureInterop.Capture_CreateForMonitor(primaryMonitor, _options.FrameIntervalMs, maxFrames);
            }

            if (_captureHandle == IntPtr.Zero)
            {
                Console.WriteLine("[ScreenRecorder] Failed to create capture manager");
                return false;
            }

            Console.WriteLine($"[ScreenRecorder] Capture created successfully (FPS: {_options.FramesPerSecond}, Buffer: {maxFrames} frames)");

            // Configure cursor capture
            ScreenCaptureInterop.Capture_SetCursorEnabled(_captureHandle, _options.CaptureCursor);
            Console.WriteLine($"[ScreenRecorder] Cursor capture: {_options.CaptureCursor}");

            // Start capturing
            if (!ScreenCaptureInterop.Capture_Start(_captureHandle))
            {
                Console.WriteLine("[ScreenRecorder] Failed to start capture");
                ScreenCaptureInterop.Capture_Destroy(_captureHandle);
                _captureHandle = IntPtr.Zero;
                return false;
            }

            _isRecording = true;
            _startTime = DateTime.UtcNow;
            _sessionId = Guid.NewGuid().ToString();
            
            Console.WriteLine("[ScreenRecorder] Recording started successfully");
            return true;
        }
    }

    public List<FrameData> Stop()
    {
        lock (_lockObj)
        {
            var frames = new List<FrameData>();

            if (!_isRecording)
            {
                return frames;
            }

            Console.WriteLine("[ScreenRecorder] Stopping recording...");

            if (_captureHandle != IntPtr.Zero)
            {
                ScreenCaptureInterop.Capture_Stop(_captureHandle);

                // Retrieve frames from native buffer
                if (ScreenCaptureInterop.Capture_GetFrames(_captureHandle, out IntPtr framesPtr, out int frameCount))
                {
                    Console.WriteLine($"[ScreenRecorder] Retrieved {frameCount} frames from native buffer");

                    if (frameCount > 0 && framesPtr != IntPtr.Zero)
                    {
                        int structSize = Marshal.SizeOf<ScreenCaptureInterop.FrameData>();

                        for (int i = 0; i < frameCount; i++)
                        {
                            IntPtr framePtr = IntPtr.Add(framesPtr, i * structSize);
                            var nativeFrame = Marshal.PtrToStructure<ScreenCaptureInterop.FrameData>(framePtr);

                            // Copy pixel data from unmanaged to managed memory
                            int dataSize = nativeFrame.Height * nativeFrame.RowPitch;
                            byte[] pixelData = new byte[dataSize];
                            Marshal.Copy(nativeFrame.PixelData, pixelData, 0, dataSize);

                            frames.Add(new FrameData
                            {
                                Width = nativeFrame.Width,
                                Height = nativeFrame.Height,
                                Timestamp = nativeFrame.Timestamp,
                                FrameNumber = nativeFrame.FrameNumber,
                                PixelData = pixelData
                            });
                        }

                        // Free native memory
                        ScreenCaptureInterop.Capture_FreeFrames(framesPtr, frameCount);
                    }
                }

                ScreenCaptureInterop.Capture_Destroy(_captureHandle);
                _captureHandle = IntPtr.Zero;
            }

            _isRecording = false;

            var duration = _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : TimeSpan.Zero;
            
            Console.WriteLine($"[ScreenRecorder] Recording stopped. Total frames: {frames.Count}, Duration: {duration:mm\\:ss\\.fff}");
            return frames;
        }
    }

    public RecordingStats GetStats()
    {
        lock (_lockObj)
        {
            return new RecordingStats
            {
                IsRecording = _isRecording,
                TotalFramesCaptured = 0, // Frame count only available after stopping
                StartTime = _startTime,
                Duration = _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : null
            };
        }
    }

    private IntPtr GetPrimaryMonitor()
    {
        var monitors = new List<IntPtr>();
        ScreenCaptureInterop.MonitorEnumDelegate callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref ScreenCaptureInterop.RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new ScreenCaptureInterop.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(ScreenCaptureInterop.MONITORINFOEX));
            if (ScreenCaptureInterop.GetMonitorInfo(hMonitor, ref info))
            {
                // Primary monitor has dwFlags = 1
                if (info.dwFlags == 1)
                {
                    monitors.Insert(0, hMonitor);
                }
                else
                {
                    monitors.Add(hMonitor);
                }
            }
            return true;
        };
        
        ScreenCaptureInterop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return monitors.Count > 0 ? monitors[0] : IntPtr.Zero;
    }

    /// <summary>
    /// Gets a list of all available monitor handles (deprecated - use EnumerateMonitorsDetailed)
    /// </summary>
    public static List<(IntPtr Handle, string DeviceName, bool IsPrimary)> EnumerateMonitors()
    {
        var monitors = new List<(IntPtr, string, bool)>();
        ScreenCaptureInterop.MonitorEnumDelegate callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref ScreenCaptureInterop.RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new ScreenCaptureInterop.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(ScreenCaptureInterop.MONITORINFOEX));
            if (ScreenCaptureInterop.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add((hMonitor, info.szDevice, info.dwFlags == 1));
            }
            return true;
        };
        
        ScreenCaptureInterop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return monitors;
    }

    /// <summary>
    /// Managed representation of monitor information
    /// </summary>
    public class MonitorInformation
    {
        public IntPtr Handle { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public bool IsPrimary { get; set; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>
    /// Managed representation of window information
    /// </summary>
    public class WindowInformation
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsVisible { get; set; }
    }

    /// <summary>
    /// Enumerates all available monitors with detailed information
    /// </summary>
    public static List<MonitorInformation> EnumerateMonitorsDetailed()
    {
        var monitors = new List<MonitorInformation>();

        if (ScreenCaptureInterop.Capture_EnumerateMonitors(out IntPtr monitorsPtr, out int count))
        {
            if (count > 0 && monitorsPtr != IntPtr.Zero)
            {
                int structSize = Marshal.SizeOf<ScreenCaptureInterop.MonitorInfoNative>();

                for (int i = 0; i < count; i++)
                {
                    IntPtr monitorPtr = IntPtr.Add(monitorsPtr, i * structSize);
                    var nativeMonitor = Marshal.PtrToStructure<ScreenCaptureInterop.MonitorInfoNative>(monitorPtr);

                    monitors.Add(new MonitorInformation
                    {
                        Handle = nativeMonitor.Handle,
                        DeviceName = Marshal.PtrToStringUni(nativeMonitor.DeviceName) ?? string.Empty,
                        Left = nativeMonitor.Left,
                        Top = nativeMonitor.Top,
                        Right = nativeMonitor.Right,
                        Bottom = nativeMonitor.Bottom,
                        IsPrimary = nativeMonitor.IsPrimary
                    });
                }

                ScreenCaptureInterop.Capture_FreeMonitors(monitorsPtr, count);
            }
        }

        return monitors;
    }

    /// <summary>
    /// Enumerates all visible windows with detailed information
    /// </summary>
    public static List<WindowInformation> EnumerateWindows()
    {
        var windows = new List<WindowInformation>();

        if (ScreenCaptureInterop.Capture_EnumerateWindows(out IntPtr windowsPtr, out int count))
        {
            if (count > 0 && windowsPtr != IntPtr.Zero)
            {
                int structSize = Marshal.SizeOf<ScreenCaptureInterop.WindowInfo>();

                for (int i = 0; i < count; i++)
                {
                    IntPtr windowPtr = IntPtr.Add(windowsPtr, i * structSize);
                    var nativeWindow = Marshal.PtrToStructure<ScreenCaptureInterop.WindowInfo>(windowPtr);

                    windows.Add(new WindowInformation
                    {
                        Handle = nativeWindow.Handle,
                        Title = Marshal.PtrToStringUni(nativeWindow.Title) ?? string.Empty,
                        Width = nativeWindow.Width,
                        Height = nativeWindow.Height,
                        IsVisible = nativeWindow.IsVisible
                    });
                }

                ScreenCaptureInterop.Capture_FreeWindows(windowsPtr, count);
            }
        }

        return windows;
    }

    /// <summary>
    /// Gets a monitor handle by 1-based index
    /// </summary>
    public static IntPtr GetMonitorByIndex(int index)
    {
        if (index <= 0)
        {
            return IntPtr.Zero;
        }

        var monitors = EnumerateMonitorsDetailed();
        if (index > monitors.Count)
        {
            return IntPtr.Zero;
        }

        return monitors[index - 1].Handle;
    }

    /// <summary>
    /// Gets a window handle by 1-based index
    /// </summary>
    public static IntPtr GetWindowByIndex(int index)
    {
        if (index <= 0)
        {
            return IntPtr.Zero;
        }

        var windows = EnumerateWindows();
        if (index > windows.Count)
        {
            return IntPtr.Zero;
        }

        return windows[index - 1].Handle;
    }

    public void Dispose()
    {
        Stop();
    }
}
