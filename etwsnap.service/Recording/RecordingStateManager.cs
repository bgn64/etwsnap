namespace ETWSnap.Service.Recording;

/// <summary>
/// Represents the current state of a recording session
/// </summary>
public class RecordingState
{
    public bool IsRecording { get; set; }
    public DateTime? StartTime { get; set; }
    public string? CurrentSessionId { get; set; }
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
                CurrentSessionId = _state.CurrentSessionId
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

            _state.IsRecording = true;
            _state.StartTime = DateTime.UtcNow;
            _state.CurrentSessionId = Guid.NewGuid().ToString();

            Console.WriteLine($"[StateManager] Recording started - Session ID: {_state.CurrentSessionId}");
            
            // TODO: Actually start ETW recording here
            // This is where you would initialize ETW session
            
            return true;
        }
    }

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
            
            // TODO: Actually stop ETW recording and save to file here
            // This is where you would stop ETW session and save the trace
            if (!string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine($"[StateManager] Would save recording to: {filePath}");
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
            
            // TODO: Actually cancel ETW recording without saving here
            // This is where you would stop ETW session without saving
            
            // Reset state
            _state.IsRecording = false;
            _state.StartTime = null;
            _state.CurrentSessionId = null;

            return true;
        }
    }
}
