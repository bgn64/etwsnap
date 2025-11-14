#pragma once
#include "pch.h"
#include <vector>
#include <mutex>

struct CapturedFrame
{
    winrt::com_ptr<ID3D11Texture2D> Texture;
    int Width;
    int Height;
    int64_t Timestamp;
    int FrameNumber;
};

class CircularFrameBuffer
{
public:
    CircularFrameBuffer(size_t maxFrames);
    ~CircularFrameBuffer();

    // Add a frame to the buffer (drops oldest if full)
    void AddFrame(winrt::com_ptr<ID3D11Texture2D> texture, int width, int height, int64_t timestamp, int frameNumber);

    // Get all frames (returns copies of frame metadata and textures)
    std::vector<CapturedFrame> GetAllFrames();

    // Clear all frames
    void Clear();

    // Get current frame count
    size_t GetFrameCount() const;

    // Get max capacity
    size_t GetMaxFrames() const { return m_maxFrames; }

private:
    std::vector<CapturedFrame> m_frames;
    size_t m_maxFrames;
    size_t m_writeIndex;
    bool m_isFull;
    mutable std::mutex m_mutex;
};
