# ETWSnap Artifact ZIPs

Every screen-capture session produces one canonical `.etwsnap.zip` payload. The payload is either a normal sidecar file or is copied byte-for-byte into one NTFS named stream on an ETL. An artifact ZIP never contains an ETL.

## Output modes

Screenshot-only capture:

```text
etwsnap-<start-UTC>-<id8>.etwsnap.zip
```

Traced capture:

```text
etwsnap-<start-UTC>-<id8>.etl
etwsnap-<start-UTC>-<id8>.etwsnap.zip
```

Embedded traced capture:

```powershell
etwsnap start --trace
etwsnap stop D:\Captures --embed-artifacts
```

Successful embedded output leaves only the `.etl`. Its stream is named `EtwSnap.Session.<full-session-id-without-hyphens>` and contains the same ZIP bytes that sidecar mode would publish. If stream preflight, attachment, verification, or publication fails, ETWSnap publishes the ETL and `.etwsnap.zip` sidecar pair and prints a warning.

## ZIP layout

```text
bundle.json
manifest.json
frames/*.png
EtwSnap.wprp       # when tracing was requested
```

`bundle.json` records schema version 2, full session and provider IDs, manifest schema and SHA-256, an optional ETL primary-stream SHA-256, and the path, length, and SHA-256 of every entry. Traced ZIPs require an exact ETL hash match. Screenshot-only ZIPs have no ETL hash.

Readers reject rooted or traversing paths, duplicate or unindexed entries, oversized entries, excessive expanded size, and every hash or length mismatch. PNG entries are stored without additional ZIP compression because PNG is already compressed.

## Artifact commands

Inspect a sidecar ZIP or all embedded streams in an ETL:

```powershell
etwsnap artifacts inspect <artifact.etwsnap.zip>
etwsnap artifacts inspect <trace.etl>
```

Attach a canonical ZIP to a matching ETL:

```powershell
etwsnap artifacts add <trace.etl> <artifact.etwsnap.zip>
```

The command validates provider/session identity and every frame against the target ETL. A traced ZIP must also match the ETL SHA-256. The ZIP is not modified or deleted, and an existing session stream is never overwritten.

Export selected embedded streams before removing them:

```powershell
etwsnap artifacts remove <trace.etl> --output-root <directory>
etwsnap artifacts remove <trace.etl> --session <id> --output-root <directory>
```

The output is a primary-stream-only ETL plus canonical sidecar ZIPs. One selected session produces `<etl-stem>.etwsnap.zip`; multiple sessions produce `<etl-stem>.<full-session-id>.etwsnap.zip`. ETWSnap verifies every output before deleting any selected stream.

Discard without export:

```powershell
etwsnap artifacts remove <trace.etl>
etwsnap artifacts remove <trace.etl> --session <id>
```

The prompt defaults to No. Redirected or non-interactive input requires `--force`.

## Portability

Named streams may be lost through ordinary copies, ZIP tools, cloud synchronization, email, or non-NTFS filesystems. Use `artifacts remove --output-root` before transfer when portability matters, or run `artifacts inspect` after transfer.

## WPA

WPA checks the exact embedded stream first and then canonical sibling ZIP names. It can also open a `.etwsnap.zip` directly, including screenshot-only archives. During load it extracts verified PNGs into `%LOCALAPPDATA%\EtwSnap\WpaCache`, allowing ordinary image paths and next/previous navigation in the default image viewer. Cache files are disposable and revalidated before reuse.
