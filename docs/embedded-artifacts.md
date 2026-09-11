# Embedded ETWSnap Artifacts

Embedded artifacts are an opt-in convenience for keeping an ETWSnap trace and its screenshots together. ETWSnap attaches a standard ZIP bundle to an unchanged ETL by using an NTFS named stream. The ETL primary stream remains readable by WPR, WPA, XPerf, and other ETL consumers.

## Capture and fallback

Start a traced session and request embedded output when stopping:

```powershell
etwsnap start --trace
etwsnap stop D:\Captures --embed-artifacts
```

Successful output resembles:

```text
D:\Captures\etwsnap-20260904T142530Z-a1b2c3d4.etl
```

The stream name is `EtwSnap.Session.<full-session-id-without-hyphens>`. ETWSnap stages the trace and screenshots on the output volume, verifies the bundle before and after attaching it, and publishes the ETL by a same-volume rename.

`--embed-artifacts` requires a session started with `--trace`. Folder output remains the default. If stream preflight, packaging, attachment, verification, or publication fails, ETWSnap removes any partial stream, publishes the canonical session folder, and prints a warning. A destination failure before capture stops leaves recording active for retry.

## Portability

NTFS named streams are Windows filesystem metadata. Many copy programs, ZIP tools, cloud-sync clients, email systems, network filesystems, and upload services preserve only the ETL primary stream. The resulting ETL may still open normally while its screenshots are gone.

Run this after every transfer where artifact preservation matters:

```powershell
etwsnap artifacts inspect .\trace.etl
```

To create portable ordinary files before transfer:

```powershell
etwsnap artifacts remove .\trace.etl --output-root .\portable
```

This verifies all selected bundles, extracts them, verifies the complete output, and only then removes their streams from the source ETL.

## Artifact commands

Inspect every ETWSnap stream and fully verify its bundle:

```powershell
etwsnap artifacts inspect <trace.etl>
```

No streams is a successful `none` result. Any invalid ETWSnap stream returns an operation failure and includes its reason.

Attach a completed traditional session folder to a matching ETL:

```powershell
etwsnap artifacts add <trace.etl> <session-folder>
```

The command validates the folder manifest, provider, frames, and matching session events in the target ETL. It never changes or deletes the source folder and never overwrites an existing session stream.

Extract selected artifacts and then remove their streams:

```powershell
etwsnap artifacts remove <trace.etl> --output-root <directory>
etwsnap artifacts remove <trace.etl> --session <id> --output-root <directory>
```

One selected session produces a traditional session folder containing a primary-stream-only ETL, manifest, frames, and profile. Multiple sessions produce one ETL plus `sessions/<full-id>/...` directories. Existing destinations are not overwritten. If any selected stream is invalid or extraction fails, no stream is removed.

Discard artifacts without extraction:

```powershell
etwsnap artifacts remove <trace.etl>
etwsnap artifacts remove <trace.etl> --session <id>
```

The interactive prompt lists every selected session, validity, frame count when known, and stream size. It defaults to No. Redirected or non-interactive input requires `--force`. The force option bypasses only confirmation; it does not weaken path or session selection.

## Bundle contract v1

Each session stream contains an ordinary ZIP archive with:

```text
bundle.json
manifest.json
frames/*.png
EtwSnap.wprp       # when available
```

`bundle.json` records schema version 1, full session and provider IDs, source manifest schema and SHA-256, ETL primary-stream SHA-256, and the path, length, and SHA-256 of every archive entry. Entry names are relative `/`-separated paths. Readers reject roots, drive or stream syntax, empty or traversal segments, duplicates, unindexed entries, oversized entries, excessive expanded size, and all hash or length mismatches.

The ETL hash binds artifacts to the exact primary stream. The manifest hash remains independently comparable with ETWSnap's `ArtifactCommitted` event when an external ETW session records that event.

## WPA

The ETWSnap WPA plugin checks the exact session stream before folder candidates. It validates metadata while loading but leaves PNGs embedded. Opening or revealing a screenshot materializes only that PNG into `%LOCALAPPDATA%\EtwSnap\WpaCache`, using a temporary file, length and hash verification, and atomic rename. Cache files are revalidated and may be deleted at any time.
