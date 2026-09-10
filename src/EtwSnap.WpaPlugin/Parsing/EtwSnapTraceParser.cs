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

                var timestampNanoseconds = parsedEvent.Timestamp.ToNanoseconds;
                firstTimestampNanoseconds = !firstTimestampNanoseconds.HasValue
                    ? timestampNanoseconds
                    : Math.Min(firstTimestampNanoseconds.Value, timestampNanoseconds);
                lastTimestampNanoseconds = !lastTimestampNanoseconds.HasValue
                    ? timestampNanoseconds
                    : Math.Max(lastTimestampNanoseconds.Value, timestampNanoseconds);
                dataProcessor.ProcessDataElement(parsedEvent, _context, cancellationToken);
            };
            source.Process();
            progress.Report(checked((index + 1) * 100 / _dataSources.Count));
        }

        var first = firstTimestampNanoseconds ?? 0;
        var last = Math.Max(lastTimestampNanoseconds ?? first, first + 1);
        _dataSourceInfo = new DataSourceInfo(first, last, sessionStartUtc ?? DateTime.UtcNow);
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
                    timestamp,
                    sessionId,
                    Payload<ulong>(data, "QpcFrequency"),
                    Payload<uint>(data, "FramesPerSecond"),
                    Payload<ulong>(data, "BufferBytes"),
                    Payload<uint>(data, "TargetKind"),
                    Payload<ulong>(data, "TargetHandle")),
                "FrameCaptured" => new FrameCapturedEvent(
                    timestamp,
                    sessionId,
                    Payload<ulong>(data, "FrameNumber"),
                    Payload<long>(data, "PresentationTime100ns"),
                    Payload<long>(data, "CallbackQpc"),
                    Payload<uint>(data, "Width"),
                    Payload<uint>(data, "Height"),
                    Payload<uint>(data, "PixelFormat")),
                "FrameCaptureError" => new FrameCaptureErrorEvent(
                    timestamp,
                    sessionId,
                    Payload<ulong>(data, "FrameNumber"),
                    PayloadHResult(data)),
                "RecordingStopped" => new RecordingStoppedEvent(
                    timestamp,
                    sessionId,
                    Payload<ulong>(data, "AcceptedFrames"),
                    Payload<ulong>(data, "RetainedFrames"),
                    Payload<ulong>(data, "EvictedFrames"),
                    Payload<ulong>(data, "DroppedFrames"),
                    Payload<ulong>(data, "ErrorCount")),
                "ArtifactReference" => new ArtifactReferenceEvent(
                    timestamp,
                    sessionId,
                    Payload<uint>(data, "ContractVersion"),
                    Payload<string>(data, "ArtifactDirectory"),
                    Payload<string>(data, "SessionDirectoryName"),
                    Payload<string>(data, "ManifestRelativePath"),
                    Payload<string>(data, "PortableManifestRelativePath"),
                    Payload<uint>(data, "ManifestSchemaVersion")),
                "ArtifactCommitted" => new ArtifactCommittedEvent(
                    timestamp,
                    sessionId,
                    Payload<uint>(data, "ContractVersion"),
                    Payload<string>(data, "ArtifactDirectory"),
                    Payload<string>(data, "SessionDirectoryName"),
                    Payload<string>(data, "ManifestRelativePath"),
                    Payload<string>(data, "PortableManifestRelativePath"),
                    Payload<uint>(data, "ManifestSchemaVersion"),
                    Payload<string>(data, "ManifestSha256"),
                    Payload<string>(data, "Status"),
                    Payload<ulong>(data, "AcceptedFrames"),
                    Payload<ulong>(data, "RetainedFrames"),
                    Payload<ulong>(data, "EvictedFrames"),
                    Payload<ulong>(data, "DroppedFrames"),
                    Payload<ulong>(data, "ErrorCount"),
                    Payload<ulong>(data, "ExportedFrames"),
                    Payload<ulong>(data, "FailedFrames")),
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
}