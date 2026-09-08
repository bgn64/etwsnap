#include "pch.h"
#include "CaptureSession.h"
#include "EtwProvider.h"

using namespace winrt;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

CaptureSession::CaptureSession(const EtwSnapCreateOptions& options)
    : m_options(options), m_ring(options.BufferBytes)
{
    if (!GraphicsCaptureSession::IsSupported())
    {
        throw hresult_not_implemented();
    }

    LARGE_INTEGER frequency{};
    check_bool(QueryPerformanceFrequency(&frequency));
    m_qpcFrequency = frequency.QuadPart;
    m_minimumQpcInterval = std::max<std::int64_t>(1, m_qpcFrequency / options.FramesPerSecond);

    m_d3dDevice = robmikh::common::uwp::CreateD3D11Device();
    m_d3dDevice->GetImmediateContext(m_d3dContext.put());
    const auto dxgiDevice = m_d3dDevice.as<IDXGIDevice>();
    m_device = CreateDirect3DDevice(dxgiDevice.get());

    switch (options.TargetKind)
    {
    case EtwSnapTargetKind_PrimaryMonitor:
    {
        POINT origin{};
        const auto monitor = MonitorFromPoint(origin, MONITOR_DEFAULTTOPRIMARY);
        m_item = robmikh::common::desktop::CreateCaptureItemForMonitor(monitor);
        break;
    }
    case EtwSnapTargetKind_Monitor:
        m_item = robmikh::common::desktop::CreateCaptureItemForMonitor(reinterpret_cast<HMONITOR>(options.TargetHandle));
        break;
    case EtwSnapTargetKind_Window:
        m_item = robmikh::common::desktop::CreateCaptureItemForWindow(reinterpret_cast<HWND>(options.TargetHandle));
        break;
    default:
        throw hresult_invalid_argument();
    }

    const auto size = m_item.Size();
    if (size.Width <= 0 || size.Height <= 0)
    {
        throw hresult_invalid_argument();
    }
    m_lastSize = size;

    m_framePool = Direct3D11CaptureFramePool::CreateFreeThreaded(
        m_device,
        DirectXPixelFormat::B8G8R8A8UIntNormalized,
        2,
        size);
    m_captureSession = m_framePool.CreateCaptureSession(m_item);
    m_captureSession.IsCursorCaptureEnabled(options.CaptureCursor != 0);
}

CaptureSession::~CaptureSession()
{
    try
    {
        Stop();
    }
    catch (...)
    {
    }
}

void CaptureSession::Start()
{
    std::scoped_lock lock(m_stateMutex);
    if (m_started || m_stopped)
    {
        throw hresult_illegal_method_call();
    }

    const auto weakSession = weak_from_this();
    m_frameToken = m_framePool.FrameArrived(
        [weakSession](const Direct3D11CaptureFramePool& sender, const winrt::Windows::Foundation::IInspectable& args)
        {
            if (const auto session = weakSession.lock())
            {
                session->OnFrameArrived(sender, args);
            }
        });
    EtwProvider::RecordingStarted(
        m_options.SessionId,
        static_cast<std::uint64_t>(m_qpcFrequency),
        m_options.FramesPerSecond,
        m_options.BufferBytes,
        m_options.TargetKind,
        m_options.TargetHandle);

    m_accepting = true;
    m_started = true;
    m_captureSession.StartCapture();
}

void CaptureSession::Stop()
{
    winrt::event_token frameToken{};
    {
        std::scoped_lock lock(m_stateMutex);
        if (m_stopped)
        {
            return;
        }

        if (!m_started)
        {
            m_stopped = true;
            return;
        }

        m_accepting = false;
        frameToken = m_frameToken;
        m_frameToken = {};
    }

    const auto recordCleanupError = [this](const HRESULT error)
    {
        ++m_errorCount;
        EtwProvider::FrameCaptureError(m_options.SessionId, 0, error);
    };

    try
    {
        if (m_framePool && frameToken.value != 0)
        {
            m_framePool.FrameArrived(frameToken);
        }
    }
    catch (const hresult_error& error)
    {
        recordCleanupError(error.code());
    }

    {
        std::unique_lock lock(m_stateMutex);
        m_callbacksDrained.wait(lock, [this] { return m_inFlightCallbacks == 0; });
        m_snapshot = m_ring.Snapshot();
        m_stopped = true;
    }

    try
    {
        if (m_captureSession)
        {
            m_captureSession.Close();
        }
    }
    catch (const hresult_error& error)
    {
        recordCleanupError(error.code());
    }
    try
    {
        if (m_framePool)
        {
            m_framePool.Close();
        }
    }
    catch (const hresult_error& error)
    {
        recordCleanupError(error.code());
    }

    EtwProvider::RecordingStopped(
        m_options.SessionId,
        m_acceptedFrames.load(),
        m_snapshot.size(),
        m_evictedFrames.load(),
        m_droppedFrames.load(),
        m_errorCount.load());

    m_captureSession = nullptr;
    m_framePool = nullptr;
    m_item = nullptr;
}

EtwSnapStats CaptureSession::GetStats() const
{
    std::scoped_lock lock(m_stateMutex);
    EtwSnapStats stats{};
    stats.StructSize = sizeof(stats);
    stats.ApiVersion = ETWSNAP_API_VERSION;
    stats.ObservedFrames = m_observedFrames.load();
    stats.AcceptedFrames = m_acceptedFrames.load();
    stats.RetainedFrames = m_stopped ? m_snapshot.size() : m_ring.Count();
    stats.EvictedFrames = m_evictedFrames.load();
    stats.DroppedFrames = m_droppedFrames.load();
    stats.ErrorCount = m_errorCount.load();
    return stats;
}

std::uint64_t CaptureSession::GetFrameCount() const
{
    std::scoped_lock lock(m_stateMutex);
    if (!m_stopped)
    {
        throw hresult_illegal_method_call();
    }
    return m_snapshot.size();
}

EtwSnapFrameInfo CaptureSession::GetFrameInfo(std::uint64_t index) const
{
    std::scoped_lock lock(m_stateMutex);
    if (!m_stopped || index >= m_snapshot.size())
    {
        throw hresult_invalid_argument();
    }
    return m_snapshot[static_cast<std::size_t>(index)].Info;
}

void CaptureSession::CopyFrame(
    std::uint64_t index,
    void* destination,
    std::uint64_t destinationBytes,
    std::uint32_t destinationStride)
{
    std::scoped_lock lock(m_stateMutex);
    if (!m_stopped || index >= m_snapshot.size() || destination == nullptr)
    {
        throw hresult_invalid_argument();
    }

    const auto& frame = m_snapshot[static_cast<std::size_t>(index)];
    const auto rowBytes = static_cast<std::uint64_t>(frame.Info.Width) * 4u;
    if (destinationStride < rowBytes || destinationBytes < static_cast<std::uint64_t>(destinationStride) * frame.Info.Height)
    {
        throw hresult_error(HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER));
    }

    D3D11_TEXTURE2D_DESC sourceDescription{};
    frame.Texture->GetDesc(&sourceDescription);
    auto stagingDescription = sourceDescription;
    stagingDescription.Usage = D3D11_USAGE_STAGING;
    stagingDescription.BindFlags = 0;
    stagingDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    stagingDescription.MiscFlags = 0;

    if (!m_stagingTexture ||
        m_stagingDescription.Width != stagingDescription.Width ||
        m_stagingDescription.Height != stagingDescription.Height ||
        m_stagingDescription.Format != stagingDescription.Format)
    {
        m_stagingTexture = nullptr;
        check_hresult(m_d3dDevice->CreateTexture2D(&stagingDescription, nullptr, m_stagingTexture.put()));
        m_stagingDescription = stagingDescription;
    }
    m_d3dContext->CopyResource(m_stagingTexture.get(), frame.Texture.get());

    D3D11_MAPPED_SUBRESOURCE mapped{};
    check_hresult(m_d3dContext->Map(m_stagingTexture.get(), 0, D3D11_MAP_READ, 0, &mapped));
    try
    {
        auto* output = static_cast<std::uint8_t*>(destination);
        const auto* input = static_cast<const std::uint8_t*>(mapped.pData);
        for (std::uint32_t row = 0; row < frame.Info.Height; ++row)
        {
            std::memcpy(output + static_cast<std::uint64_t>(row) * destinationStride, input + static_cast<std::uint64_t>(row) * mapped.RowPitch, rowBytes);
        }
    }
    catch (...)
    {
        m_d3dContext->Unmap(m_stagingTexture.get(), 0);
        throw;
    }
    m_d3dContext->Unmap(m_stagingTexture.get(), 0);
}

bool CaptureSession::EnterCallback()
{
    std::scoped_lock lock(m_stateMutex);
    if (!m_accepting)
    {
        return false;
    }
    ++m_inFlightCallbacks;
    return true;
}

void CaptureSession::ExitCallback() noexcept
{
    std::scoped_lock lock(m_stateMutex);
    if (--m_inFlightCallbacks == 0)
    {
        m_callbacksDrained.notify_all();
    }
}

std::uint64_t CaptureSession::LogicalFrameBytes(std::uint32_t width, std::uint32_t height)
{
    constexpr std::uint64_t bytesPerPixel = 4;
    if (height != 0 && width > (std::numeric_limits<std::uint64_t>::max)() / height / bytesPerPixel)
    {
        winrt::throw_hresult(E_OUTOFMEMORY);
    }
    return static_cast<std::uint64_t>(width) * height * bytesPerPixel;
}

void CaptureSession::OnFrameArrived(
    const Direct3D11CaptureFramePool& sender,
    const winrt::Windows::Foundation::IInspectable&)
{
    if (!EnterCallback())
    {
        return;
    }

    std::scoped_lock callbackLock(m_callbackMutex);
    std::uint64_t frameNumber = 0;
    try
    {
        const auto frame = sender.TryGetNextFrame();
        if (!frame)
        {
            ExitCallback();
            return;
        }

        ++m_observedFrames;
        LARGE_INTEGER qpc{};
        check_bool(QueryPerformanceCounter(&qpc));
        const auto previous = m_lastAcceptedQpc.load();
        if (previous != 0 && qpc.QuadPart - previous < m_minimumQpcInterval)
        {
            ExitCallback();
            return;
        }
        m_lastAcceptedQpc.store(qpc.QuadPart);

        frameNumber = ++m_acceptedFrames;
        const auto contentSize = frame.ContentSize();
        const auto width = static_cast<std::uint32_t>(contentSize.Width);
        const auto height = static_cast<std::uint32_t>(contentSize.Height);
        const auto presentationTime = frame.SystemRelativeTime().count();

        EtwProvider::FrameCaptured(
            m_options.SessionId,
            frameNumber,
            presentationTime,
            qpc.QuadPart,
            width,
            height);

        const auto logicalBytes = LogicalFrameBytes(width, height);
        if (logicalBytes > m_options.BufferBytes)
        {
            ++m_droppedFrames;
            ExitCallback();
            return;
        }

        const auto source = GetDXGIInterfaceFromObject<ID3D11Texture2D>(frame.Surface());
        D3D11_TEXTURE2D_DESC textureDescription{};
        source->GetDesc(&textureDescription);

        com_ptr<ID3D11Texture2D> copy;
        check_hresult(m_d3dDevice->CreateTexture2D(&textureDescription, nullptr, copy.put()));
        m_d3dContext->CopyResource(copy.get(), source.get());

        EtwSnapFrameInfo info{};
        info.StructSize = sizeof(info);
        info.ApiVersion = ETWSNAP_API_VERSION;
        info.FrameNumber = frameNumber;
        info.PresentationTime100ns = presentationTime;
        info.CallbackQpc = qpc.QuadPart;
        info.Width = width;
        info.Height = height;
        info.PixelFormat = EtwSnapPixelFormat_Bgra8;
        info.RequiredBytes = logicalBytes;

        const auto addResult = m_ring.Add({ std::move(copy), info, info.RequiredBytes });
        m_evictedFrames += addResult.Evicted;
        if (!addResult.Retained)
        {
            ++m_droppedFrames;
        }

        if (contentSize != m_lastSize)
        {
            m_framePool.Recreate(m_device, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, contentSize);
            m_lastSize = contentSize;
        }
    }
    catch (const hresult_error& error)
    {
        ++m_errorCount;
        EtwProvider::FrameCaptureError(m_options.SessionId, frameNumber, error.code());
    }
    catch (...)
    {
        ++m_errorCount;
        EtwProvider::FrameCaptureError(m_options.SessionId, frameNumber, E_FAIL);
    }

    ExitCallback();
}
