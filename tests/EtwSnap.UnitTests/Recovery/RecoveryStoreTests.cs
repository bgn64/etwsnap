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
    public async Task AbandonedEmbeddedStagingIsPublishedAsCleanFolder()
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
            Directory.CreateDirectory(Path.Combine(staging, "frames"));
            var tracePath = Path.Combine(staging, "trace.etl");
            var primaryBytes = "etl-primary"u8.ToArray();
            await File.WriteAllBytesAsync(tracePath, primaryBytes);
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), "{\"schemaVersion\":1}");
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
                    streamName),
                default);
            await store.MarkAsync(sessionId, "Persisting", staging, null, default);

            var wpr = new FakeWprController();
            await store.RecoverAbandonedAsync(wpr, default);

            var fallback = Path.ChangeExtension(finalEtl, null)!;
            Assert.True(Directory.Exists(fallback));
            Assert.False(Directory.Exists(staging));
            Assert.False(File.Exists(Path.Combine(fallback, ".reserved")));
            Assert.Equal(primaryBytes, await File.ReadAllBytesAsync(Path.Combine(fallback, "trace.etl")));
            Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(Path.Combine(fallback, "trace.etl")));
            Assert.Equal(1, wpr.CancelCalls);

            await using var stream = File.OpenRead(Path.Combine(stateRoot, sessionId.ToString("N"), "session.json"));
            using var state = await JsonDocument.ParseAsync(stream);
            Assert.Equal("Partial", state.RootElement.GetProperty("status").GetString());
            Assert.Equal(fallback, state.RootElement.GetProperty("outputDirectory").GetString());
            Assert.Equal((int)ArtifactTransport.Folder, state.RootElement.GetProperty("artifacts").GetProperty("actualTransport").GetInt32());
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
    public async Task MalformedPublishedBundleDoesNotBlockStagingRecovery()
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
            Directory.CreateDirectory(staging);
            await File.WriteAllBytesAsync(Path.Combine(staging, "trace.etl"), "staged-etl"u8.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(staging, ".reserved"), []);
            await File.WriteAllBytesAsync(finalEtl, "final-etl"u8.ToArray());
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
                new RecoveryArtifactState(ArtifactTransport.Embedded, null, staging, finalEtl, streamName),
                default);
            await store.MarkAsync(sessionId, "Persisting", staging, null, default);

            await store.RecoverAbandonedAsync(new FakeWprController(), default);

            Assert.True(Directory.Exists(Path.Combine(outputRoot, "session")));
            Assert.True(File.Exists(finalEtl));
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