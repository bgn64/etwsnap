using EtwSnap.Artifacts;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility.SourceParsing;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin.Parsing;

public sealed class EtwSnapTraceParser : SourceParser<EtwSnapEvent, EtwSnapParsingContext, Type>
{
    public static readonly Guid ProviderId = new("524507bc-3009-5e8d-c071-00a1c641849f");

    private readonly IReadOnlyList<FileDataSource> _dataSources;
    private readonly EtwSnapParsingContext _context = new();
    private DataSourceInfo? _dataSourceInfo;

    public EtwSnapTraceParser(IEnumerable<IDataSource> dataSources)
    {
        _dataSources = dataSources.OfType<FileDataSource>().ToArray();
    }

    public override string Id => nameof(EtwSnapTraceParser);
    public override DataSourceInfo? DataSourceInfo => _dataSourceInfo;

    public override void ProcessSource(
        ISourceDataProcessor<EtwSnapEvent, EtwSnapParsingContext, Type> dataProcessor,
        ILogger logger,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        long? firstTimestampNanoseconds = null;
        long? lastTimestampNanoseconds = null;
        DateTime? sessionStartUtc = null;

        for (var index = 0; index < _dataSources.Count; ++index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = _dataSources[index].FullPath;
            if (sourcePath.EndsWith(EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                var range = ProcessArchive(sourcePath, dataProcessor, cancellationToken);
                sessionStartUtc ??= range.SessionStartUtc;
                firstTimestampNanoseconds = firstTimestampNanoseconds is null
                    ? range.FirstNanoseconds
                    : Math.Min(firstTimestampNanoseconds.Value, range.FirstNanoseconds);
                lastTimestampNanoseconds = lastTimestampNanoseconds is null
                    ? range.LastNanoseconds
                    : Math.Max(lastTimestampNanoseconds.Value, range.LastNanoseconds);
            }
            else
            {
                using var source = new ETWTraceEventSource(sourcePath);
                sessionStartUtc ??= source.SessionStartTime.ToUniversalTime();
                source.Dynamic.All += data =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (data.ProviderGuid != ProviderId || !TryParse(data, out var parsedEvent))
                    {
                        return;
                    }

                    parsedEvent = parsedEvent with { SourcePath = sourcePath };
                    var timestamp = parsedEvent.Timestamp.ToNanoseconds;
                    firstTimestampNanoseconds = firstTimestampNanoseconds is null
                        ? timestamp
                        : Math.Min(firstTimestampNanoseconds.Value, timestamp);
                    lastTimestampNanoseconds = lastTimestampNanoseconds is null
                        ? timestamp
                        : Math.Max(lastTimestampNanoseconds.Value, timestamp);
                    dataProcessor.ProcessDataElement(parsedEvent, _context, cancellationToken);
                };
                source.Process();
            }
            progress.Report(checked((index + 1) * 100 / _dataSources.Count));
        }

        var first = firstTimestampNanoseconds ?? 0;
        var last = Math.Max(lastTimestampNanoseconds ?? first, first + 1);
        _dataSourceInfo = new DataSourceInfo(first, last, sessionStartUtc ?? DateTime.UtcNow);
    }

    private ArchiveRange ProcessArchive(
        string archivePath,
        ISourceDataProcessor<EtwSnapEvent, EtwSnapParsingContext, Type> dataProcessor,
        CancellationToken cancellationToken)
    {
        var siblingEtl = GetSiblingEtlPath(archivePath);
        var archive = ArtifactArchive.ValidateAsync(
            archivePath,
            File.Exists(siblingEtl) ? siblingEtl : null,
            ProviderId,
            EtwSnap.WpaPlugin.Artifacts.ArtifactResolver.SupportedManifestSchemaVersion,
            cancellationToken).GetAwaiter().GetResult();
        var manifest = archive.Manifest;
        var firstQpc = manifest.Frames!.Count == 0 ? 0 : manifest.Frames.Min(frame => frame.CallbackQpc);
        long? archiveFirst = null;
        long? archiveLast = null;

        Process(new RecordingStartedEvent(
            Timestamp.Zero,
            manifest.SessionId,
            checked((ulong)manifest.QpcFrequency),
            checked((uint)manifest.Capture!.FramesPerSecond),
            checked((ulong)manifest.Capture.BufferMegabytes * 1024 * 1024),
            checked((uint)manifest.Capture.Target!.Kind),
            checked((ulong)manifest.Capture.Target.Handle)));
        foreach (var frame in manifest.Frames.OrderBy(frame => frame.CallbackQpc))
        {
            var relativeQpc = Math.Max(0, frame.CallbackQpc - firstQpc);
            var timestamp = checked((long)((decimal)relativeQpc * 1_000_000_000m / manifest.QpcFrequency));
            Process(new FrameCapturedEvent(
                Timestamp.FromNanoseconds(timestamp),
                manifest.SessionId,
                frame.FrameNumber,
                frame.PresentationTime100ns,
                frame.CallbackQpc,
                frame.Width,
                frame.Height,
                frame.PixelFormat));
        }

        var statistics = manifest.Statistics!;
        var elapsed = Math.Max(
            archiveLast ?? 0,
            checked((manifest.StoppedAtUtc - manifest.StartedAtUtc).Ticks * 100));
        Process(new RecordingStoppedEvent(
            Timestamp.FromNanoseconds(elapsed),
            manifest.SessionId,
            statistics.AcceptedFrames,
            statistics.RetainedFrames,
            statistics.EvictedFrames,
            statistics.DroppedFrames,
            statistics.NativeErrors));

        return new ArchiveRange(
            archiveFirst ?? 0,
            Math.Max(archiveLast ?? 0, 1),
            manifest.StartedAtUtc.UtcDateTime);

        void Process(EtwSnapEvent item)
        {
            item = item with { SourcePath = archivePath };
            var timestamp = item.Timestamp.ToNanoseconds;
            archiveFirst = archiveFirst is null ? timestamp : Math.Min(archiveFirst.Value, timestamp);
            archiveLast = archiveLast is null ? timestamp : Math.Max(archiveLast.Value, timestamp);
            dataProcessor.ProcessDataElement(item, _context, cancellationToken);
        }
    }

    private static string GetSiblingEtlPath(string archivePath)
    {
        var stem = archivePath[..^EmbeddedArtifactConstants.ArtifactFileExtension.Length];
        var direct = stem + ".etl";
        if (File.Exists(direct))
        {
            return direct;
        }
        var suffix = Path.GetExtension(stem);
        return suffix.Length == 33 && Guid.TryParseExact(suffix[1..], "N", out _)
            ? stem[..^suffix.Length] + ".etl"
            : direct;
    }

    internal static bool TryParse(TraceEvent data, out EtwSnapEvent parsedEvent)
    {
        try
        {
            var timestamp = Timestamp.FromNanoseconds(checked((long)(data.TimeStampRelativeMSec * 1_000_000d)));
            var sessionId = Payload<Guid>(data, "SessionId");
            parsedEvent = data.EventName switch
            {
                "RecordingStarted" => new RecordingStartedEvent(
                    timestamp, sessionId, Payload<ulong>(data, "QpcFrequency"),
                    Payload<uint>(data, "FramesPerSecond"), Payload<ulong>(data, "BufferBytes"),
                    Payload<uint>(data, "TargetKind"), Payload<ulong>(data, "TargetHandle")),
                "FrameCaptured" => new FrameCapturedEvent(
                    timestamp, sessionId, Payload<ulong>(data, "FrameNumber"),
                    Payload<long>(data, "PresentationTime100ns"), Payload<long>(data, "CallbackQpc"),
                    Payload<uint>(data, "Width"), Payload<uint>(data, "Height"), Payload<uint>(data, "PixelFormat")),
                "FrameCaptureError" => new FrameCaptureErrorEvent(
                    timestamp, sessionId, Payload<ulong>(data, "FrameNumber"), PayloadHResult(data)),
                "RecordingStopped" => new RecordingStoppedEvent(
                    timestamp, sessionId, Payload<ulong>(data, "AcceptedFrames"),
                    Payload<ulong>(data, "RetainedFrames"), Payload<ulong>(data, "EvictedFrames"),
                    Payload<ulong>(data, "DroppedFrames"), Payload<ulong>(data, "ErrorCount")),
                "ArtifactReference" => new ArtifactReferenceEvent(
                    timestamp, sessionId, Payload<uint>(data, "ContractVersion"),
                    Payload<string>(data, "ArtifactPath"), Payload<string>(data, "ArtifactFileName"),
                    Payload<uint>(data, "ManifestSchemaVersion"), Payload<uint>(data, "BundleSchemaVersion"),
                    Payload<uint>(data, "RequestedTransport")),
                "ArtifactCommitted" => new ArtifactCommittedEvent(
                    timestamp, sessionId, Payload<uint>(data, "ContractVersion"),
                    Payload<string>(data, "ArtifactPath"), Payload<string>(data, "ArtifactFileName"),
                    Payload<uint>(data, "ManifestSchemaVersion"), Payload<uint>(data, "BundleSchemaVersion"),
                    Payload<uint>(data, "RequestedTransport"), Payload<uint>(data, "ActualTransport"),
                    Payload<string>(data, "ManifestSha256"), Payload<string>(data, "ArtifactSha256"),
                    Payload<string>(data, "Status"), Payload<ulong>(data, "AcceptedFrames"),
                    Payload<ulong>(data, "RetainedFrames"), Payload<ulong>(data, "EvictedFrames"),
                    Payload<ulong>(data, "DroppedFrames"), Payload<ulong>(data, "ErrorCount"),
                    Payload<ulong>(data, "ExportedFrames"), Payload<ulong>(data, "FailedFrames")),
                _ => null!,
            };
            return parsedEvent is not null;
        }
        catch (Exception)
        {
            parsedEvent = null!;
            return false;
        }
    }

    private static T Payload<T>(TraceEvent data, string name)
    {
        var value = data.PayloadByName(name);
        if (value is T typed)
        {
            return typed;
        }
        if (typeof(T) == typeof(Guid))
        {
            return (T)(object)Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
        }
        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int PayloadHResult(TraceEvent data)
    {
        var value = data.PayloadByName("HResult");
        return value switch
        {
            int signed => signed,
            uint unsigned => unchecked((int)unsigned),
            long signed => unchecked((int)signed),
            ulong unsigned => unchecked((int)unsigned),
            _ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private sealed record ArchiveRange(long FirstNanoseconds, long LastNanoseconds, DateTime SessionStartUtc);
}