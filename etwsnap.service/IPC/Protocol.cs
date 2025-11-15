namespace ETWSnap.IPC;

/// <summary>
/// Command types supported by the IPC protocol
/// </summary>
public enum CommandType
{
    Start,
    Stop,
    Cancel,
    CheckStatus,
    ListWindows,
    ListMonitors
}

/// <summary>
/// Response status codes
/// </summary>
public enum ResponseStatus
{
    Success,
    Error,
    AlreadyRecording,
    NotRecording
}

/// <summary>
/// Request message sent from client to service
/// </summary>
public class Request
{
    public CommandType Command { get; set; }
    public string? FilePath { get; set; }  // Used for Stop command
    public Dictionary<string, string> Parameters { get; set; } = new();
    
    // Recording options for Start command (using long for JSON serialization)
    public long WindowHandle { get; set; } = 0;
    public long MonitorHandle { get; set; } = 0;
}

/// <summary>
/// Response message sent from service to client
/// </summary>
public class Response
{
    public ResponseStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRecording { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
}
