using System.Diagnostics;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;

namespace EtwSnap.IntegrationTests.Capture;

public sealed class NativeCaptureIntegrationTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public async Task CapturesAndExportsPrimaryMonitorFrame()
    {
        var sessionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var request = new StartCaptureRequest(
            false,
            null,
            new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
            30,
            128,
            false);
        using var factory = new NativeCaptureFactory();
        using var capture = factory.Create(sessionId, request);

        capture.Start();
        var timeout = Stopwatch.StartNew();
        NativeCaptureStats liveStats;
        do
        {
            liveStats = capture.GetStats();
            Thread.Yield();
        }
        while (liveStats.RetainedFrames == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5));

        Assert.True(liveStats.RetainedFrames > 0);
        capture.Stop();

        var stoppedStats = capture.GetStats();
        var count = capture.GetFrameCount();
        Assert.Equal(stoppedStats.RetainedFrames, count);
        Assert.True(count > 0);

        var frame = capture.GetFrameInfo(0);
        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);
        var pixels = new byte[checked((int)frame.RequiredBytes)];
        capture.CopyFrameBgra(0, pixels, checked(frame.Width * 4));
        Assert.Contains(pixels, value => value != 0);

        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-native-test-{Guid.NewGuid():N}");
        try
        {
            var writer = new SessionArtifactWriter();
            var reservation = writer.Reserve(root, sessionId, startedAt);
            var result = await writer.WriteAsync(
                reservation,
                new SessionMetadata(sessionId, startedAt, request, null),
                capture,
                stoppedStats,
                null,
                [],
                default,
                (_, _) => ValueTask.CompletedTask);

            Assert.Equal(checked((int)count), result.ExportedFrames);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.Equal(count, (ulong)Directory.GetFiles(Path.Combine(result.OutputDirectory, "frames"), "*.png").Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        }
    }
