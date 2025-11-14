#include "pch.h"
#include "EtwLogging.h"
#include <evntrace.h>

// Define the ETW provider with the same name as the C# EventSource
// Name: "ETWSnap-Service"
// Using the exact GUID that C# EventSource generates from the name "ETWSnap-Service"
// GUID: {524507bc-3009-5e8d-c071-00a1c641849f}
TRACELOGGING_DEFINE_PROVIDER(
    g_EtwSnapProvider,
    "ETWSnap-Service",
    (0x524507bc, 0x3009, 0x5e8d, 0xc0, 0x71, 0x00, 0xa1, 0xc6, 0x41, 0x84, 0x9f));

namespace EtwLogging
{
    void Initialize()
    {
        // Register the ETW provider
        TraceLoggingRegister(g_EtwSnapProvider);
    }

    void Shutdown()
    {
        // Unregister the ETW provider
        TraceLoggingUnregister(g_EtwSnapProvider);
    }

    void LogFrameCaptured(int frameNumber, int64_t timestamp, int width, int height, const std::wstring& filename)
    {
        // Event ID: 1, Level: Informational
        // Message: "Frame captured: {filename}"
        TraceLoggingWrite(
            g_EtwSnapProvider,
            "FrameCaptured",
            TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
            TraceLoggingInt32(frameNumber, "FrameNumber"),
            TraceLoggingInt64(timestamp, "Timestamp"),
            TraceLoggingInt32(width, "Width"),
            TraceLoggingInt32(height, "Height"),
            TraceLoggingWideString(filename.c_str(), "Filename"));
    }

    void LogRecordingStarted(const std::wstring& sessionId, int fps, int bufferSizeMB)
    {
        // Event ID: 2, Level: Informational
        // Message: "Recording started: SessionId={sessionId}, FPS={fps}, BufferSizeMB={bufferSizeMB}"
        TraceLoggingWrite(
            g_EtwSnapProvider,
            "RecordingStarted",
            TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
            TraceLoggingWideString(sessionId.c_str(), "SessionId"),
            TraceLoggingInt32(fps, "FPS"),
            TraceLoggingInt32(bufferSizeMB, "BufferSizeMB"));
    }

    void LogRecordingStopped(const std::wstring& sessionId, int totalFrames, int64_t durationMs)
    {
        // Event ID: 3, Level: Informational
        // Message: "Recording stopped: SessionId={sessionId}, TotalFrames={totalFrames}, DurationMs={durationMs}"
        TraceLoggingWrite(
            g_EtwSnapProvider,
            "RecordingStopped",
            TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
            TraceLoggingWideString(sessionId.c_str(), "SessionId"),
            TraceLoggingInt32(totalFrames, "TotalFrames"),
            TraceLoggingInt64(durationMs, "DurationMs"));
    }

    void LogFrameCaptureError(int frameNumber, const std::wstring& errorMessage)
    {
        // Event ID: 4, Level: Error
        // Message: "Frame capture error: Frame={frameNumber}, Error={errorMessage}"
        TraceLoggingWrite(
            g_EtwSnapProvider,
            "FrameCaptureError",
            TraceLoggingLevel(TRACE_LEVEL_ERROR),
            TraceLoggingInt32(frameNumber, "FrameNumber"),
            TraceLoggingWideString(errorMessage.c_str(), "ErrorMessage"));
    }
}
