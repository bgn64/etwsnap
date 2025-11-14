#include "pch.h"
#include "exports.h"
#include "CaptureManager.h"
#include <iostream>

extern "C" {
    CAPTURE_API int Add(int a, int b) {
        return a + b;
    }

    CAPTURE_API void PrintMessage(const char* message) {
        if (message != nullptr) {
            std::cout << "C++ DLL says: " << message << std::endl;
        }
    }

    CAPTURE_API bool Multiply(int a, int b, int* result) {
        if (result == nullptr) {
            return false;
        }
        *result = a * b;
        return true;
    }

    // ===== Screen Capture API Implementation =====

    CAPTURE_API void* Capture_Create(void* windowHandle, int frameIntervalMs, int maxFrames) {
        try {
            if (windowHandle == nullptr || frameIntervalMs <= 0 || maxFrames <= 0) {
                return nullptr;
            }

            auto manager = new CaptureManager(static_cast<HWND>(windowHandle), frameIntervalMs, static_cast<size_t>(maxFrames));
            return static_cast<void*>(manager);
        }
        catch (...) {
            return nullptr;
        }
    }

    CAPTURE_API void* Capture_CreateForMonitor(void* monitorHandle, int frameIntervalMs, int maxFrames) {
        try {
            if (monitorHandle == nullptr || frameIntervalMs <= 0 || maxFrames <= 0) {
                return nullptr;
            }

            auto manager = new CaptureManager(static_cast<HMONITOR>(monitorHandle), frameIntervalMs, static_cast<size_t>(maxFrames));
            return static_cast<void*>(manager);
        }
        catch (...) {
            return nullptr;
        }
    }

    CAPTURE_API bool Capture_Start(void* captureHandle) {
        try {
            if (captureHandle == nullptr) {
                return false;
            }

            auto manager = static_cast<CaptureManager*>(captureHandle);
            manager->StartCapture();
            return true;
        }
        catch (...) {
            return false;
        }
    }

    CAPTURE_API void Capture_Stop(void* captureHandle) {
        try {
            if (captureHandle != nullptr) {
                auto manager = static_cast<CaptureManager*>(captureHandle);
                manager->StopCapture();
            }
        }
        catch (...) {
            // Ignore errors during stop
        }
    }

    CAPTURE_API void Capture_Destroy(void* captureHandle) {
        try {
            if (captureHandle != nullptr) {
                auto manager = static_cast<CaptureManager*>(captureHandle);
                delete manager;
            }
        }
        catch (...) {
            // Ignore errors during cleanup
        }
    }

    CAPTURE_API bool Capture_GetFrames(void* captureHandle, FrameData** outFrames, int* outCount) {
        try {
            if (captureHandle == nullptr || outFrames == nullptr || outCount == nullptr) {
                return false;
            }

            auto manager = static_cast<CaptureManager*>(captureHandle);
            auto frames = manager->GetFrames();

            if (frames.empty()) {
                *outFrames = nullptr;
                *outCount = 0;
                return true;
            }

            // Allocate array for frame data
            auto frameArray = new FrameData[frames.size()];

            // Process each frame - Map texture to get CPU-accessible pixel data
            winrt::com_ptr<ID3D11Device> d3dDevice;
            winrt::com_ptr<ID3D11DeviceContext> d3dContext;
            
            // Get D3D device from first frame's texture
            frames[0].Texture->GetDevice(d3dDevice.put());
            d3dDevice->GetImmediateContext(d3dContext.put());

            for (size_t i = 0; i < frames.size(); i++) {
                auto& frame = frames[i];
                
                D3D11_TEXTURE2D_DESC desc{};
                frame.Texture->GetDesc(&desc);

                // Create staging texture for CPU access
                D3D11_TEXTURE2D_DESC stagingDesc = desc;
                stagingDesc.Usage = D3D11_USAGE_STAGING;
                stagingDesc.BindFlags = 0;
                stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                stagingDesc.MiscFlags = 0;

                winrt::com_ptr<ID3D11Texture2D> stagingTexture;
                winrt::check_hresult(d3dDevice->CreateTexture2D(&stagingDesc, nullptr, stagingTexture.put()));

                // Copy to staging texture
                d3dContext->CopyResource(stagingTexture.get(), frame.Texture.get());

                // Map to get CPU access
                D3D11_MAPPED_SUBRESOURCE mapped{};
                HRESULT hr = d3dContext->Map(stagingTexture.get(), 0, D3D11_MAP_READ, 0, &mapped);

                if (SUCCEEDED(hr)) {
                    // Calculate data size
                    int bytesPerPixel = 4; // BGRA8
                    int rowWidth = frame.Width * bytesPerPixel;
                    size_t dataSize = frame.Height * rowWidth;

                    // Allocate and copy pixel data
                    void* pixelData = malloc(dataSize);
                    if (pixelData) {
                        if (mapped.RowPitch == rowWidth) {
                            // No padding - single copy
                            memcpy(pixelData, mapped.pData, dataSize);
                        } else {
                            // Has padding - copy row by row
                            for (int row = 0; row < frame.Height; row++) {
                                void* srcRow = static_cast<byte*>(mapped.pData) + (row * mapped.RowPitch);
                                void* dstRow = static_cast<byte*>(pixelData) + (row * rowWidth);
                                memcpy(dstRow, srcRow, rowWidth);
                            }
                        }

                        frameArray[i].PixelData = pixelData;
                        frameArray[i].Width = frame.Width;
                        frameArray[i].Height = frame.Height;
                        frameArray[i].RowPitch = rowWidth;
                        frameArray[i].Timestamp = frame.Timestamp;
                        frameArray[i].FrameNumber = frame.FrameNumber;
                    }

                    d3dContext->Unmap(stagingTexture.get(), 0);
                }
            }

            *outFrames = frameArray;
            *outCount = static_cast<int>(frames.size());
            return true;
        }
        catch (...) {
            return false;
        }
    }

    CAPTURE_API void Capture_FreeFrames(FrameData* frames, int count) {
        if (frames != nullptr) {
            for (int i = 0; i < count; i++) {
                if (frames[i].PixelData != nullptr) {
                    free(frames[i].PixelData);
                }
            }
            delete[] frames;
        }
    }

    CAPTURE_API void Capture_SetCursorEnabled(void* captureHandle, bool enabled) {
        try {
            if (captureHandle != nullptr) {
                auto manager = static_cast<CaptureManager*>(captureHandle);
                manager->SetCursorEnabled(enabled);
            }
        }
        catch (...) {
            // Ignore errors
        }
    }

    CAPTURE_API bool Capture_IsCursorEnabled(void* captureHandle) {
        try {
            if (captureHandle != nullptr) {
                auto manager = static_cast<CaptureManager*>(captureHandle);
                return manager->IsCursorEnabled();
            }
        }
        catch (...) {
            // Ignore errors
        }
        return false;
    }
}

