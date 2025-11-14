#pragma once
#include "pch.h"
#include <string>

// ETWSnap-Service TraceLogging Provider
// This provider matches the C# EventSource: [EventSource(Name = "ETWSnap-Service")]
TRACELOGGING_DECLARE_PROVIDER(g_EtwSnapProvider);

namespace EtwLogging
{
    // Initialize the ETW provider (call once at startup)
    void Initialize();

    // Shutdown the ETW provider (call once at shutdown)
    void Shutdown();

    /// <summary>
    /// Logs when a frame is captured
    /// Event ID: 1, Level: Informational
    /// </summary>
    /// <param name="frameNumber">The frame number in the recording sequence</param>
    /// <param name="timestamp">The timestamp when the frame was captured</param>
    /// <param name="width">The width of the frame in pixels</param>
    /// <param name="height">The height of the frame in pixels</param>
    /// <param name="filename">The filename that will be used when the frame is saved to disk</param>
    void LogFrameCaptured(int frameNumber, int64_t timestamp, int width, int height, const std::wstring& filename);

    /// <summary>
    /// Logs when recording starts
    /// Event ID: 2, Level: Informational
    /// </summary>
    /// <param name="sessionId">The unique session identifier</param>
    /// <param name="fps">Frames per second</param>
    /// <param name="bufferSizeMB">Buffer size in megabytes</param>
    void LogRecordingStarted(const std::wstring& sessionId, int fps, int bufferSizeMB);

    /// <summary>
    /// Logs when recording stops
    /// Event ID: 3, Level: Informational
    /// </summary>
    /// <param name="sessionId">The unique session identifier</param>
    /// <param name="totalFrames">Total number of frames captured</param>
    /// <param name="durationMs">Duration of recording in milliseconds</param>
    void LogRecordingStopped(const std::wstring& sessionId, int totalFrames, int64_t durationMs);

    /// <summary>
    /// Logs errors during frame capture
    /// Event ID: 4, Level: Error
    /// </summary>
    /// <param name="frameNumber">The frame number that failed</param>
    /// <param name="errorMessage">The error message</param>
    void LogFrameCaptureError(int frameNumber, const std::wstring& errorMessage);
}
