using System.Diagnostics.Tracing;

namespace ETWSnap.Service.Recording;

/// <summary>
/// ETW Event Source for ETWSnap service events
/// </summary>
[EventSource(Name = "ETWSnap-Service")]
public sealed class EtwSnapEventSource : EventSource
{
    /// <summary>
    /// Singleton instance of the event source
    /// </summary>
    public static readonly EtwSnapEventSource Log = new();

    private EtwSnapEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
    {
    }

    /// <summary>
    /// Logs when a frame is captured
    /// </summary>
    /// <param name="frameNumber">The frame number in the recording sequence</param>
    /// <param name="timestamp">The timestamp when the frame was captured</param>
    /// <param name="width">The width of the frame in pixels</param>
    /// <param name="height">The height of the frame in pixels</param>
    /// <param name="filename">The filename that will be used when the frame is saved to disk</param>
    [Event(1, Level = EventLevel.Informational, Message = "Frame captured: {4}")]
    public void FrameCaptured(int frameNumber, long timestamp, int width, int height, string filename)
    {
        if (IsEnabled())
        {
            WriteEvent(1, frameNumber, timestamp, width, height, filename);
        }
    }

    /// <summary>
    /// Logs when recording starts
    /// </summary>
    /// <param name="sessionId">The unique session identifier</param>
    /// <param name="fps">Frames per second</param>
    /// <param name="bufferSizeMB">Buffer size in megabytes</param>
    [Event(2, Level = EventLevel.Informational, Message = "Recording started: SessionId={0}, FPS={1}, BufferSizeMB={2}")]
    public void RecordingStarted(string sessionId, int fps, int bufferSizeMB)
    {
        if (IsEnabled())
        {
            WriteEvent(2, sessionId, fps, bufferSizeMB);
        }
    }

    /// <summary>
    /// Logs when recording stops
    /// </summary>
    /// <param name="sessionId">The unique session identifier</param>
    /// <param name="totalFrames">Total number of frames captured</param>
    /// <param name="durationMs">Duration of recording in milliseconds</param>
    [Event(3, Level = EventLevel.Informational, Message = "Recording stopped: SessionId={0}, TotalFrames={1}, DurationMs={2}")]
    public void RecordingStopped(string sessionId, int totalFrames, long durationMs)
    {
        if (IsEnabled())
        {
            WriteEvent(3, sessionId, totalFrames, durationMs);
        }
    }

    /// <summary>
    /// Logs errors during frame capture
    /// </summary>
    /// <param name="frameNumber">The frame number that failed</param>
    /// <param name="errorMessage">The error message</param>
    [Event(4, Level = EventLevel.Error, Message = "Frame capture error: Frame={0}, Error={1}")]
    public void FrameCaptureError(int frameNumber, string errorMessage)
    {
        if (IsEnabled())
        {
            WriteEvent(4, frameNumber, errorMessage);
        }
    }
}
