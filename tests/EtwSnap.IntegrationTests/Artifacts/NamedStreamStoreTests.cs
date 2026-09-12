using EtwSnap.Artifacts;
using System.Text.Json;
using Xunit.Sdk;

namespace EtwSnap.IntegrationTests.Artifacts;

public sealed class NamedStreamStoreTests
{
    [Fact]
    public async Task StreamRoundTripEnumeratesAndDeletesWithoutChangingPrimaryFile()
    {
        using var fixture = await NamedStreamFixture.CreateAsync();
        var store = new NamedStreamStore();
        var sessionId = Guid.NewGuid();
        var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
        var payloadPath = Path.Combine(fixture.Root, "payload.zip");
        var payload = "embedded-artifacts"u8.ToArray();
        await File.WriteAllBytesAsync(payloadPath, payload);

        await store.WriteFromFileAsync(fixture.EtlPath, streamName, payloadPath, default);

        Assert.Equal(fixture.PrimaryBytes, await File.ReadAllBytesAsync(fixture.EtlPath));
        await using (var stream = store.OpenRead(fixture.EtlPath, streamName))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Assert.Equal(payload, memory.ToArray());
        }
        var streamInfo = Assert.Single(store.EnumerateEtwSnapStreams(fixture.EtlPath));
        Assert.Equal(streamName, streamInfo.Name);
        Assert.Equal(payload.Length, streamInfo.Size);

        store.Delete(fixture.EtlPath, streamName);

        Assert.False(store.Exists(fixture.EtlPath, streamName));
        Assert.Empty(store.EnumerateEtwSnapStreams(fixture.EtlPath));
    }

    [Fact]
    public async Task SameVolumeRenamePreservesNamedStream()
    {
        using var fixture = await NamedStreamFixture.CreateAsync();
        var store = new NamedStreamStore();
        var sessionId = Guid.NewGuid();
        var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
        var payloadPath = Path.Combine(fixture.Root, "payload.zip");
        await File.WriteAllBytesAsync(payloadPath, "bundle"u8.ToArray());
        await store.WriteFromFileAsync(fixture.EtlPath, streamName, payloadPath, default);
        var renamedPath = Path.Combine(fixture.Root, "published.etl");

        File.Move(fixture.EtlPath, renamedPath);

        Assert.Equal(fixture.PrimaryBytes, await File.ReadAllBytesAsync(renamedPath));
        Assert.True(store.Exists(renamedPath, streamName));
        Assert.Equal(streamName, Assert.Single(store.EnumerateEtwSnapStreams(renamedPath)).Name);
    }

    [Fact]
    public async Task ArtifactManagerRoundTripExportsZipBeforeRemovingStream()
    {
        using var fixture = await NamedStreamFixture.CreateAsync();
        var providerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var sessionDirectory = Path.Combine(fixture.Root, "session");
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "frames"));
        var framePath = Path.Combine(sessionDirectory, "frames", "frame_00000001.png");
        var frameBytes = new byte[] { 137, 80, 78, 71 };
        await File.WriteAllBytesAsync(framePath, frameBytes);
        await File.WriteAllBytesAsync(
            Path.Combine(sessionDirectory, "manifest.json"),
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
                provider = new { name = "ETWSnap-Service", id = providerId },
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
                    acceptedFrames = 1,
                    retainedFrames = 1,
                    evictedFrames = 0,
                    droppedFrames = 0,
                    nativeErrors = 0,
                    exportedFrames = 1,
                    failedFrames = 0,
                },
                frames = new[]
                {
                    new
                    {
                        frameNumber = 1,
                        presentationTime100ns = 10,
                        callbackQpc = 20,
                        width = 1,
                        height = 1,
                        pixelFormat = 1,
                        path = "frames/frame_00000001.png",
                    },
                },
            }));
        var zipPath = Path.Combine(fixture.Root, "session.etwsnap.zip");
        await new EmbeddedBundle().CreateAsync(
            sessionDirectory,
            fixture.EtlPath,
            sessionId,
            providerId,
            2,
            zipPath,
            default);
        var validated = await ArtifactArchive.ValidateAsync(zipPath, fixture.EtlPath, providerId, 2, default);
        var manager = new EmbeddedArtifactManager();

        var added = await manager.AddAsync(fixture.EtlPath, validated, default);
        var inspected = Assert.Single(await manager.InspectAsync(fixture.EtlPath, default));

        Assert.True(inspected.IsValid);
        Assert.Equal(sessionId, inspected.SessionId);
        Assert.Equal(added.StreamName, inspected.Stream.Name);

        var exportRoot = Path.Combine(fixture.Root, "exported");
        var export = await manager.ExportAsync(fixture.EtlPath, [inspected], exportRoot, default);

        Assert.Equal(Path.GetFileName(fixture.EtlPath), Path.GetFileName(export.EtlPath));
        Assert.Equal(fixture.PrimaryBytes, await File.ReadAllBytesAsync(export.EtlPath));
        var exportedZip = Assert.Single(export.ArtifactZipPaths);
        Assert.EndsWith(EmbeddedArtifactConstants.ArtifactFileExtension, exportedZip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(await File.ReadAllBytesAsync(zipPath), await File.ReadAllBytesAsync(exportedZip));
        Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(export.EtlPath));
        Assert.False(Directory.Exists(Path.Combine(exportRoot, "frames")));

        manager.Remove(fixture.EtlPath, [inspected]);

        Assert.Empty(await manager.InspectAsync(fixture.EtlPath, default));
    }

    [Fact]
    public async Task MalformedEtwSnapStreamIsReportedAsInvalid()
    {
        using var fixture = await NamedStreamFixture.CreateAsync();
        var streamName = EmbeddedArtifactConstants.StreamPrefix + "not-a-session-id";
        await File.WriteAllBytesAsync(
            new NamedStreamStore().GetStreamPath(fixture.EtlPath, streamName),
            "partial"u8.ToArray());

        var inspection = Assert.Single(await new EmbeddedArtifactManager().InspectAsync(fixture.EtlPath, default));

        Assert.False(inspection.IsValid);
        Assert.Null(inspection.SessionId);
        Assert.Contains("valid full session ID", inspection.Error);
    }

    [Fact]
    public async Task ConcurrentWritersDoNotDeleteWinningStream()
    {
        using var fixture = await NamedStreamFixture.CreateAsync();
        var store = new NamedStreamStore();
        var streamName = EmbeddedArtifactConstants.GetStreamName(Guid.NewGuid());
        var firstPath = Path.Combine(fixture.Root, "first.zip");
        var secondPath = Path.Combine(fixture.Root, "second.zip");
        var firstBytes = "first-bundle"u8.ToArray();
        var secondBytes = "second-bundle"u8.ToArray();
        await File.WriteAllBytesAsync(firstPath, firstBytes);
        await File.WriteAllBytesAsync(secondPath, secondBytes);
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            TryWriteAsync(firstPath),
            TryWriteAsync(secondPath));

        Assert.Single(results, result => result);
        await using var stream = store.OpenRead(fixture.EtlPath, streamName);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Assert.True(memory.ToArray().SequenceEqual(firstBytes) || memory.ToArray().SequenceEqual(secondBytes));

        async Task<bool> TryWriteAsync(string sourcePath)
        {
            return await Task.Run(async () =>
            {
                barrier.SignalAndWait();
                try
                {
                    await store.WriteFromFileAsync(fixture.EtlPath, streamName, sourcePath, default);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            });
        }
    }

    private sealed class NamedStreamFixture : IDisposable
    {
        private NamedStreamFixture(string root, string etlPath, byte[] primaryBytes)
        {
            Root = root;
            EtlPath = etlPath;
            PrimaryBytes = primaryBytes;
        }

        public string Root { get; }
        public string EtlPath { get; }
        public byte[] PrimaryBytes { get; }

        public static async Task<NamedStreamFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"etwsnap-stream-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var store = new NamedStreamStore();
            try
            {
                await store.PreflightAsync(root, default);
            }
            catch (IOException)
            {
                Directory.Delete(root, recursive: true);
                throw SkipException.ForSkip("The test volume does not support writable named streams.");
            }

            var etlPath = Path.Combine(root, "trace.etl");
            var primaryBytes = "etl-primary-stream"u8.ToArray();
            await File.WriteAllBytesAsync(etlPath, primaryBytes);
            return new NamedStreamFixture(root, etlPath, primaryBytes);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}