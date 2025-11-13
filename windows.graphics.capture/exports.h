#pragma once

#ifdef WINDOWSGRAPHICSCAPTURE_EXPORTS
#define CAPTURE_API __declspec(dllexport)
#else
#define CAPTURE_API __declspec(dllimport)
#endif

// Callback function pointer type for frame events
// Parameters: width, height, timestamp (milliseconds since epoch), user context
typedef void(*FrameArrivedCallback)(int width, int height, int64_t timestamp, void* userContext);

extern "C" {
    // Simple arithmetic function
    CAPTURE_API int Add(int a, int b);
    
    // String manipulation function
    CAPTURE_API void PrintMessage(const char* message);
    
    // Function that returns a value through pointer
    CAPTURE_API bool Multiply(int a, int b, int* result);

    // ===== Screen Capture API =====
    
    // Create a capture manager for a specific window
    // Parameters: windowHandle - HWND of the window to capture
    //            frameIntervalMs - minimum milliseconds between frame callbacks
    // Returns: Opaque handle to capture manager, or nullptr on failure
    CAPTURE_API void* Capture_Create(void* windowHandle, int frameIntervalMs);

    // Start capturing frames
    // Parameters: captureHandle - handle returned from Capture_Create
    // Returns: true on success, false on failure
    CAPTURE_API bool Capture_Start(void* captureHandle);

    // Stop capturing frames
    // Parameters: captureHandle - handle returned from Capture_Create
    CAPTURE_API void Capture_Stop(void* captureHandle);

    // Destroy capture manager and free resources
    // Parameters: captureHandle - handle returned from Capture_Create
    CAPTURE_API void Capture_Destroy(void* captureHandle);

    // Set callback function to receive frame events
    // Parameters: captureHandle - handle returned from Capture_Create
    //            callback - function pointer to call on each frame
    //            userContext - user-defined pointer passed to callback
    CAPTURE_API void Capture_SetFrameCallback(void* captureHandle, FrameArrivedCallback callback, void* userContext);

    // Enable/disable cursor capture
    // Parameters: captureHandle - handle returned from Capture_Create
    //            enabled - true to capture cursor, false to exclude it
    CAPTURE_API void Capture_SetCursorEnabled(void* captureHandle, bool enabled);

    // Get current cursor capture state
    // Parameters: captureHandle - handle returned from Capture_Create
    // Returns: true if cursor capture is enabled, false otherwise
    CAPTURE_API bool Capture_IsCursorEnabled(void* captureHandle);
}

