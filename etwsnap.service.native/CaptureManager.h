#pragma once
#include "pch.h"
#include "exports.h"
#include "CircularFrameBuffer.h"
#include <string>

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
    CaptureManager(HWND hwnd, int frameIntervalMs, size_t maxFrames);
    CaptureManager(HMONITOR hmon, int frameIntervalMs, size_t maxFrames);
    ~CaptureManager();

    void StartCapture();
    void StopCapture();
    std::vector<CapturedFrame> GetFrames();

    bool IsCursorEnabled() const;
    void SetCursorEnabled(bool enabled);

private:
    void OnFrameArrived(
        winrt::Direct3D11CaptureFramePool const& sender,
        winrt::IInspectable const& args);

    void CheckClosed();
    std::wstring GenerateSessionId();

private:
    winrt::GraphicsCaptureItem m_item{ nullptr };
    winrt::Direct3D11CaptureFramePool m_framePool{ nullptr };
    winrt::GraphicsCaptureSession m_session{ nullptr };

    winrt::IDirect3DDevice m_device{ nullptr };
    winrt::com_ptr<ID3D11Device> m_d3dDevice{ nullptr };
    winrt::com_ptr<ID3D11DeviceContext> m_d3dContext{ nullptr };

    CircularFrameBuffer m_frameBuffer;
    int m_frameNumber{ 0 };

    std::atomic<bool> m_closed{ false };
    std::chrono::steady_clock::time_point m_lastFrameTime;
    std::chrono::steady_clock::time_point m_recordingStartTime;
    int m_frameIntervalMs;
    size_t m_maxFrames;
    std::wstring m_sessionId;
};
