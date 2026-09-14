# ETWSnap WPA Plugin

`EtwSnap.WpaPlugin` provides independent Microsoft Performance Toolkit SDK processing sources for ETL files and `.etwsnap.zip` artifacts. It uses only public SDK and TraceEvent APIs and can coexist with XPerf while parsing the same ETL independently.

## Tables

`ETWSnap Screenshots` exposes every `FrameCaptured` event as a point at its compositor presentation time. Selecting a screenshot represents that instant, not a range extending to the next frame. Screenshot configurations bind only the `StartTime` role and do not expose an inferred duration.

Configurations:

- `Saved Screenshots` filters to manifest-backed PNGs that currently exist.
- `All Frames` includes saved, missing, evicted/not-persisted, and unresolved frames.

`All Frames` is the default so traces without accessible artifacts still show every ETW frame.

Right-click exactly one saved row to use:

- `Open in Default Viewer`, which opens the PNG through the user's default image handler.
- `Reveal in File Explorer`, which opens Explorer with that PNG selected.

Both commands revalidate that the PNG still exists when invoked. They safely do nothing for non-persisted frames, missing files, invalid rows, or multiple selected rows. SDK `1.2.2-preview` does not expose selection-aware command enablement, so the commands remain visible in the `All Frames` configuration; use `Saved Screenshots` to hide non-persisted rows entirely.

`ETWSnap Sessions` exposes lifecycle settings, frame statistics, artifact resolution state, manifest path, and integrity diagnostics. Sessions plot `Start Time` and `End Time` as interval bars, with public `StartTime`, `EndTime`, and `Duration` roles for range selection. Missing lifecycle events use the available capture data to estimate session bounds.

Every data column in both tables has a short tooltip describing its meaning. Time columns use SDK timestamp types so WPA can change their displayed units. Both tables can share selection and zoom with other tables in the same WPA Analysis tab.

The first plugin version is table-only. It does not provide thumbnails, image preview, custom docking, or a preset WPA layout.

## Artifact discovery

For each `(ETL source path, full session ID)`, the plugin first checks the deterministic named stream `EtwSnap.Session.<session-id:N>`. A present stream is fully verified against the ETL primary-stream hash, bundle index, manifest, provider identity, and ETW frame metadata. A valid embedded bundle takes precedence over sidecars. An invalid stream is reported and is never hidden by a sidecar.

When the exact stream is absent, the plugin checks only these sibling candidates:

1. `<etl-stem>.etwsnap.zip`
2. `<etl-stem>-1.etwsnap.zip`, `<etl-stem>-2.etwsnap.zip`, and other canonical positive integer suffixes

The plugin searches only the ETL's own directory, never subdirectories. Numbered candidates are sorted numerically and mapped to sessions by their internal full session ID. The plugin can also open `.etwsnap.zip` directly. Traced ZIPs require their matching sibling ETL. Direct ZIP timelines use manifest presentation timestamps, with the earliest retained frame at time zero; open the ETL for alignment with other ETW providers. In ETL views, screenshot start times use WGC compositor time mapped to the trace clock, not ETW event-write time.

A manifest must have a supported schema, matching full session ID and provider GUID, unique frame numbers, contained relative image paths, matching ETW frame metadata, and a matching committed SHA-256 when available. Artifact failures do not hide ETW rows.

Open `Window > Diagnostic Console` in WPA to inspect artifact discovery. The plugin emits one resolution trace per source and session, including the exact embedded stream and sibling ZIP candidates checked, reasons candidates were skipped or rejected, the selected artifact and frame count, and the final resolution state.

Artifact PNGs are materialized while WPA loads the source. Each Image Path is a normal file under `%LOCALAPPDATA%\EtwSnap\WpaCache\<source-hash>\<session-id>`, so default image viewers can navigate between adjacent frames. The source hash is the ETL hash for traced artifacts and ZIP hash for screenshot-only artifacts. Files are written atomically and verified by length and SHA-256; valid cached files are reused. Cleanup is bounded to 2 GiB and 30 days, and deleting the cache is always safe.

Traced sidecar layout:

```text
capture.etl
capture.etwsnap.zip
```

Exported multi-session layout:

```text
capture.etl
capture-1.etwsnap.zip
capture-2.etwsnap.zip
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
5. Selecting a screenshot emphasizes its timestamp without extending a range to the next screenshot; selecting a session highlights its start-to-end bar.
6. Column-header tooltips describe the data, and time columns offer unit formatting.
7. An ETWSnap graph and an XPerf graph in the same Analysis tab follow the same zoom and time selection.

If the plugin does not appear, open `Window > Diagnostic Console` and capture the complete load error, then retry with `-NoDefault` to distinguish plugin loading from interaction with default processing sources.

`eng\Package-WpaPlugin.ps1` installs the published `Microsoft.Performance.Toolkit.Plugins.Cli` tool at pinned version `0.1.77-preview` into the ignored short path `artifacts\.pt`, validates the generated package metadata, and produces the `.ptix`, SHA-256, and symbols archive. The temporary tool directory is removed afterward. WPA does not install `plugintool`; it is build-time packaging infrastructure. Use `-PluginToolNuGetSource` only when the configured NuGet source needs to be overridden.

The output includes the plugin's third-party TraceEvent dependencies but deliberately excludes `Microsoft.Performance.SDK.dll`; WPA supplies that shared runtime and rejects packaged plugins that carry their own copy.