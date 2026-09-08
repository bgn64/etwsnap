using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Capture;

namespace EtwSnap.IntegrationTests.Capture;

public sealed class EtwCorrelationIntegrationTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public void RetainedFramesJoinExactlyToNativeEtwEvents()
    {
        var sessionId = Guid.NewGuid();
        var etlPath = Path.Combine(Path.GetTempPath(), $"etwsnap-correlation-{sessionId:N}.etl");
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
            capture.Stop();

            var retainedFrames = Enumerable.Range(0, checked((int)capture.GetFrameCount()))
                .Select(index => capture.GetFrameInfo(checked((ulong)index)))
                .ToDictionary(frame => frame.FrameNumber);
            Assert.NotEmpty(retainedFrames);
            trace.Stop();

            var events = new Dictionary<ulong, CorrelationEvent>();
            using var source = new ETWTraceEventSource(etlPath);
            source.Dynamic.All += data =>
            {
                if (data.ProviderGuid != EtwSnapConstants.ProviderId || data.EventName != "FrameCaptured")
                {
                    return;
                }

                var eventSessionId = (Guid)data.PayloadByName("SessionId");
                if (eventSessionId != sessionId)
                {
                    return;
                }

                var frameNumber = Convert.ToUInt64(data.PayloadByName("FrameNumber"));
                Assert.True(events.TryAdd(frameNumber, new CorrelationEvent(
                    Convert.ToInt64(data.PayloadByName("PresentationTime100ns")),
                    Convert.ToInt64(data.PayloadByName("CallbackQpc")),
                    Convert.ToUInt32(data.PayloadByName("Width")),
                    Convert.ToUInt32(data.PayloadByName("Height")))));
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
        }
        finally
        {
            if (File.Exists(etlPath))
            {
                File.Delete(etlPath);
            }
        }
    }

    private sealed record CorrelationEvent(long PresentationTime100ns, long CallbackQpc, uint Width, uint Height);
}