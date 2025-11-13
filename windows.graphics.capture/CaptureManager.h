#pragma once
#include "pch.h"
#include "exports.h"

namespace winrt
{
    using namespace Windows::Foundation;
    using namespace Windows::Graphics;
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Graphics::DirectX;
    using namespace Windows::Graphics::DirectX::Direct3D11;
}

class CaptureManager
{
public:
    CaptureManager(HWND hwnd, int frameIntervalMs);
    CaptureManager(HMONITOR hmon, int frameIntervalMs);
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
    winrt::com_ptr<ID3D11DeviceContext> m_d3dContext{ nullptr };
    winrt::com_ptr<ID3D11Texture2D> m_stagingTexture{ nullptr };

    FrameArrivedCallback m_frameCallback{ nullptr };
    void* m_userContext{ nullptr };

    std::atomic<bool> m_closed{ false };
    std::chrono::steady_clock::time_point m_lastFrameTime;
    int m_frameIntervalMs;
};
