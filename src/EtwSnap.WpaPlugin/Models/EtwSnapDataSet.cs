using EtwSnap.WpaPlugin.Artifacts;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;

namespace EtwSnap.WpaPlugin.Models;

public enum ScreenshotAvailability
{
    Saved,
    MissingFile,
    NotPersisted,
    ArtifactUnavailable,
}

public sealed record ScreenshotRecord(
    Timestamp StartTime,
    TimestampDelta Duration,
    Guid SessionId,
    ulong FrameNumber,
    ScreenshotAvailability Availability,
    string? ImagePath,
    uint Width,
    uint Height,
    uint PixelFormat,
    long PresentationTime100ns,
    long CallbackQpc,
    Func<string>? MaterializeImage = null);

public sealed record SessionRecord(
    Timestamp StartTime,
    TimestampDelta Duration,
    Guid SessionId,
    uint? FramesPerSecond,
    ulong? BufferBytes,
    uint? TargetKind,
    ulong? TargetHandle,
    ulong? AcceptedFrames,
    ulong? RetainedFrames,
    ulong? EvictedFrames,
    ulong? DroppedFrames,
    ulong? ErrorCount,
    ulong? ExportedFrames,
    ulong? FailedFrames,
    ArtifactResolutionState ArtifactState,
    string? ManifestPath,
    string? ArtifactDetail);

public sealed record EtwSnapDataSet(
    IReadOnlyList<ScreenshotRecord> Screenshots,
    IReadOnlyList<SessionRecord> Sessions)
{
    public static EtwSnapDataSet Build(IEnumerable<EtwSnapEvent> sourceEvents, ArtifactResolver? resolver = null)
    {
        resolver ??= new ArtifactResolver();
        var screenshots = new List<ScreenshotRecord>();
        var sessions = new List<SessionRecord>();

        foreach (var group in sourceEvents.GroupBy(item => new { item.SessionId, item.SourcePath }))
        {
            var events = group.OrderBy(item => item.Timestamp.ToNanoseconds).ToArray();
            var frames = events.OfType<FrameCapturedEvent>().OrderBy(frame => frame.Timestamp.ToNanoseconds).ToArray();
            var started = events.OfType<RecordingStartedEvent>().FirstOrDefault();
            var stopped = events.OfType<RecordingStoppedEvent>().LastOrDefault();
            var committed = events.OfType<ArtifactCommittedEvent>().LastOrDefault();
            var resolution = resolver.Resolve(group.Key.SessionId, events);
            var sessionStart = started?.Timestamp ?? frames.FirstOrDefault()?.Timestamp ?? events.First().Timestamp;
            var sessionStop = stopped?.Timestamp ?? frames.LastOrDefault()?.Timestamp ?? events.Last().Timestamp;

            for (var index = 0; index < frames.Length; ++index)
            {
                var frame = frames[index];
                var stop = index + 1 < frames.Length ? frames[index + 1].Timestamp : sessionStop;
                var durationNanoseconds = Math.Max(0, stop.ToNanoseconds - frame.Timestamp.ToNanoseconds);
                var hasFileFrame = resolution.FramePaths.TryGetValue(frame.FrameNumber, out var imagePath);
                EmbeddedFrameReference? embeddedFrame = null;
                var hasEmbeddedFrame = resolution.EmbeddedFrames?.TryGetValue(frame.FrameNumber, out embeddedFrame) == true;
                var hasManifestFrame = hasFileFrame || hasEmbeddedFrame;
                screenshots.Add(new ScreenshotRecord(
                    frame.Timestamp,
                    TimestampDelta.FromNanoseconds(durationNanoseconds),
                    group.Key.SessionId,
                    frame.FrameNumber,
                    hasManifestFrame
                        ? ScreenshotAvailability.Saved
                        : resolution.State == ArtifactResolutionState.Resolved
                            ? resolution.ManifestFrameNumbers.Contains(frame.FrameNumber)
                                ? ScreenshotAvailability.MissingFile
                                : ScreenshotAvailability.NotPersisted
                            : ScreenshotAvailability.ArtifactUnavailable,
                    hasFileFrame ? imagePath : hasEmbeddedFrame ? embeddedFrame!.DisplayPath : null,
                    frame.Width,
                    frame.Height,
                    frame.PixelFormat,
                    frame.PresentationTime100ns,
                    frame.CallbackQpc,
                    hasEmbeddedFrame ? embeddedFrame!.Materialize : null));
            }

            sessions.Add(new SessionRecord(
                sessionStart,
                TimestampDelta.FromNanoseconds(Math.Max(0, sessionStop.ToNanoseconds - sessionStart.ToNanoseconds)),
                group.Key.SessionId,
                started?.FramesPerSecond,
                started?.BufferBytes,
                started?.TargetKind,
                started?.TargetHandle,
                stopped?.AcceptedFrames ?? resolution.Statistics?.AcceptedFrames,
                stopped?.RetainedFrames ?? resolution.Statistics?.RetainedFrames,
                stopped?.EvictedFrames ?? resolution.Statistics?.EvictedFrames,
                stopped?.DroppedFrames ?? resolution.Statistics?.DroppedFrames,
                stopped?.ErrorCount ?? resolution.Statistics?.ErrorCount,
                committed?.ExportedFrames ?? resolution.Statistics?.ExportedFrames,
                committed?.FailedFrames ?? resolution.Statistics?.FailedFrames,
                resolution.State,
                resolution.ManifestPath,
                resolution.Detail));
        }

        return new EtwSnapDataSet(screenshots, sessions);
    }
}