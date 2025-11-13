namespace ETWSnap.Service.Recording;

/// <summary>
/// Configuration options for screen recording
/// </summary>
public class RecordingOptions
{
    /// <summary>
    /// Target frames per second (default: 30 FPS)
    /// </summary>
    public int FramesPerSecond { get; set; } = 30;

    /// <summary>
    /// Maximum buffer size in megabytes (default: 500 MB)
    /// </summary>
    public long MaxBufferSizeMB { get; set; } = 500;

    /// <summary>
    /// Whether to capture the cursor (default: true)
    /// </summary>
    public bool CaptureCursor { get; set; } = true;

    /// <summary>
    /// Window handle to capture (IntPtr.Zero means capture monitor or desktop)
    /// </summary>
    public IntPtr WindowHandle { get; set; } = IntPtr.Zero;

    /// <summary>
    /// Monitor handle to capture (IntPtr.Zero means capture window or desktop)
    /// If both WindowHandle and MonitorHandle are zero, captures the primary monitor
    /// </summary>
    public IntPtr MonitorHandle { get; set; } = IntPtr.Zero;

    /// <summary>
    /// Image format to use when saving frames (default: PNG)
    /// </summary>
    public FrameImageFormat ImageFormat { get; set; } = FrameImageFormat.PNG;

    /// <summary>
    /// JPEG quality (1-100), only used when ImageFormat is JPEG (default: 90)
    /// </summary>
    public long JpegQuality { get; set; } = 90;

    /// <summary>
    /// Gets the frame interval in milliseconds based on FPS
    /// </summary>
    public int FrameIntervalMs => 1000 / FramesPerSecond;

    /// <summary>
    /// Gets the buffer size in bytes
    /// </summary>
    public long MaxBufferSizeBytes => MaxBufferSizeMB * 1024 * 1024;
}

/// <summary>
/// Interface for screen recording functionality
/// </summary>
public interface IScreenRecorder : IDisposable
{
    /// <summary>
    /// Whether recording is currently active
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Current recording options
    /// </summary>
    RecordingOptions Options { get; }

    /// <summary>
    /// Gets the current frame buffer
    /// </summary>
    FrameBuffer FrameBuffer { get; }

    /// <summary>
    /// Starts screen recording with the specified options
    /// </summary>
    bool Start(RecordingOptions? options = null);

    /// <summary>
    /// Stops screen recording
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets statistics about the current recording session
    /// </summary>
    RecordingStats GetStats();
}

/// <summary>
/// Statistics about the current recording session
/// </summary>
public class RecordingStats
{
    public bool IsRecording { get; set; }
    public int TotalFramesCaptured { get; set; }
    public BufferStats BufferStats { get; set; } = new();
    public DateTime? StartTime { get; set; }
    public TimeSpan? Duration { get; set; }
}
