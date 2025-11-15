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
    bool StartRecording(RecordingOptions? options = null);
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
            return new RecordingState
            {
                IsRecording = _state.IsRecording,
                StartTime = _state.StartTime,
                CurrentSessionId = _state.CurrentSessionId,
                Stats = null // Stats only available after stopping
            };
        }
    }

    public bool StartRecording(RecordingOptions? options = null)
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
            
            // Create recording options with defaults if not provided
            var recordingOptions = options ?? new RecordingOptions();
            
            // Apply default values if not specified
            if (recordingOptions.FramesPerSecond == 0)
            {
                recordingOptions.FramesPerSecond = 30;
            }
            if (recordingOptions.MaxBufferSizeMB == 0)
            {
                recordingOptions.MaxBufferSizeMB = 500;
            }

            if (!_screenRecorder.Start(recordingOptions))
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
            
            // Stop screen recording and retrieve frames
            var frames = _screenRecorder.Stop();
            Console.WriteLine($"[StateManager] Retrieved {frames.Count} frames from recording");

            // Save frames to disk
            if (!string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine($"[StateManager] Saving recording to: {filePath}");
                
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
            
            // Stop screen recording without saving (discard frames)
            var frames = _screenRecorder.Stop();
            Console.WriteLine($"[StateManager] Discarded {frames.Count} frames");
            
            // Reset state
            _state.IsRecording = false;
            _state.StartTime = null;
            _state.CurrentSessionId = null;

            return true;
        }
    }
}
