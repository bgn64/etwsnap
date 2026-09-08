#pragma once

#include "pch.h"

TRACELOGGING_DECLARE_PROVIDER(g_etwSnapProvider);

namespace EtwProvider
{
    HRESULT Initialize() noexcept;
    void Shutdown() noexcept;
    void RecordingStarted(
        const GUID& sessionId,
        std::uint64_t qpcFrequency,
        std::uint32_t framesPerSecond,
        std::uint64_t bufferBytes,
        std::uint32_t targetKind,
        std::uint64_t targetHandle) noexcept;
    void FrameCaptured(
        const GUID& sessionId,
        std::uint64_t frameNumber,
        std::int64_t presentationTime100ns,
        std::int64_t callbackQpc,
        std::uint32_t width,
        std::uint32_t height) noexcept;
    void FrameCaptureError(const GUID& sessionId, std::uint64_t frameNumber, HRESULT error) noexcept;
    void RecordingStopped(
        const GUID& sessionId,
        std::uint64_t accepted,
        std::uint64_t retained,
        std::uint64_t evicted,
        std::uint64_t dropped,
        std::uint64_t errors) noexcept;
}
