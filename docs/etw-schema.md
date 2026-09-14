# ETWSnap ETW Contract

Provider name: `ETWSnap-Service`

Provider GUID: `524507bc-3009-5e8d-c071-00a1c641849f`

Frame identity is always the full `(SessionId, FrameNumber)` pair. Consumers must not join frames by filename, frame number alone, or the abbreviated session ID used in directory names.

## Lifecycle and frame events

- `RecordingStarted`: session ID, QPC frequency, requested FPS and buffer size, target kind, and target handle.
- `FrameCaptured`: session ID, frame number, WGC presentation time, callback QPC, dimensions, and pixel format.
- `FrameCaptureError`: session ID, frame number, and HRESULT.
- `RecordingStopped`: session ID and accepted, retained, evicted, dropped, and error counts.

The ETW event-header timestamp records event emission. WPA places screenshot rows at `PresentationTime100ns`, the WGC compositor timestamp, mapped to trace-relative nanoseconds using the ETL's QPC frequency and an event QPC/time anchor. `CallbackQpc` remains diagnostic metadata; callback and event-write delays do not determine screenshot placement. Compositor time is not a guarantee of physical display scanout time.

When an artifact ZIP is opened directly, screenshot times are relative to the earliest retained presentation timestamp. Open the matching ETL to align screenshots with other ETW providers.

## Artifact events

Artifact event contract version 2 describes the canonical artifact ZIP payload.

`ArtifactReference` is emitted after output reservation and before native capture and ETWSnap-managed WPR stop. It is therefore present in a normal ETWSnap-managed trace.

Fields:

- `ContractVersion`
- `SessionId`
- `ArtifactPath` (prospective sidecar path or ETL named-stream locator)
- `ArtifactFileName` (canonical `.etwsnap.zip` filename)
- `ManifestSchemaVersion`
- `BundleSchemaVersion`
- `RequestedTransport` (`Sidecar` or `Embedded`)

`ArtifactCommitted` is emitted after `manifest.json` is atomically finalized. ETWSnap-managed WPR has already stopped at this point, so this event normally appears only in an independently running external trace.

It repeats the reference fields and adds:

- `ManifestSha256`, lowercase hexadecimal over the exact committed bytes
- `ArtifactSha256`, lowercase hexadecimal over the exact canonical ZIP bytes
- `ActualTransport` (`Sidecar` or `Embedded`)
- `Status`
- accepted, retained, evicted, dropped, native-error, exported, and failed frame counts

Artifact event emission is best-effort. A provider failure does not interrupt capture shutdown or invalidate a successfully committed artifact.