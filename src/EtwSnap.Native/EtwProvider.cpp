#include "pch.h"
#include "EtwProvider.h"

TRACELOGGING_DEFINE_PROVIDER(
    g_etwSnapProvider,
    "ETWSnap-Service",
    (0x524507bc, 0x3009, 0x5e8d, 0xc0, 0x71, 0x00, 0xa1, 0xc6, 0x41, 0x84, 0x9f));

namespace
{
    std::mutex g_providerMutex;
    bool g_registered = false;
}

HRESULT EtwProvider::Initialize() noexcept
{
    std::scoped_lock lock(g_providerMutex);
    if (g_registered)
    {
        return S_OK;
    }

    const auto status = TraceLoggingRegister(g_etwSnapProvider);
    if (status != ERROR_SUCCESS)
    {
        return HRESULT_FROM_WIN32(status);
    }

    g_registered = true;
    return S_OK;
}

void EtwProvider::Shutdown() noexcept
{
    std::scoped_lock lock(g_providerMutex);
    if (g_registered)
    {
        TraceLoggingUnregister(g_etwSnapProvider);
        g_registered = false;
    }
}

void EtwProvider::RecordingStarted(
    const GUID& sessionId,
    std::uint64_t qpcFrequency,
    std::uint32_t framesPerSecond,
    std::uint64_t bufferBytes,
    std::uint32_t targetKind,
    std::uint64_t targetHandle) noexcept
{
    TraceLoggingWrite(
        g_etwSnapProvider,
        "RecordingStarted",
        TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
        TraceLoggingGuid(sessionId, "SessionId"),
        TraceLoggingUInt64(qpcFrequency, "QpcFrequency"),
        TraceLoggingUInt32(framesPerSecond, "FramesPerSecond"),
        TraceLoggingUInt64(bufferBytes, "BufferBytes"),
        TraceLoggingUInt32(targetKind, "TargetKind"),
        TraceLoggingUInt64(targetHandle, "TargetHandle"));
}

void EtwProvider::FrameCaptured(
    const GUID& sessionId,
    std::uint64_t frameNumber,
    std::int64_t presentationTime100ns,
    std::int64_t callbackQpc,
    std::uint32_t width,
    std::uint32_t height) noexcept
{
    TraceLoggingWrite(
        g_etwSnapProvider,
        "FrameCaptured",
        TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
        TraceLoggingGuid(sessionId, "SessionId"),
        TraceLoggingUInt64(frameNumber, "FrameNumber"),
        TraceLoggingInt64(presentationTime100ns, "PresentationTime100ns"),
        TraceLoggingInt64(callbackQpc, "CallbackQpc"),
        TraceLoggingUInt32(width, "Width"),
        TraceLoggingUInt32(height, "Height"),
        TraceLoggingUInt32(EtwSnapPixelFormat_Bgra8, "PixelFormat"));
}

void EtwProvider::FrameCaptureError(const GUID& sessionId, std::uint64_t frameNumber, HRESULT error) noexcept
{
    TraceLoggingWrite(
        g_etwSnapProvider,
        "FrameCaptureError",
        TraceLoggingLevel(TRACE_LEVEL_ERROR),
        TraceLoggingGuid(sessionId, "SessionId"),
        TraceLoggingUInt64(frameNumber, "FrameNumber"),
        TraceLoggingHResult(error, "HResult"));
}

void EtwProvider::RecordingStopped(
    const GUID& sessionId,
    std::uint64_t accepted,
    std::uint64_t retained,
    std::uint64_t evicted,
    std::uint64_t dropped,
    std::uint64_t errors) noexcept
{
    TraceLoggingWrite(
        g_etwSnapProvider,
        "RecordingStopped",
        TraceLoggingLevel(TRACE_LEVEL_INFORMATION),
        TraceLoggingGuid(sessionId, "SessionId"),
        TraceLoggingUInt64(accepted, "AcceptedFrames"),
        TraceLoggingUInt64(retained, "RetainedFrames"),
        TraceLoggingUInt64(evicted, "EvictedFrames"),
        TraceLoggingUInt64(dropped, "DroppedFrames"),
        TraceLoggingUInt64(errors, "ErrorCount"));
}
