# ETWSnap WPA Plugin

`EtwSnap.WpaPlugin` provides independent Microsoft Performance Toolkit SDK processing sources for ETL files and `.etwsnap.zip` artifacts. It uses only public SDK and TraceEvent APIs and can coexist with XPerf while parsing the same ETL independently.

## Tables

`ETWSnap Screenshots` exposes every `FrameCaptured` event with ETW event-header time assigned to the WPA `StartTime` role. Duration extends to the next frame in the same session, or to `RecordingStopped` for the final frame.

Configurations:

- `Saved Screenshots` filters to manifest-backed PNGs that currently exist.
- `All Frames` includes saved, missing, evicted/not-persisted, and unresolved frames.

`All Frames` is the default so traces without accessible artifacts still show every ETW frame.

Right-click exactly one saved row to use:

- `Open in Default Viewer`, which opens the PNG through the user's default image handler.
- `Reveal in File Explorer`, which opens Explorer with that PNG selected.

Both commands revalidate that the PNG still exists when invoked. They safely do nothing for non-persisted frames, missing files, invalid rows, or multiple selected rows. SDK `1.2.2-preview` does not expose selection-aware command enablement, so the commands remain visible in the `All Frames` configuration; use `Saved Screenshots` to hide non-persisted rows entirely.

`ETWSnap Sessions` exposes lifecycle settings, frame statistics, artifact resolution state, manifest path, and integrity diagnostics. Both tables assign public `StartTime` and `Duration` column roles so they can share selection and zoom with other tables in the same WPA Analysis tab.

The first plugin version is table-only. It does not provide thumbnails, image preview, custom docking, or a preset WPA layout.

## Artifact discovery

For each `(ETL source path, full session ID)`, the plugin first checks the deterministic named stream `EtwSnap.Session.<session-id:N>`. A present stream is fully verified against the ETL primary-stream hash, bundle index, manifest, provider identity, and ETW frame metadata. A valid embedded bundle takes precedence over sidecars. An invalid stream is reported and is never hidden by a sidecar.

When the exact stream is absent, the plugin checks only these sibling candidates:

1. `<etl-stem>.etwsnap.zip`
2. `<etl-stem>.<full-session-id>.etwsnap.zip`

The plugin never recursively scans directories or guesses from frame filenames. It can also open `.etwsnap.zip` directly. Traced ZIPs require their matching sibling ETL; screenshot-only ZIPs build a timeline from manifest QPC metadata.

A manifest must have a supported schema, matching full session ID and provider GUID, unique frame numbers, contained relative image paths, matching ETW frame metadata, and a matching committed SHA-256 when available. Artifact failures do not hide ETW rows.

Artifact PNGs are materialized while WPA loads the source. Each Image Path is a normal file under `%LOCALAPPDATA%\EtwSnap\WpaCache\<source-hash>\<session-id>`, so default image viewers can navigate between adjacent frames. The source hash is the ETL hash for traced artifacts and ZIP hash for screenshot-only artifacts. Files are written atomically and verified by length and SHA-256; valid cached files are reused. Cleanup is bounded to 2 GiB and 30 days, and deleting the cache is always safe.

Traced sidecar layout:

```text
capture.etl
capture.etwsnap.zip
```

Exported multi-session layout:

```text
capture.etl
capture.<full-session-id>.etwsnap.zip
```

## Build and compatibility

The project currently targets `net8.0-windows`, Microsoft.Performance.SDK `1.2.2-preview`, and TraceEvent `3.1.21`. SDK `1.2.*-preview` supports WPA `11.7.240.51934` and later. Manual UI validation passed on WPA `11.9.89.56208` with hosted SDK `1.4.9-preview2`.

```powershell
dotnet build .\src\EtwSnap.WpaPlugin\EtwSnap.WpaPlugin.csproj -c Release
dotnet test .\tests\EtwSnap.WpaPlugin.Tests\EtwSnap.WpaPlugin.Tests.csproj -c Release
```

Launch the loose development plugin without installing or packaging it:

```powershell
.\eng\Launch-WpaPlugin.ps1 -TracePath .\capture.etl
.\eng\Launch-WpaPlugin.ps1 -TracePath .\capture.etwsnap.zip
```

The launcher deliberately keeps WPA's default processing sources enabled so ETWSnap and XPerf tables are available together. Use `-NoDefault` only when isolating plugin-load failures.

The launcher prefers `wpa.exe` from `PATH` and falls back to the standard ADK location. On the validation machine, `C:\xperf\wpa.exe` loaded the plugin while the older ADK copy could not enumerate loose managed plugins because its host dependency manifest was missing. Pass `-WpaPath` to select a specific installation.

For a smoke capture, validate:

1. `Help > About Windows Performance Analyzer` includes an ETWSnap processing-source tab.
2. Graph Explorer contains `ETWSnap Screenshots` and `ETWSnap Sessions`.
3. Sessions shows the captured session ID and frame statistics.
4. Screenshots shows the expected rows and normal cache paths under `%LOCALAPPDATA%\EtwSnap\WpaCache`.
5. An ETWSnap graph and an XPerf graph in the same Analysis tab follow the same zoom and highlighted time range.

If the plugin does not appear, open `Window > Diagnostic Console` and capture the complete load error, then retry with `-NoDefault` to distinguish plugin loading from interaction with default processing sources.

`eng\Package-WpaPlugin.ps1` installs the published `Microsoft.Performance.Toolkit.Plugins.Cli` tool at pinned version `0.1.77-preview` into the ignored short path `artifacts\.pt`, validates the generated package metadata, and produces the `.ptix`, SHA-256, and symbols archive. The temporary tool directory is removed afterward. WPA does not install `plugintool`; it is build-time packaging infrastructure. Use `-PluginToolNuGetSource` only when the configured NuGet source needs to be overridden.

The output includes the plugin's third-party TraceEvent dependencies but deliberately excludes `Microsoft.Performance.SDK.dll`; WPA supplies that shared runtime and rejects packaged plugins that carry their own copy.