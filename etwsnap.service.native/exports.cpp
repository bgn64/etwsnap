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

    // ===== Window and Monitor Enumeration Implementation =====

    struct EnumWindowsData {
        std::vector<WindowInfo> windows;
    };

    BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam) {
        auto data = reinterpret_cast<EnumWindowsData*>(lParam);

        // Skip invisible windows
        if (!IsWindowVisible(hwnd)) {
            return TRUE;
        }

        // Get window title
        int titleLength = GetWindowTextLengthW(hwnd);
        if (titleLength == 0) {
            return TRUE; // Skip windows without titles
        }

        wchar_t* title = new wchar_t[titleLength + 1];
        GetWindowTextW(hwnd, title, titleLength + 1);

        // Get window rect
        RECT rect{};
        GetWindowRect(hwnd, &rect);

        WindowInfo info{};
        info.Handle = hwnd;
        info.Title = title;
        info.Width = rect.right - rect.left;
        info.Height = rect.bottom - rect.top;
        info.IsVisible = true;

        data->windows.push_back(info);
        return TRUE;
    }

    CAPTURE_API bool Capture_EnumerateWindows(WindowInfo** outWindows, int* outCount) {
        try {
            if (outWindows == nullptr || outCount == nullptr) {
                return false;
            }

            EnumWindowsData data;
            EnumWindows(EnumWindowsProc, reinterpret_cast<LPARAM>(&data));

            if (data.windows.empty()) {
                *outWindows = nullptr;
                *outCount = 0;
                return true;
            }

            // Allocate array for output
            auto windowArray = new WindowInfo[data.windows.size()];
            for (size_t i = 0; i < data.windows.size(); i++) {
                windowArray[i] = data.windows[i];
            }

            *outWindows = windowArray;
            *outCount = static_cast<int>(data.windows.size());
            return true;
        }
        catch (...) {
            return false;
        }
    }

    CAPTURE_API void Capture_FreeWindows(WindowInfo* windows, int count) {
        if (windows != nullptr) {
            for (int i = 0; i < count; i++) {
                if (windows[i].Title != nullptr) {
                    delete[] windows[i].Title;
                }
            }
            delete[] windows;
        }
    }

    struct EnumMonitorsData {
        std::vector<MonitorInfo> monitors;
    };

    BOOL CALLBACK EnumMonitorsProc(HMONITOR hMonitor, HDC hdcMonitor, LPRECT lprcMonitor, LPARAM dwData) {
        auto data = reinterpret_cast<EnumMonitorsData*>(dwData);

        MONITORINFOEXW monitorInfo{};
        monitorInfo.cbSize = sizeof(MONITORINFOEXW);

        if (GetMonitorInfoW(hMonitor, &monitorInfo)) {
            // Duplicate device name string
            size_t len = wcslen(monitorInfo.szDevice);
            wchar_t* deviceName = new wchar_t[len + 1];
            wcscpy_s(deviceName, len + 1, monitorInfo.szDevice);

            MonitorInfo info{};
            info.Handle = hMonitor;
            info.DeviceName = deviceName;
            info.Left = monitorInfo.rcMonitor.left;
            info.Top = monitorInfo.rcMonitor.top;
            info.Right = monitorInfo.rcMonitor.right;
            info.Bottom = monitorInfo.rcMonitor.bottom;
            info.IsPrimary = (monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;

            data->monitors.push_back(info);
        }

        return TRUE;
    }

    CAPTURE_API bool Capture_EnumerateMonitors(MonitorInfo** outMonitors, int* outCount) {
        try {
            if (outMonitors == nullptr || outCount == nullptr) {
                return false;
            }

            EnumMonitorsData data;
            EnumDisplayMonitors(nullptr, nullptr, EnumMonitorsProc, reinterpret_cast<LPARAM>(&data));

            if (data.monitors.empty()) {
                *outMonitors = nullptr;
                *outCount = 0;
                return true;
            }

            // Allocate array for output
            auto monitorArray = new MonitorInfo[data.monitors.size()];
            for (size_t i = 0; i < data.monitors.size(); i++) {
                monitorArray[i] = data.monitors[i];
            }

            *outMonitors = monitorArray;
            *outCount = static_cast<int>(data.monitors.size());
            return true;
        }
        catch (...) {
            return false;
        }
    }

    CAPTURE_API void Capture_FreeMonitors(MonitorInfo* monitors, int count) {
        if (monitors != nullptr) {
            for (int i = 0; i < count; i++) {
                if (monitors[i].DeviceName != nullptr) {
                    delete[] monitors[i].DeviceName;
                }
            }
            delete[] monitors;
        }
    }

    CAPTURE_API void Capture_FreeString(wchar_t* str) {
        if (str != nullptr) {
            delete[] str;
        }
    }
}

