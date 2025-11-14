#include "pch.h"
#include "CaptureManager.h"
#include "CircularFrameBuffer.h"
#include "EtwLogging.h"
#include <chrono>
#include <sstream>
#include <iomanip>

using namespace winrt;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

CaptureManager::CaptureManager(HWND hwnd, int frameIntervalMs, size_t maxFrames)
    : m_frameIntervalMs(frameIntervalMs)
    , m_frameBuffer(maxFrames)
    , m_maxFrames(maxFrames)
    , m_sessionId(GenerateSessionId())
{
    // Initialize Direct3D device using robmikh.common helper
    m_d3dDevice = robmikh::common::uwp::CreateD3D11Device();
    m_d3dDevice->GetImmediateContext(m_d3dContext.put());
    auto dxgiDevice = m_d3dDevice.as<IDXGIDevice>();
    m_device = CreateDirect3DDevice(dxgiDevice.get());

    // Create capture item from HWND using robmikh.common helper
    m_item = robmikh::common::desktop::CreateCaptureItemForWindow(hwnd);

    // Create frame pool
    auto pixelFormat = DirectXPixelFormat::B8G8R8A8UIntNormalized;
    m_framePool = Direct3D11CaptureFramePool::CreateFreeThreaded(
        m_device,
        pixelFormat,
        2,
        m_item.Size());

    // Create capture session
    m_session = m_framePool.CreateCaptureSession(m_item);

    // Register frame arrived handler
    m_framePool.FrameArrived({ this, &CaptureManager::OnFrameArrived });
}

CaptureManager::CaptureManager(HMONITOR hmon, int frameIntervalMs, size_t maxFrames)
    : m_frameIntervalMs(frameIntervalMs)
    , m_frameBuffer(maxFrames)
    , m_maxFrames(maxFrames)
    , m_sessionId(GenerateSessionId())
{
    // Initialize Direct3D device using robmikh.common helper
    m_d3dDevice = robmikh::common::uwp::CreateD3D11Device();
    m_d3dDevice->GetImmediateContext(m_d3dContext.put());
    auto dxgiDevice = m_d3dDevice.as<IDXGIDevice>();
    m_device = CreateDirect3DDevice(dxgiDevice.get());

    // Create capture item from HMONITOR using robmikh.common helper
    m_item = robmikh::common::desktop::CreateCaptureItemForMonitor(hmon);

    // Create frame pool
    auto pixelFormat = DirectXPixelFormat::B8G8R8A8UIntNormalized;
    m_framePool = Direct3D11CaptureFramePool::CreateFreeThreaded(
        m_device,
        pixelFormat,
        2,
        m_item.Size());

    // Create capture session
    m_session = m_framePool.CreateCaptureSession(m_item);

    // Register frame arrived handler
    m_framePool.FrameArrived({ this, &CaptureManager::OnFrameArrived });
}

CaptureManager::~CaptureManager()
{
    StopCapture();
}

void CaptureManager::StartCapture()
{
    CheckClosed();
    m_lastFrameTime = std::chrono::steady_clock::now();
    m_recordingStartTime = m_lastFrameTime;
    m_session.StartCapture();

    // Log recording started event
    int fps = m_frameIntervalMs > 0 ? (1000 / m_frameIntervalMs) : 0;
    int bufferSizeMB = static_cast<int>(m_maxFrames * 4 * 1920 * 1080 / (1024 * 1024)); // Rough estimate
    EtwLogging::LogRecordingStarted(m_sessionId, fps, bufferSizeMB);
}

void CaptureManager::StopCapture()
{
    auto expected = false;
    if (m_closed.compare_exchange_strong(expected, true))
    {
        // Calculate recording duration
        auto now = std::chrono::steady_clock::now();
        auto durationMs = std::chrono::duration_cast<std::chrono::milliseconds>(
            now - m_recordingStartTime).count();

        // Log recording stopped event
        EtwLogging::LogRecordingStopped(m_sessionId, m_frameNumber, durationMs);

        if (m_session)
        {
            m_session.Close();
        }
        if (m_framePool)
        {
            m_framePool.Close();
        }

        m_framePool = nullptr;
        m_session = nullptr;
        m_item = nullptr;
    }
}

std::vector<CapturedFrame> CaptureManager::GetFrames()
{
    return m_frameBuffer.GetAllFrames();
}

bool CaptureManager::IsCursorEnabled() const
{
    if (m_session)
    {
        return m_session.IsCursorCaptureEnabled();
    }
    return false;
}

void CaptureManager::SetCursorEnabled(bool enabled)
{
    CheckClosed();
    m_session.IsCursorCaptureEnabled(enabled);
}

void CaptureManager::OnFrameArrived(
    Direct3D11CaptureFramePool const& sender,
    winrt::Windows::Foundation::IInspectable const&)
{
    auto frame = sender.TryGetNextFrame();
    if (!frame)
    {
        return;
    }

    auto now = std::chrono::steady_clock::now();
    auto timeSinceLastFrame = std::chrono::duration_cast<std::chrono::milliseconds>(
        now - m_lastFrameTime).count();

    if (timeSinceLastFrame >= m_frameIntervalMs)
    {
        m_lastFrameTime = now;
        m_frameNumber++;

        // Get frame info
        auto contentSize = frame.ContentSize();
        
        // Get timestamp
        auto timestamp = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::system_clock::now().time_since_epoch()).count();

        // Generate filename for this frame
        std::wstringstream filenameStream;
        filenameStream << L"frame_" << std::setw(6) << std::setfill(L'0') << m_frameNumber << L".png";
        std::wstring filename = filenameStream.str();

        // Log ETW event for frame captured
        EtwLogging::LogFrameCaptured(m_frameNumber, timestamp, contentSize.Width, contentSize.Height, filename);

        // Get the surface texture from the frame
        auto surfaceTexture = GetDXGIInterfaceFromObject<ID3D11Texture2D>(frame.Surface());

        D3D11_TEXTURE2D_DESC desc{};
        surfaceTexture->GetDesc(&desc);

        // Create a copy of the texture (GPU-side only)
        winrt::com_ptr<ID3D11Texture2D> frameTexture;
        winrt::check_hresult(m_d3dDevice->CreateTexture2D(&desc, nullptr, frameTexture.put()));

        // Copy frame texture (fast GPU-to-GPU copy)
        m_d3dContext->CopyResource(frameTexture.get(), surfaceTexture.get());

        // Store in circular buffer
        m_frameBuffer.AddFrame(
            frameTexture,
            contentSize.Width,
            contentSize.Height,
            timestamp,
            m_frameNumber);
    }
}

void CaptureManager::CheckClosed()
{
    if (m_closed.load())
    {
        throw hresult_error(RO_E_CLOSED);
    }
}

std::wstring CaptureManager::GenerateSessionId()
{
    // Generate a simple session ID based on timestamp
    auto now = std::chrono::system_clock::now();
    auto timestamp = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()).count();
    
    std::wstringstream ss;
    ss << L"session_" << timestamp;
    return ss.str();
}
