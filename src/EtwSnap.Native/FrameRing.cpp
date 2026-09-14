#include "pch.h"
#include "FrameRing.h"

FrameRing::FrameRing(std::uint64_t byteBudget) : m_byteBudget(byteBudget)
{
    if (byteBudget == 0)
    {
        throw std::invalid_argument("The frame buffer byte budget must be positive.");
    }
}

winrt::com_ptr<ID3D11Texture2D> FrameRing::TakeReusableTexture(
    const D3D11_TEXTURE2D_DESC& description,
    std::uint64_t logicalBytes)
{
    std::scoped_lock lock(m_mutex);
    if (logicalBytes == 0 || logicalBytes > m_byteBudget || m_frames.empty() ||
        m_bytes <= m_byteBudget - logicalBytes)
    {
        return {};
    }

    D3D11_TEXTURE2D_DESC previous{};
    m_frames.front().Texture->GetDesc(&previous);
    if (previous.Width != description.Width || previous.Height != description.Height ||
        previous.MipLevels != description.MipLevels || previous.ArraySize != description.ArraySize ||
        previous.Format != description.Format || previous.SampleDesc.Count != description.SampleDesc.Count ||
        previous.SampleDesc.Quality != description.SampleDesc.Quality || previous.Usage != description.Usage ||
        previous.BindFlags != description.BindFlags || previous.CPUAccessFlags != description.CPUAccessFlags ||
        previous.MiscFlags != description.MiscFlags)
    {
        return {};
    }

    auto texture = std::move(m_frames.front().Texture);
    m_bytes -= m_frames.front().LogicalBytes;
    m_frames.pop_front();
    return texture;
}

AddFrameResult FrameRing::Add(CapturedFrame frame)
{
    std::scoped_lock lock(m_mutex);
    if (frame.LogicalBytes == 0 || frame.LogicalBytes > m_byteBudget)
    {
        return {};
    }

    std::uint64_t evicted = 0;
    while (!m_frames.empty() && m_bytes > m_byteBudget - frame.LogicalBytes)
    {
        m_bytes -= m_frames.front().LogicalBytes;
        m_frames.pop_front();
        ++evicted;
    }

    m_bytes += frame.LogicalBytes;
    m_frames.push_back(std::move(frame));
    return { true, evicted };
}

std::vector<CapturedFrame> FrameRing::Snapshot() const
{
    std::scoped_lock lock(m_mutex);
    return { m_frames.begin(), m_frames.end() };
}

std::uint64_t FrameRing::Count() const
{
    std::scoped_lock lock(m_mutex);
    return m_frames.size();
}

std::uint64_t FrameRing::Bytes() const
{
    std::scoped_lock lock(m_mutex);
    return m_bytes;
}
