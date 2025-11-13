#pragma once
#include "pch.h"

namespace winrt
{
    using namespace Windows::Foundation;
    using namespace Windows::Graphics;
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Graphics::DirectX;
    using namespace Windows::Graphics::DirectX::Direct3D11;
}

// Callback function pointer type for frame events
// Parameters: width, height, timestamp (milliseconds since epoch), user context
typedef void(*FrameArrivedCallback)(int width, int height, int64_t timestamp, void* userContext);

class CaptureManager
{
public:
    CaptureManager(HWND hwnd, int frameIntervalMs);
    ~CaptureManager();

    void StartCapture();
    void StopCapture();
    void SetFrameCallback(FrameArrivedCallback callback, void* userContext);

    bool IsCursorEnabled() const;
    void SetCursorEnabled(bool enabled);

private:
    void OnFrameArrived(
        winrt::Direct3D11CaptureFramePool const& sender,
        winrt::IInspectable const& args);

    void CheckClosed();

private:
    winrt::GraphicsCaptureItem m_item{ nullptr };
    winrt::Direct3D11CaptureFramePool m_framePool{ nullptr };
    winrt::GraphicsCaptureSession m_session{ nullptr };

    winrt::IDirect3DDevice m_device{ nullptr };
    winrt::com_ptr<ID3D11Device> m_d3dDevice{ nullptr };

    FrameArrivedCallback m_frameCallback{ nullptr };
    void* m_userContext{ nullptr };

    std::atomic<bool> m_closed{ false };
    std::chrono::steady_clock::time_point m_lastFrameTime;
    int m_frameIntervalMs;
};
