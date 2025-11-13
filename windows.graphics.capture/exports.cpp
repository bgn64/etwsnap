#include "pch.h"
#include "exports.h"
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
}
