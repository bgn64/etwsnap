#include "pch.h"
#include "CaptureSession.h"
#include "EtwProvider.h"

namespace
{
    using CaptureHandle = std::shared_ptr<CaptureSession>;
    thread_local std::wstring g_lastError;

    CaptureHandle& GetSession(void* session)
    {
        return *static_cast<CaptureHandle*>(session);
    }

    void SetLastErrorMessage(const wchar_t* message)
    {
        g_lastError = message == nullptr ? L"Unknown native error." : message;
    }

    EtwSnapResult TranslateException() noexcept
    {
        try
        {
            throw;
        }
        catch (const winrt::hresult_invalid_argument& error)
        {
            SetLastErrorMessage(error.message().c_str());
            return EtwSnapResult_InvalidArgument;
        }
        catch (const winrt::hresult_illegal_method_call& error)
        {
            SetLastErrorMessage(error.message().c_str());
            return EtwSnapResult_InvalidState;
        }
        catch (const winrt::hresult_not_implemented& error)
        {
            SetLastErrorMessage(error.message().c_str());
            return EtwSnapResult_NotSupported;
        }
        catch (const winrt::hresult_error& error)
        {
            SetLastErrorMessage(error.message().c_str());
            return error.code() == HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER)
                ? EtwSnapResult_BufferTooSmall
                : EtwSnapResult_Failure;
        }
        catch (const std::exception& error)
        {
            g_lastError.assign(error.what(), error.what() + std::strlen(error.what()));
            return EtwSnapResult_Failure;
        }
        catch (...)
        {
            SetLastErrorMessage(L"Unknown native error.");
            return EtwSnapResult_Failure;
        }
    }

    bool ValidStruct(const std::uint32_t size, const std::uint32_t version, const std::uint32_t expectedSize)
    {
        return size == expectedSize && version == ETWSNAP_API_VERSION;
    }

    bool ValidArtifactPaths(
        const wchar_t* artifactDirectory,
        const wchar_t* sessionDirectoryName,
        const wchar_t* manifestRelativePath,
        const wchar_t* portableManifestRelativePath)
    {
        return artifactDirectory != nullptr && sessionDirectoryName != nullptr &&
            manifestRelativePath != nullptr && portableManifestRelativePath != nullptr;
    }
}

extern "C"
{
    std::uint32_t EtwSnap_GetApiVersion() noexcept
    {
        return ETWSNAP_API_VERSION;
    }

    EtwSnapResult EtwSnap_Initialize() noexcept
    {
        try
        {
            const auto result = EtwProvider::Initialize();
            if (FAILED(result))
            {
                winrt::throw_hresult(result);
            }
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    void EtwSnap_Shutdown() noexcept
    {
        EtwProvider::Shutdown();
    }

    EtwSnapResult EtwSnap_Create(const EtwSnapCreateOptions* options, void** session) noexcept
    {
        if (options == nullptr || session == nullptr ||
            !ValidStruct(options->StructSize, options->ApiVersion, sizeof(EtwSnapCreateOptions)) ||
            options->FramesPerSecond == 0 || options->FramesPerSecond > 120 || options->BufferBytes == 0)
        {
            SetLastErrorMessage(L"Invalid capture options.");
            return EtwSnapResult_InvalidArgument;
        }

        *session = nullptr;
        try
        {
            *session = new CaptureHandle(std::make_shared<CaptureSession>(*options));
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_Start(void* session) noexcept
    {
        if (session == nullptr)
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            GetSession(session)->Start();
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_Stop(void* session) noexcept
    {
        if (session == nullptr)
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            GetSession(session)->Stop();
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    void EtwSnap_Destroy(void* session) noexcept
    {
        delete static_cast<CaptureHandle*>(session);
    }

    EtwSnapResult EtwSnap_GetStats(void* session, EtwSnapStats* stats) noexcept
    {
        if (session == nullptr || stats == nullptr || !ValidStruct(stats->StructSize, stats->ApiVersion, sizeof(EtwSnapStats)))
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            *stats = GetSession(session)->GetStats();
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_GetFrameCount(void* session, std::uint64_t* frameCount) noexcept
    {
        if (session == nullptr || frameCount == nullptr)
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            *frameCount = GetSession(session)->GetFrameCount();
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_GetFrameInfo(void* session, std::uint64_t index, EtwSnapFrameInfo* info) noexcept
    {
        if (session == nullptr || info == nullptr || !ValidStruct(info->StructSize, info->ApiVersion, sizeof(EtwSnapFrameInfo)))
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            *info = GetSession(session)->GetFrameInfo(index);
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_CopyFrameBgra(
        void* session,
        std::uint64_t index,
        void* destination,
        std::uint64_t destinationBytes,
        std::uint32_t destinationStride) noexcept
    {
        if (session == nullptr || destination == nullptr)
        {
            return EtwSnapResult_InvalidArgument;
        }
        try
        {
            GetSession(session)->CopyFrame(index, destination, destinationBytes, destinationStride);
            return EtwSnapResult_Success;
        }
        catch (...)
        {
            return TranslateException();
        }
    }

    EtwSnapResult EtwSnap_EmitArtifactReference(const EtwSnapArtifactReference* artifact) noexcept
    {
        if (artifact == nullptr ||
            !ValidStruct(artifact->StructSize, artifact->ApiVersion, sizeof(EtwSnapArtifactReference)) ||
            !ValidArtifactPaths(
                artifact->ArtifactDirectory,
                artifact->SessionDirectoryName,
                artifact->ManifestRelativePath,
                artifact->PortableManifestRelativePath))
        {
            SetLastErrorMessage(L"Invalid artifact reference.");
            return EtwSnapResult_InvalidArgument;
        }

        EtwProvider::ArtifactReference(*artifact);
        return EtwSnapResult_Success;
    }

    EtwSnapResult EtwSnap_EmitArtifactCommitted(const EtwSnapArtifactCommitted* artifact) noexcept
    {
        if (artifact == nullptr ||
            !ValidStruct(artifact->StructSize, artifact->ApiVersion, sizeof(EtwSnapArtifactCommitted)) ||
            !ValidArtifactPaths(
                artifact->ArtifactDirectory,
                artifact->SessionDirectoryName,
                artifact->ManifestRelativePath,
                artifact->PortableManifestRelativePath) ||
            artifact->ManifestSha256 == nullptr || artifact->Status == nullptr)
        {
            SetLastErrorMessage(L"Invalid committed artifact.");
            return EtwSnapResult_InvalidArgument;
        }

        EtwProvider::ArtifactCommitted(*artifact);
        return EtwSnapResult_Success;
    }

    EtwSnapResult EtwSnap_GetLastError(wchar_t* destination, std::uint32_t capacity, std::uint32_t* requiredLength) noexcept
    {
        if (requiredLength == nullptr)
        {
            return EtwSnapResult_InvalidArgument;
        }

        *requiredLength = static_cast<std::uint32_t>(g_lastError.size() + 1);
        if (destination == nullptr || capacity < *requiredLength)
        {
            return EtwSnapResult_BufferTooSmall;
        }

        std::wmemcpy(destination, g_lastError.c_str(), *requiredLength);
        return EtwSnapResult_Success;
    }
}
