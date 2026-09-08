# ETWSnap

ETWSnap keeps a rolling, memory-bounded buffer of Windows Graphics Capture frames and emits an ETW event from the native frame callback for every accepted frame. When recording stops, retained frames are written as PNG files and can be correlated exactly with ETW events.

## Usage

Start a screenshot-only session on the primary monitor:

```powershell
etwsnap start
```

Native ETW events are always emitted. Without `--trace`, ETWSnap does not start WPR or create an ETL; an independently configured ETW consumer can still collect the provider.

Start screenshots and collect ETWSnap events with the bundled WPR profile:

```powershell
etwsnap start --trace
```

Compose a user profile with the bundled ETWSnap profile in the same WPR recording:

```powershell
etwsnap start --trace --profile ".\performance.wprp!Performance.Verbose"
```

The user profile is validated and passed to WPR unchanged. `--profile` requires `--trace`; ETWSnap never edits WPRP files.

Stop and choose the output root:

```powershell
etwsnap stop D:\Captures
```

The destination is reserved before capture stops. If it is invalid or unwritable, recording remains active so `stop` can be retried with another path.

Other commands:

```powershell
etwsnap status
etwsnap cancel
etwsnap targets monitors
etwsnap targets windows
etwsnap provider info
```

Capture options:

```text
--window, -w <handle>   Capture an HWND
--monitor, -m <handle> Capture an HMONITOR
--fps <1..120>         Accepted frames per second (default: 30)
--buffer-mb <MiB>      Retained BGRA payload budget (default: 500)
--cursor               Explicitly include the cursor (default)
--no-cursor            Exclude the cursor
```

Window and monitor handles may be decimal or hexadecimal with a `0x` prefix.

## Session Artifact

`stop D:\Captures` creates a session directory resembling:

```text
D:\Captures\etwsnap-20260904T142530Z-a1b2c3d4\
	frames\
		frame_00000001.png
		frame_00000002.png
	manifest.json
	trace.etl              # only with --trace
	EtwSnap.wprp           # only with --trace
```

`manifest.json` records capture settings, provider identity, WPR profile hashes, aggregate frame counts, and each saved frame's correlation metadata. A failed trace stop or failed frame export produces a partial artifact rather than deleting successful output.

## Correlation Contract

The native callback is the sole authority for frame identity and timing. For each cadence-accepted frame it:

1. Reads `Direct3D11CaptureFrame.SystemRelativeTime`.
2. Samples `QueryPerformanceCounter`.
3. Assigns a monotonic frame number.
4. Emits `FrameCaptured` from that callback.
5. Copies and retains the corresponding GPU texture.

The stable join key is `(SessionId, FrameNumber)`. Both the ETW event and manifest also carry presentation time, callback QPC, and dimensions. Event-header time remains an independent ETW trace timestamp.

Provider:

```text
Name: ETWSnap-Service
GUID: 524507bc-3009-5e8d-c071-00a1c641849f
```

The native capture path deliberately retains `robmikh.common` 0.0.23-beta, Windows Graphics Capture, and D3D11.

## Architecture

```text
EtwSnap.Cli       C#/.NET 10 command parsing, host startup, IPC client
EtwSnap.Contracts C#/.NET 10 versioned length-prefixed JSON contracts
EtwSnap.Host      C#/.NET 10 session state, WPR, targets, PNGs, manifests
EtwSnap.Native    C++20 WGC/D3D11 capture, GPU ring, ETW correlation
```

The host is an on-demand per-user process, not a Windows Service. Its mutex and named pipe are scoped by the current user's SID, and the pipe uses current-user-only access. It exits after five idle minutes.

Frames remain in GPU memory during capture. The ring evicts oldest textures according to actual logical BGRA bytes, preserving frame numbers and reporting evictions. A host crash loses retained frames by design; local recovery state is used to detect the abandoned session and clean up only ETWSnap's named WPR instance.

## Requirements

- x64 Windows 10 or later with Windows Graphics Capture support
- .NET 10 SDK/runtime
- Visual Studio C++ build tools with C++20 and a Windows SDK
- Windows Performance Recorder for `--trace`
- An elevated terminal when WPR requires administrator access

## Build

From a Visual Studio Developer PowerShell with the C++ workload installed:

```powershell
msbuild .\src\EtwSnap.Cli\EtwSnap.Cli.csproj `
	/restore /t:Rebuild `
	/p:Configuration=Release /p:Platform=x64
```

The packaged executable is written under `src\EtwSnap.Cli\bin\x64\Release\net10.0-windows`.

Build mixed-language tests with Visual Studio MSBuild, then execute them without rebuilding through the .NET SDK:

```powershell
msbuild .\tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj `
	/restore /t:Build /p:Configuration=Release /p:Platform=x64
msbuild .\tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj `
	/restore /t:Build /p:Configuration=Release /p:Platform=x64

dotnet test .\tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj `
	-c Release -p:Platform=x64 --no-build
dotnet test .\tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj `
	-c Release -p:Platform=x64 --no-build
```

Integration tests require an interactive Windows desktop. They cover real native capture, PNG/manifest persistence, concurrent IPC, and ETL-to-frame correlation.

For managed-only development on a machine without the C++ workload:

```powershell
dotnet build .\src\EtwSnap.Cli\EtwSnap.Cli.csproj -c Debug -p:SkipNativeBuild=true
dotnet test .\tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj -p:SkipNativeBuild=true
dotnet test .\tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj -p:SkipNativeBuild=true
```

Managed-only builds cannot run `start`; the host reports a clear capture error until `EtwSnap.Native.dll` is built and deployed.