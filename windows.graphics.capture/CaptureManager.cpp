#include "pch.h"
#include "CaptureManager.h"
#include <chrono>
#include <Windows.Graphics.Capture.Interop.h>

using namespace winrt;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

CaptureManager::CaptureManager(HWND hwnd, int frameIntervalMs)
    : m_frameIntervalMs(frameIntervalMs)
{
    // Initialize Direct3D device
    winrt::com_ptr<ID3D11Device> d3dDevice;
    D3D_FEATURE_LEVEL featureLevel;
    winrt::check_hresult(D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        nullptr, 0,
        D3D11_SDK_VERSION,
        d3dDevice.put(),
        &featureLevel,
        nullptr));
    
    m_d3dDevice = d3dDevice;
    m_device = CreateDirect3DDevice(m_d3dDevice.as<IDXGIDevice>().get());

    // Create capture item from HWND
    auto interop = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
    check_hresult(interop->CreateForWindow(
        hwnd,
        guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
        put_abi(m_item)));

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
    m_session.StartCapture();
}

void CaptureManager::StopCapture()
{
    auto expected = false;
    if (m_closed.compare_exchange_strong(expected, true))
    {
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

void CaptureManager::SetFrameCallback(FrameArrivedCallback callback, void* userContext)
{
    m_frameCallback = callback;
    m_userContext = userContext;
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

        // Get frame info
        auto contentSize = frame.ContentSize();
        
        // Get timestamp
        auto timestamp = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::system_clock::now().time_since_epoch()).count();

        // Call the callback if registered
        if (m_frameCallback)
        {
            m_frameCallback(contentSize.Width, contentSize.Height, timestamp, m_userContext);
        }
    }
}

void CaptureManager::CheckClosed()
{
    if (m_closed.load())
    {
        throw hresult_error(RO_E_CLOSED);
    }
}
