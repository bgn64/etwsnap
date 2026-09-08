#pragma once

#include "pch.h"

struct CapturedFrame
{
    winrt::com_ptr<ID3D11Texture2D> Texture;
    EtwSnapFrameInfo Info{};
    std::uint64_t LogicalBytes{};
};

struct AddFrameResult
{
    bool Retained{};
    std::uint64_t Evicted{};
};

class FrameRing
{
public:
    explicit FrameRing(std::uint64_t byteBudget);

    AddFrameResult Add(CapturedFrame frame);
    std::vector<CapturedFrame> Snapshot() const;
    std::uint64_t Count() const;
    std::uint64_t Bytes() const;

private:
    const std::uint64_t m_byteBudget;
    mutable std::mutex m_mutex;
    std::deque<CapturedFrame> m_frames;
    std::uint64_t m_bytes{};
};
