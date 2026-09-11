# ETWSnap

ETWSnap keeps a rolling, memory-bounded buffer of Windows Graphics Capture frames and emits an ETW event from the native frame callback for every accepted frame. When recording stops, retained frames are written as PNG files and can be correlated exactly with ETW events.

## Install

ETWSnap is distributed as an unsigned, framework-dependent x64 Windows package. Install the .NET 10 Runtime first if `dotnet --list-runtimes` does not show `Microsoft.NETCore.App 10.x`:

```powershell
winget install Microsoft.DotNet.Runtime.10
```

Scoop is the recommended installation method:

```powershell
scoop bucket add etwsnap https://github.com/bgn64/etwsnap
scoop install etwsnap
```

Update later with:

```powershell
scoop update
scoop update etwsnap
```

Alternatively, download `etwsnap-vX.Y.Z-win-x64.zip` from the GitHub Release, verify its adjacent SHA-256 file, extract the complete archive, and add that directory to `PATH`. Do not copy only `etwsnap.exe`; the host, native DLL, profiles, and managed dependencies are also required.

Release ZIPs are currently not Authenticode-signed. GitHub build provenance can be verified with:

```powershell
gh attestation verify .\etwsnap-vX.Y.Z-win-x64.zip --repo bgn64/etwsnap
```

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

Before WPR starts, ETWSnap stages byte-for-byte profile copies under `%LOCALAPPDATA%\EtwSnap\Wpr` so package-manager junctions and symbolic links are never passed to WPR. Staged files are removed after stop, cancel, failure, or recovery.

Stop and choose the output root:

```powershell
etwsnap stop D:\Captures
```

The destination is reserved before capture stops. If it is invalid or unwritable, recording remains active so `stop` can be retried with another path.

For a session started with `--trace`, artifacts can instead be attached to a standalone ETL on NTFS:

```powershell
etwsnap stop D:\Captures --embed-artifacts
```

The ETL primary bytes remain an ordinary ETL. If embedded publication is unsupported or fails, ETWSnap saves the normal session folder and prints a prominent warning. Folder output remains the default.

Inspect, add, extract, or remove embedded artifacts without starting the capture host:

```powershell
etwsnap artifacts inspect D:\Captures\trace.etl
etwsnap artifacts add D:\Captures\trace.etl D:\Captures\etwsnap-session
etwsnap artifacts remove D:\Captures\trace.etl --output-root D:\Extracted
etwsnap artifacts remove D:\Captures\trace.etl --session <session-id> --force
```

See [docs/embedded-artifacts.md](docs/embedded-artifacts.md) before copying or deleting embedded artifacts; ordinary copy, archive, and upload tools may discard NTFS named streams.

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

ETWSnap also emits versioned artifact-discovery events. `ArtifactReference` is written before ETWSnap stops WPR and identifies the reserved output directory. `ArtifactCommitted` is written after manifest finalization and includes its SHA-256, so it is normally visible only to an external ETW session that continues recording. The complete event contract is documented in [docs/etw-schema.md](docs/etw-schema.md).

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
EtwSnap.Artifacts C#/.NET 8 named-stream, ZIP, validation, and extraction core
EtwSnap.Contracts C#/.NET 10 versioned length-prefixed JSON contracts
EtwSnap.Host      C#/.NET 10 session state, WPR, targets, PNGs, manifests
EtwSnap.Native    C++20 WGC/D3D11 capture, GPU ring, ETW correlation
EtwSnap.WpaPlugin Public Performance Toolkit SDK processor and WPA tables
```

The host is an on-demand per-user process, not a Windows Service. Its mutex and named pipe are scoped by the current user's SID, and the pipe uses current-user-only access. It exits after five idle minutes.

Frames remain in GPU memory during capture. The ring evicts oldest textures according to actual logical BGRA bytes, preserving frame numbers and reporting evictions. A host crash loses retained frames by design; local recovery state is used to detect the abandoned session and clean up only ETWSnap's named WPR instance.

## Requirements

- x64 Windows 10 or later with Windows Graphics Capture support
- .NET 10 Runtime for using ETWSnap
- .NET 10 SDK for building ETWSnap
- .NET 8 SDK targeting support for building the WPA plugin
- Visual Studio C++ build tools with C++20 and a Windows SDK
- Windows Performance Recorder for `--trace`
- An elevated terminal when WPR requires administrator access

## Build

From a Visual Studio Developer PowerShell with the C++ workload installed:

```powershell
.\eng\Build.ps1 -Version 0.1.0 -IncludeInteractiveTests
.\eng\Package.ps1 -Version 0.1.0 -SkipBuild
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
dotnet test .\tests\EtwSnap.WpaPlugin.Tests\EtwSnap.WpaPlugin.Tests.csproj `
	-c Release --no-build
```

Integration tests require an interactive Windows desktop. They cover real native capture, PNG/manifest persistence, concurrent IPC, and ETL-to-frame correlation.

For managed-only development on a machine without the C++ workload:

```powershell
dotnet build .\src\EtwSnap.Cli\EtwSnap.Cli.csproj -c Debug -p:SkipNativeBuild=true
dotnet test .\tests\EtwSnap.UnitTests\EtwSnap.UnitTests.csproj -p:SkipNativeBuild=true
dotnet test .\tests\EtwSnap.IntegrationTests\EtwSnap.IntegrationTests.csproj -p:SkipNativeBuild=true
```

Managed-only builds cannot run `start`; the host reports a clear capture error until `EtwSnap.Native.dll` is built and deployed.

The WPA plugin source, table behavior, and portable multi-session layout are documented in [docs/wpa-plugin.md](docs/wpa-plugin.md). The release workflow publishes the validated plugin as a separate `.ptix` asset with its own SHA-256 and symbols archive.

For local WPA development, load the Release plugin directly from its build directory:

```powershell
.\eng\Launch-WpaPlugin.ps1 -TracePath <trace.etl>
```

Release maintainer instructions are in [docs/releasing.md](docs/releasing.md).