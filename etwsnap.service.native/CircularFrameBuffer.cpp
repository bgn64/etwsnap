#include "pch.h"
#include "CircularFrameBuffer.h"

CircularFrameBuffer::CircularFrameBuffer(size_t maxFrames)
    : m_maxFrames(maxFrames)
    , m_writeIndex(0)
    , m_isFull(false)
{
    m_frames.resize(maxFrames);
}

CircularFrameBuffer::~CircularFrameBuffer()
{
    Clear();
}

void CircularFrameBuffer::AddFrame(winrt::com_ptr<ID3D11Texture2D> texture, int width, int height, int64_t timestamp, int frameNumber)
{
    std::lock_guard<std::mutex> lock(m_mutex);

    // Store frame at current write index
    m_frames[m_writeIndex] = CapturedFrame{
        texture,
        width,
        height,
        timestamp,
        frameNumber
    };

    // Advance write index (circular)
    m_writeIndex = (m_writeIndex + 1) % m_maxFrames;

    // Track if we've filled the buffer at least once
    if (m_writeIndex == 0 && !m_isFull)
    {
        m_isFull = true;
    }
}

std::vector<CapturedFrame> CircularFrameBuffer::GetAllFrames()
{
    std::lock_guard<std::mutex> lock(m_mutex);

    std::vector<CapturedFrame> result;

    if (!m_isFull)
    {
        // Buffer not yet full, return frames from 0 to writeIndex
        for (size_t i = 0; i < m_writeIndex; i++)
        {
            if (m_frames[i].Texture)
            {
                result.push_back(m_frames[i]);
            }
        }
    }
    else
    {
        // Buffer is full, return frames in chronological order
        // Start from writeIndex (oldest) and wrap around
        for (size_t i = 0; i < m_maxFrames; i++)
        {
            size_t index = (m_writeIndex + i) % m_maxFrames;
            if (m_frames[index].Texture)
            {
                result.push_back(m_frames[index]);
            }
        }
    }

    return result;
}

void CircularFrameBuffer::Clear()
{
    std::lock_guard<std::mutex> lock(m_mutex);

    for (auto& frame : m_frames)
    {
        frame.Texture = nullptr;
    }

    m_writeIndex = 0;
    m_isFull = false;
}

size_t CircularFrameBuffer::GetFrameCount() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_isFull ? m_maxFrames : m_writeIndex;
}
