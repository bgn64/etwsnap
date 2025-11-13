namespace ETWSnap.Service.Recording;

/// <summary>
/// Represents the current state of a recording session
/// </summary>
public class RecordingState
{
    public bool IsRecording { get; set; }
    public DateTime? StartTime { get; set; }
    public string? CurrentSessionId { get; set; }
    public RecordingStats? Stats { get; set; }
}

/// <summary>
/// Interface for managing recording state
/// </summary>
public interface IRecordingStateManager
{
    bool IsRecording { get; }
    RecordingState GetState();
    bool StartRecording();
    bool StopRecording(string? filePath = null);
    bool CancelRecording();
}

/// <summary>
/// Manages the recording state for the service
/// </summary>
public class RecordingStateManager : IRecordingStateManager
{
    private readonly object _lockObj = new();
    private RecordingState _state = new();
    private readonly IScreenRecorder _screenRecorder;

    public RecordingStateManager(IScreenRecorder screenRecorder)
    {
        _screenRecorder = screenRecorder ?? throw new ArgumentNullException(nameof(screenRecorder));
    }

    public bool IsRecording
    {
        get
        {
            lock (_lockObj)
            {
                return _state.IsRecording;
            }
        }
    }

    public RecordingState GetState()
    {
        lock (_lockObj)
        {
            // Update stats from the screen recorder
            var stats = _screenRecorder.IsRecording ? _screenRecorder.GetStats() : null;
            
            return new RecordingState
            {
                IsRecording = _state.IsRecording,
                StartTime = _state.StartTime,
                CurrentSessionId = _state.CurrentSessionId,
                Stats = stats
            };
        }
    }

    public bool StartRecording()
    {
        lock (_lockObj)
        {
            if (_state.IsRecording)
            {
                return false; // Already recording
            }

            _state.StartTime = DateTime.UtcNow;
            _state.CurrentSessionId = Guid.NewGuid().ToString();

            Console.WriteLine($"[StateManager] Recording started - Session ID: {_state.CurrentSessionId}");
            
            // Start screen recording with default options
            // By default, capture the primary monitor (both handles are zero)
            var options = new RecordingOptions
            {
                FramesPerSecond = 30,           // 30 FPS
                MaxBufferSizeMB = 500,          // 500 MB buffer
                CaptureCursor = true,           // Include cursor
                WindowHandle = IntPtr.Zero,     // Not capturing a specific window
                MonitorHandle = IntPtr.Zero     // Will capture primary monitor
            };

            if (!_screenRecorder.Start(options))
            {
                Console.WriteLine($"[StateManager] Failed to start screen recorder");
                _state.StartTime = null;
                _state.CurrentSessionId = null;
                return false;
            }

            _state.IsRecording = true;
            return true;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    public bool StopRecording(string? filePath = null)
    {
        lock (_lockObj)
        {
            if (!_state.IsRecording)
            {
                return false; // Not recording
            }

            var sessionId = _state.CurrentSessionId;
            var duration = DateTime.UtcNow - _state.StartTime;

            Console.WriteLine($"[StateManager] Recording stopped - Session ID: {sessionId}, Duration: {duration}");
            
            // Stop screen recording
            _screenRecorder.Stop();

            // Get final stats
            var stats = _screenRecorder.GetStats();
            Console.WriteLine($"[StateManager] Captured {stats.TotalFramesCaptured} frames");
            Console.WriteLine($"[StateManager] Buffer contains {stats.BufferStats.FrameCount} frames ({stats.BufferStats.CurrentSizeInBytes / (1024 * 1024)} MB)");

            // Save frames to disk
            if (!string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine($"[StateManager] Saving recording to: {filePath}");
                
                // Retrieve frames from buffer
                var frames = _screenRecorder.FrameBuffer.GetAllFrames();
                
                if (frames.Count > 0)
                {
                    // Determine output directory
                    // If filePath is a directory, use it directly
                    // If it's a file path, use its directory
                    string outputDirectory;
                    if (Directory.Exists(filePath))
                    {
                        outputDirectory = filePath;
                    }
                    else
                    {
                        // Extract directory from file path, or use current directory
                        outputDirectory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
                    }
                    
                    // Create save options
                    var saveOptions = new FrameSaveOptions
                    {
                        OutputDirectory = outputDirectory,
                        BaseFilename = $"frame_{sessionId}",
                        Format = _screenRecorder.Options.ImageFormat,
                        JpegQuality = _screenRecorder.Options.JpegQuality
                    };
                    
                    // Save frames
                    int savedCount = FrameSaver.SaveFrames(frames, saveOptions);
                    Console.WriteLine($"[StateManager] Successfully saved {savedCount} frames to: {outputDirectory}");
                }
                else
                {
                    Console.WriteLine($"[StateManager] No frames available to save");
                }
            }

            // Reset state
            _state.IsRecording = false;
            _state.StartTime = null;
            _state.CurrentSessionId = null;

            return true;
        }
    }

    public bool CancelRecording()
    {
        lock (_lockObj)
        {
            if (!_state.IsRecording)
            {
                return false; // Not recording
            }

            var sessionId = _state.CurrentSessionId;
            var duration = DateTime.UtcNow - _state.StartTime;

            Console.WriteLine($"[StateManager] Recording cancelled - Session ID: {sessionId}, Duration: {duration}");
            
            // Stop screen recording without saving
            _screenRecorder.Stop();
            
            // Reset state
            _state.IsRecording = false;
            _state.StartTime = null;
            _state.CurrentSessionId = null;

            return true;
        }
    }
}
