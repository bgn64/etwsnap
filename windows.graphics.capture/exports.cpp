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

    CAPTURE_API void* Capture_Create(void* windowHandle, int frameIntervalMs) {
        try {
            if (windowHandle == nullptr || frameIntervalMs <= 0) {
                return nullptr;
            }

            auto manager = new CaptureManager(static_cast<HWND>(windowHandle), frameIntervalMs);
            return static_cast<void*>(manager);
        }
        catch (...) {
            return nullptr;
        }
    }

    CAPTURE_API void* Capture_CreateForMonitor(void* monitorHandle, int frameIntervalMs) {
        try {
            if (monitorHandle == nullptr || frameIntervalMs <= 0) {
                return nullptr;
            }

            auto manager = new CaptureManager(static_cast<HMONITOR>(monitorHandle), frameIntervalMs);
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

    CAPTURE_API void Capture_SetFrameCallback(void* captureHandle, FrameArrivedCallback callback, void* userContext) {
        try {
            if (captureHandle != nullptr) {
                auto manager = static_cast<CaptureManager*>(captureHandle);
                manager->SetFrameCallback(callback, userContext);
            }
        }
        catch (...) {
            // Ignore errors
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

