#pragma once

#include <Windows.h>
#include <cstddef>
#include <cstdint>

#ifdef ETWSNAP_NATIVE_EXPORTS
#define ETWSNAP_API __declspec(dllexport)
#else
#define ETWSNAP_API __declspec(dllimport)
#endif

constexpr std::uint32_t ETWSNAP_API_VERSION = 1;

enum EtwSnapResult : std::int32_t
{
    EtwSnapResult_Success = 0,
    EtwSnapResult_InvalidArgument = 1,
    EtwSnapResult_InvalidState = 2,
    EtwSnapResult_NotSupported = 3,
    EtwSnapResult_BufferTooSmall = 4,
    EtwSnapResult_Failure = 5,
};

enum EtwSnapTargetKind : std::uint32_t
{
    EtwSnapTargetKind_PrimaryMonitor = 0,
    EtwSnapTargetKind_Monitor = 1,
    EtwSnapTargetKind_Window = 2,
};

enum EtwSnapPixelFormat : std::uint32_t
{
    EtwSnapPixelFormat_Bgra8 = 1,
};

struct EtwSnapCreateOptions
{
    std::uint32_t StructSize;
    std::uint32_t ApiVersion;
    std::uint32_t TargetKind;
    std::uint32_t CaptureCursor;
    std::uint64_t TargetHandle;
    std::uint32_t FramesPerSecond;
    std::uint32_t Reserved;
    std::uint64_t BufferBytes;
    GUID SessionId;
};

struct EtwSnapFrameInfo
{
    std::uint32_t StructSize;
    std::uint32_t ApiVersion;
    std::uint64_t FrameNumber;
    std::int64_t PresentationTime100ns;
    std::int64_t CallbackQpc;
    std::uint32_t Width;
    std::uint32_t Height;
    std::uint32_t PixelFormat;
    std::uint32_t Reserved;
    std::uint64_t RequiredBytes;
};

struct EtwSnapStats
{
    std::uint32_t StructSize;
    std::uint32_t ApiVersion;
    std::uint64_t ObservedFrames;
    std::uint64_t AcceptedFrames;
    std::uint64_t RetainedFrames;
    std::uint64_t EvictedFrames;
    std::uint64_t DroppedFrames;
    std::uint64_t ErrorCount;
};

static_assert(sizeof(EtwSnapCreateOptions) == 56);
static_assert(sizeof(EtwSnapFrameInfo) == 56);
static_assert(sizeof(EtwSnapStats) == 56);
static_assert(offsetof(EtwSnapCreateOptions, BufferBytes) == 32);
static_assert(offsetof(EtwSnapCreateOptions, SessionId) == 40);

extern "C"
{
    ETWSNAP_API std::uint32_t EtwSnap_GetApiVersion() noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_Initialize() noexcept;
    ETWSNAP_API void EtwSnap_Shutdown() noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_Create(const EtwSnapCreateOptions* options, void** session) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_Start(void* session) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_Stop(void* session) noexcept;
    ETWSNAP_API void EtwSnap_Destroy(void* session) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_GetStats(void* session, EtwSnapStats* stats) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_GetFrameCount(void* session, std::uint64_t* frameCount) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_GetFrameInfo(void* session, std::uint64_t index, EtwSnapFrameInfo* info) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_CopyFrameBgra(
        void* session,
        std::uint64_t index,
        void* destination,
        std::uint64_t destinationBytes,
        std::uint32_t destinationStride) noexcept;
    ETWSNAP_API EtwSnapResult EtwSnap_GetLastError(wchar_t* destination, std::uint32_t capacity, std::uint32_t* requiredLength) noexcept;
}
