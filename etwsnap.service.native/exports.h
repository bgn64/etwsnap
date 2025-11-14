#pragma once

#ifdef ETWSNAPSERVICENATIVE_EXPORTS
#define CAPTURE_API __declspec(dllexport)
#else
#define CAPTURE_API __declspec(dllimport)
#endif

// Structure representing a captured frame
struct FrameData
{
    void* PixelData;      // Pointer to BGRA8 pixel data (caller must free with Capture_FreeFrameData)
    int Width;            // Width in pixels
    int Height;           // Height in pixels
    int RowPitch;         // Bytes per row
    int64_t Timestamp;    // Milliseconds since epoch
    int FrameNumber;      // Sequential frame number
};

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
    //            maxFrames - maximum frames to store in circular buffer
    // Returns: Opaque handle to capture manager, or nullptr on failure
    CAPTURE_API void* Capture_Create(void* windowHandle, int frameIntervalMs, int maxFrames);

    // Create a capture manager for a monitor
    // Parameters: monitorHandle - HMONITOR of the monitor to capture
    //            frameIntervalMs - minimum milliseconds between frame callbacks
    //            maxFrames - maximum frames to store in circular buffer
    // Returns: Opaque handle to capture manager, or nullptr on failure
    CAPTURE_API void* Capture_CreateForMonitor(void* monitorHandle, int frameIntervalMs, int maxFrames);

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

    // Get all captured frames from the buffer
    // Parameters: captureHandle - handle returned from Capture_Create
    //            outFrames - pointer to receive array of FrameData (caller must free with Capture_FreeFrames)
    //            outCount - pointer to receive number of frames
    // Returns: true on success, false on failure
    CAPTURE_API bool Capture_GetFrames(void* captureHandle, FrameData** outFrames, int* outCount);

    // Free frame data allocated by Capture_GetFrames
    // Parameters: frames - array returned from Capture_GetFrames
    //            count - number of frames in array
    CAPTURE_API void Capture_FreeFrames(FrameData* frames, int count);

    // Enable/disable cursor capture
    // Parameters: captureHandle - handle returned from Capture_Create
    //            enabled - true to capture cursor, false to exclude it
    CAPTURE_API void Capture_SetCursorEnabled(void* captureHandle, bool enabled);

    // Get current cursor capture state
    // Parameters: captureHandle - handle returned from Capture_Create
    // Returns: true if cursor capture is enabled, false otherwise
    CAPTURE_API bool Capture_IsCursorEnabled(void* captureHandle);
}

