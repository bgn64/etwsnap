#pragma once

#include "pch.h"
#include "FrameRing.h"

class CaptureSession final : public std::enable_shared_from_this<CaptureSession>
{
public:
    explicit CaptureSession(const EtwSnapCreateOptions& options);
    ~CaptureSession();

    void Start();
    void Stop();
    EtwSnapStats GetStats() const;
    std::uint64_t GetFrameCount() const;
    EtwSnapFrameInfo GetFrameInfo(std::uint64_t index) const;
    void CopyFrame(std::uint64_t index, void* destination, std::uint64_t destinationBytes, std::uint32_t destinationStride);

private:
    void OnFrameArrived(
        const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender,
        const winrt::Windows::Foundation::IInspectable&);
    bool EnterCallback();
    void ExitCallback() noexcept;
    static std::uint64_t LogicalFrameBytes(std::uint32_t width, std::uint32_t height);

    EtwSnapCreateOptions m_options{};
    FrameRing m_ring;
    winrt::Windows::Graphics::Capture::GraphicsCaptureItem m_item{ nullptr };
    winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool m_framePool{ nullptr };
    winrt::Windows::Graphics::Capture::GraphicsCaptureSession m_captureSession{ nullptr };
    winrt::Windows::Graphics::SizeInt32 m_lastSize{};
    winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice m_device{ nullptr };
    winrt::com_ptr<ID3D11Device> m_d3dDevice;
    winrt::com_ptr<ID3D11DeviceContext> m_d3dContext;
    winrt::com_ptr<ID3D11Texture2D> m_stagingTexture;
    D3D11_TEXTURE2D_DESC m_stagingDescription{};
    winrt::event_token m_frameToken{};
    std::vector<CapturedFrame> m_snapshot;

    mutable std::mutex m_stateMutex;
    std::mutex m_callbackMutex;
    std::condition_variable m_callbacksDrained;
    std::uint32_t m_inFlightCallbacks{};
    bool m_started{};
    bool m_accepting{};
    bool m_stopped{};

    std::int64_t m_qpcFrequency{};
    std::int64_t m_minimumQpcInterval{};
    std::atomic<std::int64_t> m_lastAcceptedQpc{};
    std::atomic<std::uint64_t> m_observedFrames{};
    std::atomic<std::uint64_t> m_acceptedFrames{};
    std::atomic<std::uint64_t> m_evictedFrames{};
    std::atomic<std::uint64_t> m_droppedFrames{};
    std::atomic<std::uint64_t> m_errorCount{};
};
