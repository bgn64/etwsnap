// pch.h: This is a precompiled header file.
// Files listed below are compiled only once, improving build performance for future builds.
// This also affects IntelliSense performance, including code completion and many code browsing features.
// However, files listed here are ALL re-compiled if any one of them is updated between builds.
// Do not add files here that you will be updating frequently as this negates the performance advantage.

#ifndef PCH_H
#define PCH_H

// add headers that you want to pre-compile here
#include "framework.h"

// Windows Runtime includes
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.System.h>

// Direct3D includes
#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.graphics.directx.direct3d11.interop.h>

// Capture interop
#include <Windows.Graphics.Capture.Interop.h>

// robmikh.common helpers
#include <robmikh.common/direct3d11.interop.h>
#include <robmikh.common/d3d11Helpers.h>
#include <robmikh.common/capture.desktop.interop.h>

// Standard library
#include <atomic>
#include <memory>

#include "exports.h"

#endif //PCH_H
