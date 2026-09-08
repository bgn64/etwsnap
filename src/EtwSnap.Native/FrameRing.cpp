#include "pch.h"
#include "FrameRing.h"

FrameRing::FrameRing(std::uint64_t byteBudget) : m_byteBudget(byteBudget)
{
    if (byteBudget == 0)
    {
        throw std::invalid_argument("The frame buffer byte budget must be positive.");
    }
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
