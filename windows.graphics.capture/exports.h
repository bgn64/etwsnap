#pragma once

#ifdef WINDOWSGRAPHICSCAPTURE_EXPORTS
#define CAPTURE_API __declspec(dllexport)
#else
#define CAPTURE_API __declspec(dllimport)
#endif

extern "C" {
    // Simple arithmetic function
    CAPTURE_API int Add(int a, int b);
    
    // String manipulation function
    CAPTURE_API void PrintMessage(const char* message);
    
    // Function that returns a value through pointer
    CAPTURE_API bool Multiply(int a, int b, int* result);
}
