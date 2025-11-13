using System.Runtime.InteropServices;

namespace ETWSnap.Service.Recording;

/// <summary>
/// Screen recorder implementation using Windows.Graphics.Capture
/// </summary>
public class ScreenRecorder : IScreenRecorder
{
    private readonly object _lockObj = new();
    private IntPtr _captureHandle = IntPtr.Zero;
    private FrameBuffer _frameBuffer;
    private RecordingOptions _options;
    private bool _isRecording = false;
    private int _totalFramesCaptured = 0;
    private DateTime? _startTime;
    
    // Keep a reference to the callback to prevent it from being garbage collected
    private ScreenCaptureInterop.FrameArrivedCallback? _frameCallback;

    public ScreenRecorder()
    {
        _options = new RecordingOptions();
        _frameBuffer = new FrameBuffer(_options.MaxBufferSizeBytes);
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

    public FrameBuffer FrameBuffer
    {
        get
        {
            lock (_lockObj)
            {
                return _frameBuffer;
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
                // Recreate buffer with new size if changed
                if (_frameBuffer.MaxSizeInBytes != _options.MaxBufferSizeBytes)
                {
                    _frameBuffer = new FrameBuffer(_options.MaxBufferSizeBytes);
                }
            }
            else
            {
                // Clear existing buffer for new recording
                _frameBuffer.Clear();
            }

            // Determine what to capture: monitor takes precedence over window
            if (_options.MonitorHandle != IntPtr.Zero)
            {
                // Capture specified monitor
                Console.WriteLine($"[ScreenRecorder] Capturing monitor handle: 0x{_options.MonitorHandle:X}");
                _captureHandle = ScreenCaptureInterop.Capture_CreateForMonitor(_options.MonitorHandle, _options.FrameIntervalMs);
            }
            else if (_options.WindowHandle != IntPtr.Zero)
            {
                // Capture specified window
                Console.WriteLine($"[ScreenRecorder] Capturing window handle: 0x{_options.WindowHandle:X}");
                _captureHandle = ScreenCaptureInterop.Capture_Create(_options.WindowHandle, _options.FrameIntervalMs);
            }
            else
            {
                // Capture primary monitor by default
                var primaryMonitor = GetPrimaryMonitor();
                Console.WriteLine($"[ScreenRecorder] Capturing primary monitor: 0x{primaryMonitor:X}");
                _captureHandle = ScreenCaptureInterop.Capture_CreateForMonitor(primaryMonitor, _options.FrameIntervalMs);
            }

            if (_captureHandle == IntPtr.Zero)
            {
                Console.WriteLine("[ScreenRecorder] Failed to create capture manager");
                return false;
            }

            Console.WriteLine($"[ScreenRecorder] Capture created successfully (FPS: {_options.FramesPerSecond}, Buffer: {_options.MaxBufferSizeMB}MB)");

            // Set up the frame callback
            _frameCallback = OnFrameArrived;
            ScreenCaptureInterop.Capture_SetFrameCallback(_captureHandle, _frameCallback, IntPtr.Zero);

            // Configure cursor capture
            ScreenCaptureInterop.Capture_SetCursorEnabled(_captureHandle, _options.CaptureCursor);
            Console.WriteLine($"[ScreenRecorder] Cursor capture: {_options.CaptureCursor}");

            // Start capturing
            if (!ScreenCaptureInterop.Capture_Start(_captureHandle))
            {
                Console.WriteLine("[ScreenRecorder] Failed to start capture");
                ScreenCaptureInterop.Capture_Destroy(_captureHandle);
                _captureHandle = IntPtr.Zero;
                _frameCallback = null;
                return false;
            }

            _isRecording = true;
            _totalFramesCaptured = 0;
            _startTime = DateTime.UtcNow;
            Console.WriteLine("[ScreenRecorder] Recording started successfully");
            return true;
        }
    }

    public void Stop()
    {
        lock (_lockObj)
        {
            if (!_isRecording)
            {
                return;
            }

            Console.WriteLine("[ScreenRecorder] Stopping recording...");

            if (_captureHandle != IntPtr.Zero)
            {
                ScreenCaptureInterop.Capture_Stop(_captureHandle);
                ScreenCaptureInterop.Capture_Destroy(_captureHandle);
                _captureHandle = IntPtr.Zero;
            }

            _frameCallback = null;
            _isRecording = false;

            var duration = _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : TimeSpan.Zero;
            Console.WriteLine($"[ScreenRecorder] Recording stopped. Total frames: {_totalFramesCaptured}, Duration: {duration:mm\\:ss\\.fff}");
        }
    }

    public RecordingStats GetStats()
    {
        lock (_lockObj)
        {
            return new RecordingStats
            {
                IsRecording = _isRecording,
                TotalFramesCaptured = _totalFramesCaptured,
                BufferStats = _frameBuffer.GetStats(),
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
    /// Gets a list of all available monitor handles
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

    private void OnFrameArrived(IntPtr pixelData, int width, int height, int rowPitch, long timestamp, IntPtr userContext)
    {
        try
        {
            lock (_lockObj)
            {
                if (!_isRecording)
                {
                    return;
                }

                _totalFramesCaptured++;

                // Calculate actual data size needed
                // We need to copy row-by-row if rowPitch > width*4 due to alignment
                int bytesPerPixel = 4; // BGRA8
                int rowWidth = width * bytesPerPixel;
                
                byte[] frameData = new byte[height * rowWidth];

                // Copy pixel data from unmanaged to managed memory
                if (rowPitch == rowWidth)
                {
                    // No padding - can copy entire buffer at once
                    Marshal.Copy(pixelData, frameData, 0, frameData.Length);
                }
                else
                {
                    // Has padding - copy row by row to remove padding
                    for (int row = 0; row < height; row++)
                    {
                        IntPtr sourceRow = IntPtr.Add(pixelData, row * rowPitch);
                        Marshal.Copy(sourceRow, frameData, row * rowWidth, rowWidth);
                    }
                }

                var frame = new FrameData
                {
                    Width = width,
                    Height = height,
                    Timestamp = timestamp,
                    FrameNumber = _totalFramesCaptured,
                    PixelData = frameData
                };

                _frameBuffer.AddFrame(frame);

                // Log progress every 30 frames (approximately once per second at 30 FPS)
                if (_totalFramesCaptured % 30 == 0)
                {
                    var stats = _frameBuffer.GetStats();
                    Console.WriteLine($"[ScreenRecorder] Frame {_totalFramesCaptured}: {width}x{height}, Buffer: {stats.FrameCount} frames ({stats.CurrentSizeInBytes / (1024 * 1024)}MB / {stats.MaxSizeInBytes / (1024 * 1024)}MB)");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenRecorder] Error in frame callback: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
