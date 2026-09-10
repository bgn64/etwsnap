using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Extensibility.SourceParsing;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.IntegrationTests.Capture;

public sealed class EtwCorrelationIntegrationTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public async Task RetainedFramesAndArtifactsJoinExactlyToNativeEtwEvents()
    {
        var sessionId = Guid.NewGuid();
        var etlPath = Path.Combine(Path.GetTempPath(), $"etwsnap-correlation-{sessionId:N}.etl");
        var outputRoot = Path.Combine(Path.GetTempPath(), $"etwsnap-correlation-{sessionId:N}");
        var traceSessionName = $"EtwSnapCorrelation_{sessionId:N}";

        try
        {
            using var trace = new TraceEventSession(traceSessionName, etlPath);
            trace.EnableProvider(EtwSnapConstants.ProviderId, TraceEventLevel.Verbose);

            using var factory = new NativeCaptureFactory();
            using var capture = factory.Create(
                sessionId,
                new StartCaptureRequest(
                    false,
                    null,
                    new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                    30,
                    128,
                    false));

            capture.Start();
            var timeout = Stopwatch.StartNew();
            while (capture.GetStats().RetainedFrames < 3 && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Yield();
            }

            var writer = new SessionArtifactWriter();
            var reservation = writer.Reserve(outputRoot, sessionId, DateTimeOffset.UtcNow);
            var artifactReference = ArtifactEvents.CreateReference(sessionId, reservation);
            NativeArtifactEventEmitter.Instance.EmitReference(artifactReference);
            capture.Stop();
            var stoppedStats = capture.GetStats();

            var retainedFrames = Enumerable.Range(0, checked((int)capture.GetFrameCount()))
                .Select(index => capture.GetFrameInfo(checked((ulong)index)))
                .ToDictionary(frame => frame.FrameNumber);
            Assert.NotEmpty(retainedFrames);
            var artifactResult = await writer.WriteAsync(
                reservation,
                new SessionMetadata(
                    sessionId,
                    DateTimeOffset.UtcNow,
                    new StartCaptureRequest(
                        false,
                        null,
                        new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                        30,
                        128,
                        false),
                    null),
                capture,
                stoppedStats,
                null,
                [],
                default,
                (_, _) => ValueTask.CompletedTask);
            NativeArtifactEventEmitter.Instance.EmitCommitted(
                ArtifactEvents.CreateCommitted(artifactReference, artifactResult, stoppedStats));
            trace.Stop();

            var events = new Dictionary<ulong, CorrelationEvent>();
            ParsedArtifactReference? parsedReference = null;
            ParsedArtifactCommitted? parsedCommitted = null;
            var eventOrdinal = 0;
            var recordingStoppedOrdinal = 0;
            using var source = new ETWTraceEventSource(etlPath);
            source.Dynamic.All += data =>
            {
                if (data.ProviderGuid != EtwSnapConstants.ProviderId)
                {
                    return;
                }

                var eventSessionId = (Guid)data.PayloadByName("SessionId");
                if (eventSessionId != sessionId)
                {
                    return;
                }

                ++eventOrdinal;
                switch (data.EventName)
                {
                    case "FrameCaptured":
                        var frameNumber = Convert.ToUInt64(data.PayloadByName("FrameNumber"));
                        Assert.True(events.TryAdd(frameNumber, new CorrelationEvent(
                            Convert.ToInt64(data.PayloadByName("PresentationTime100ns")),
                            Convert.ToInt64(data.PayloadByName("CallbackQpc")),
                            Convert.ToUInt32(data.PayloadByName("Width")),
                            Convert.ToUInt32(data.PayloadByName("Height")))));
                        break;
                    case "ArtifactReference":
                        parsedReference = new ParsedArtifactReference(
                            Convert.ToUInt32(data.PayloadByName("ContractVersion")),
                            Convert.ToString(data.PayloadByName("ArtifactDirectory"))!,
                            Convert.ToString(data.PayloadByName("PortableManifestRelativePath"))!,
                            eventOrdinal);
                        break;
                    case "RecordingStopped":
                        recordingStoppedOrdinal = eventOrdinal;
                        break;
                    case "ArtifactCommitted":
                        parsedCommitted = new ParsedArtifactCommitted(
                            Convert.ToString(data.PayloadByName("ManifestSha256"))!,
                            Convert.ToString(data.PayloadByName("Status"))!,
                            eventOrdinal);
                        break;
                }
            };
            source.Process();

            foreach (var frame in retainedFrames.Values)
            {
                Assert.True(events.TryGetValue(frame.FrameNumber, out var traceEvent), $"Missing ETW event for frame {frame.FrameNumber}.");
                Assert.Equal(frame.PresentationTime100ns, traceEvent.PresentationTime100ns);
                Assert.Equal(frame.CallbackQpc, traceEvent.CallbackQpc);
                Assert.Equal(frame.Width, traceEvent.Width);
                Assert.Equal(frame.Height, traceEvent.Height);
            }

            Assert.NotNull(parsedReference);
            Assert.Equal(EtwSnapConstants.ArtifactContractVersion, checked((int)parsedReference.ContractVersion));
            Assert.Equal(reservation.DirectoryPath, parsedReference.ArtifactDirectory);
            Assert.Equal($"sessions/{sessionId:N}/manifest.json", parsedReference.PortableManifestRelativePath);
            Assert.NotNull(parsedCommitted);
            Assert.Equal(artifactResult.ManifestSha256, parsedCommitted.ManifestSha256);
            Assert.Equal(artifactResult.Status, parsedCommitted.Status);
            Assert.True(parsedReference.Ordinal < recordingStoppedOrdinal);
            Assert.True(recordingStoppedOrdinal < parsedCommitted.Ordinal);

            var pluginEvents = new PluginEventCollector();
            var pluginParser = new EtwSnapTraceParser([new FileDataSource(etlPath)]);
            pluginParser.ProcessSource(pluginEvents, null!, new Progress<int>(), default);
            Assert.Equal(retainedFrames.Count, pluginEvents.Events.OfType<FrameCapturedEvent>().Count(frame => retainedFrames.ContainsKey(frame.FrameNumber)));
            Assert.Single(pluginEvents.Events.OfType<EtwSnap.WpaPlugin.Parsing.ArtifactReferenceEvent>());
            Assert.Single(pluginEvents.Events.OfType<EtwSnap.WpaPlugin.Parsing.ArtifactCommittedEvent>());
        }
        finally
        {
            if (File.Exists(etlPath))
            {
                File.Delete(etlPath);
            }
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private sealed record CorrelationEvent(long PresentationTime100ns, long CallbackQpc, uint Width, uint Height);
    private sealed record ParsedArtifactReference(
        uint ContractVersion,
        string ArtifactDirectory,
        string PortableManifestRelativePath,
        int Ordinal);
    private sealed record ParsedArtifactCommitted(string ManifestSha256, string Status, int Ordinal);

    private sealed class PluginEventCollector
        : ISourceDataProcessor<EtwSnapEvent, EtwSnapParsingContext, Type>
    {
        public List<EtwSnapEvent> Events { get; } = [];

        public DataProcessingResult ProcessDataElement(
            EtwSnapEvent data,
            EtwSnapParsingContext context,
            CancellationToken cancellationToken)
        {
            Events.Add(data);
            return DataProcessingResult.Processed;
        }
    }
}