using System.Text.Json;
using EtwSnap.Artifacts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Recovery;
using EtwSnap.Host.Tracing;
using Xunit.Sdk;

namespace EtwSnap.UnitTests.Recovery;

public sealed class RecoveryStoreTests
{
    [Fact]
    public async Task AbandonedEmbeddedStagingIsPublishedAsSidecarZip()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-recovery-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "state");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(outputRoot);
        try
        {
            try
            {
                await new NamedStreamStore().PreflightAsync(outputRoot, default);
            }
            catch (IOException)
            {
                throw SkipException.ForSkip("The test volume does not support writable named streams.");
            }

            var sessionId = Guid.NewGuid();
            var name = $"etwsnap-20260101T000000Z-{sessionId:N}"[..34];
            var staging = Path.Combine(outputRoot, ".etwsnap-staging", name);
            var finalEtl = Path.Combine(outputRoot, name + ".etl");
            var finalZip = Path.Combine(outputRoot, EmbeddedArtifactConstants.GetArtifactFileName(name));
            Directory.CreateDirectory(Path.Combine(staging, "frames"));
            var tracePath = Path.Combine(staging, "trace.etl");
            var primaryBytes = "etl-primary"u8.ToArray();
            await File.WriteAllBytesAsync(tracePath, primaryBytes);
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), "{\"schemaVersion\":2}");
            await File.WriteAllBytesAsync(Path.Combine(staging, ".reserved"), []);
            var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
            await File.WriteAllBytesAsync(new NamedStreamStore().GetStreamPath(tracePath, streamName), "partial"u8.ToArray());

            var store = new RecoveryStore(stateRoot);
            var request = new StartCaptureRequest(
                true,
                null,
                new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                30,
                500,
                true);
            await store.BeginAsync(sessionId, DateTimeOffset.UtcNow, request, default);
            await store.MarkArtifactsAsync(
                sessionId,
                new RecoveryArtifactState(
                    ArtifactTransport.Embedded,
                    null,
                    staging,
                    finalEtl,
                    finalZip,
                    streamName),
                default);
            await store.MarkAsync(sessionId, "Persisting", staging, null, default);

            var wpr = new FakeWprController();
            await store.RecoverAbandonedAsync(wpr, default);

            Assert.False(Directory.Exists(staging));
            Assert.True(File.Exists(finalZip));
            Assert.Equal(primaryBytes, await File.ReadAllBytesAsync(finalEtl));
            Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(finalEtl));
            Assert.Equal(1, wpr.CancelCalls);

            await using var stream = File.OpenRead(Path.Combine(stateRoot, sessionId.ToString("N"), "session.json"));
            using var state = await JsonDocument.ParseAsync(stream);
            Assert.Equal("Partial", state.RootElement.GetProperty("status").GetString());
            Assert.Equal(finalZip, state.RootElement.GetProperty("outputDirectory").GetString());
            Assert.Equal((int)ArtifactTransport.Sidecar, state.RootElement.GetProperty("artifacts").GetProperty("actualTransport").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MalformedPublishedBundleFallsBackToSidecarWithCleanEtl()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-recovery-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "state");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(outputRoot);
        try
        {
            var sessionId = Guid.NewGuid();
            var staging = Path.Combine(outputRoot, ".etwsnap-staging", "session");
            var finalEtl = Path.Combine(outputRoot, "session.etl");
            var finalZip = Path.Combine(outputRoot, "session.etwsnap.zip");
            Directory.CreateDirectory(staging);
            await File.WriteAllBytesAsync(Path.Combine(staging, "trace.etl"), "staged-etl"u8.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(staging, ".reserved"), []);
            await File.WriteAllBytesAsync(
                Path.Combine(staging, "manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 2,
                    sessionId,
                    status = "Complete",
                    startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    stoppedAtUtc = DateTimeOffset.UtcNow,
                    qpcFrequency = 10_000_000,
                    capture = new
                    {
                        trace = true,
                        target = new { kind = 0, handle = 0 },
                        framesPerSecond = 30,
                        bufferMegabytes = 500,
                        captureCursor = true,
                    },
                    provider = new { name = "ETWSnap-Service", id = EtwSnap.Contracts.EtwSnapConstants.ProviderId },
                    trace = new
                    {
                        instanceName = "test",
                        supplementalProfilePath = "EtwSnap.wprp",
                        supplementalProfileHash = "hash",
                        userProfilePath = (string?)null,
                        userProfileSelector = (string?)null,
                        userProfileHash = (string?)null,
                    },
                    statistics = new
                    {
                        acceptedFrames = 0,
                        retainedFrames = 0,
                        evictedFrames = 0,
                        droppedFrames = 0,
                        nativeErrors = 0,
                        exportedFrames = 0,
                        failedFrames = 0,
                    },
                    frames = Array.Empty<object>(),
                }));
            await File.WriteAllBytesAsync(finalEtl, "final-etl"u8.ToArray());
            File.Delete(Path.Combine(staging, "trace.etl"));
            var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
            await File.WriteAllBytesAsync(
                new NamedStreamStore().GetStreamPath(finalEtl, streamName),
                "invalid-zip"u8.ToArray());

            var store = new RecoveryStore(stateRoot);
            var request = new StartCaptureRequest(
                true,
                null,
                new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                30,
                500,
                true);
            await store.BeginAsync(sessionId, DateTimeOffset.UtcNow, request, default);
            await store.MarkArtifactsAsync(
                sessionId,
                new RecoveryArtifactState(ArtifactTransport.Embedded, null, staging, finalEtl, finalZip, streamName),
                default);
            await store.MarkAsync(sessionId, "Persisting", staging, null, default);

            await store.RecoverAbandonedAsync(new FakeWprController(), default);

            Assert.False(Directory.Exists(staging));
            Assert.True(File.Exists(finalEtl));
            Assert.True(File.Exists(finalZip));
            Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(finalEtl));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeWprController : IWprController
    {
        public int CancelCalls { get; private set; }

        public Task<WprSession> StartAsync(Guid sessionId, string? userProfileSelector, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopAsync(WprSession session, string outputPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(WprSession session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelInstanceAsync(string instanceName, CancellationToken cancellationToken)
        {
            ++CancelCalls;
            return Task.CompletedTask;
        }
    }
}