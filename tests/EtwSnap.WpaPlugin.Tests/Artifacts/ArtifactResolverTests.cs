using System.Text.Json;
using EtwSnap.Artifacts;
using EtwSnap.WpaPlugin.Artifacts;
using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using EtwSnap.WpaPlugin.Tables;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Extensibility.SourceParsing;
using Microsoft.Performance.SDK.Processing;
using Xunit.Sdk;

namespace EtwSnap.WpaPlugin.Tests.Artifacts;

public sealed class ArtifactResolverTests
{
    [Fact]
    public async Task ScreenshotOnlyZipLoadsDirectlyIntoWpaDataSet()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var zipPath = await fixture.WriteScreenshotOnlyBundleAsync(sessionId, [[137, 80, 78, 71]]);
        var collector = new PluginEventCollector();
        var parser = new EtwSnapTraceParser([new FileDataSource(zipPath)]);

        parser.ProcessSource(collector, null!, new Progress<int>(), default);
        var dataSet = EtwSnapDataSet.Build(collector.Events);

        Assert.Single(dataSet.Sessions);
        var screenshot = Assert.Single(dataSet.Screenshots);
        Assert.Equal(ScreenshotAvailability.Saved, screenshot.Availability);
        Assert.True(File.Exists(screenshot.ImagePath));
        File.Delete(screenshot.ImagePath!);
    }

    [Fact]
    public async Task ResolvesCanonicalSidecarZipAndMaterializesFrame()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var imageBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        await fixture.WriteSidecarBundleAsync(sessionId, [imageBytes]);
        var frame = CreateFrame(sessionId, fixture.TracePath);

        var resolution = new ArtifactResolver().Resolve(sessionId, [frame]);
        var screenshot = Assert.Single(EtwSnapDataSet.Build([frame]).Screenshots);

        Assert.Equal(ArtifactResolutionState.Resolved, resolution.State);
        Assert.True(File.Exists(screenshot.ImagePath));
        Assert.Equal(imageBytes, await File.ReadAllBytesAsync(screenshot.ImagePath!));
        File.Delete(screenshot.ImagePath!);
    }

    [Fact]
    public async Task ResolvesSessionQualifiedSidecarZip()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        await fixture.WriteSidecarBundleAsync(sessionId, [[137, 80, 78, 71]], qualifyWithSessionId: true);

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.Equal(ArtifactResolutionState.Resolved, resolution.State);
        Assert.Contains(sessionId.ToString("N"), resolution.ManifestPath);
        foreach (var path in resolution.FramePaths.Values)
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsSidecarManifestPathTraversal()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        await fixture.WriteSidecarBundleAsync(sessionId, [], framePathOverride: "../outside.png");

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.Equal(ArtifactResolutionState.InvalidManifest, resolution.State);
    }

    [Fact]
    public async Task RejectsCommittedSidecarManifestHashMismatch()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        await fixture.WriteSidecarBundleAsync(sessionId, [[137, 80, 78, 71]]);
        var reference = CreateReference(sessionId, fixture.Root, fixture.TracePath);
        var committed = new ArtifactCommittedEvent(
            Timestamp.FromNanoseconds(300), sessionId, 2,
            reference.ArtifactPath, reference.ArtifactFileName,
            2, 2, 0, 0, new string('0', 64), new string('1', 64),
            "Complete", 1, 1, 0, 0, 0, 1, 0)
        {
            SourcePath = fixture.TracePath,
        };

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath), committed]);

        Assert.Equal(ArtifactResolutionState.IntegrityMismatch, resolution.State);
    }

    [Fact]
    public async Task EmbeddedArtifactsWinAndMaterializeDuringResolution()
    {
        using var fixture = new ArtifactFixture();
        await fixture.RequireNamedStreamsAsync();
        var sessionId = Guid.NewGuid();
        await fixture.WriteSidecarBundleAsync(sessionId, [[1, 2, 3], [4, 5, 6]]);
        byte[][] embeddedImageBytes = [[137, 80, 78, 71, 4, 5, 6], [137, 80, 78, 71, 7, 8, 9]];
        await fixture.WriteEmbeddedBundleAsync(sessionId, embeddedImageBytes);
        var frames = new[]
        {
            CreateFrame(sessionId, fixture.TracePath),
            CreateFrame(sessionId, fixture.TracePath, 2, 200),
        };

        var resolution = new ArtifactResolver().Resolve(sessionId, frames);
        var screenshots = EtwSnapDataSet.Build(frames).Screenshots;

        Assert.Equal(ArtifactResolutionState.Resolved, resolution.State);
        Assert.Equal(2, resolution.FramePaths.Count);
        Assert.Contains(EmbeddedArtifactConstants.StreamPrefix, resolution.ManifestPath);
        Assert.All(screenshots, screenshot => Assert.Equal(ScreenshotAvailability.Saved, screenshot.Availability));
        Assert.All(screenshots, screenshot => Assert.True(File.Exists(screenshot.ImagePath)));
        Assert.Single(screenshots.Select(screenshot => Path.GetDirectoryName(screenshot.ImagePath)).Distinct());

        Assert.True(ScreenshotTableCommands.TryCreateViewStartInfo(screenshots, [0], out var startInfo));
        try
        {
            Assert.Equal(screenshots[0].ImagePath, startInfo.FileName);
            Assert.True(File.Exists(startInfo.FileName));
            Assert.Equal(embeddedImageBytes[0], await File.ReadAllBytesAsync(screenshots[0].ImagePath!));
            Assert.Equal(embeddedImageBytes[1], await File.ReadAllBytesAsync(screenshots[1].ImagePath!));
        }
        finally
        {
            foreach (var screenshot in screenshots)
            {
                File.Delete(screenshot.ImagePath!);
            }
        }
    }

    [Fact]
    public async Task InvalidEmbeddedStreamIsNotMaskedByValidSidecar()
    {
        using var fixture = new ArtifactFixture();
        await fixture.RequireNamedStreamsAsync();
        var sessionId = Guid.NewGuid();
        await fixture.WriteSidecarBundleAsync(sessionId, [[137, 80, 78, 71]]);
        File.WriteAllBytes(
            new NamedStreamStore().GetStreamPath(fixture.TracePath, EmbeddedArtifactConstants.GetStreamName(sessionId)),
            "not-a-zip"u8.ToArray());

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.NotEqual(ArtifactResolutionState.Resolved, resolution.State);
        Assert.NotEqual(ArtifactResolutionState.NotFound, resolution.State);
    }

    [Fact]
    public async Task DuplicateFrameBasenamesMaterializeToDistinctCachePaths()
    {
        using var fixture = new ArtifactFixture();
        await fixture.RequireNamedStreamsAsync();
        var sessionId = Guid.NewGuid();
        var source = Path.Combine(fixture.Root, "duplicate-source");
        Directory.CreateDirectory(Path.Combine(source, "frames", "a"));
        Directory.CreateDirectory(Path.Combine(source, "frames", "b"));
        await File.WriteAllTextAsync(Path.Combine(source, "manifest.json"), "{\"schemaVersion\":1}");
        await File.WriteAllBytesAsync(Path.Combine(source, "frames", "a", "image.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(source, "frames", "b", "image.png"), [2]);
        var zipPath = Path.Combine(fixture.Root, "duplicates.zip");
        var bundle = new EmbeddedBundle();
        var descriptor = await bundle.CreateAsync(
            source,
            fixture.TracePath,
            sessionId,
            EtwSnapTraceParser.ProviderId,
            1,
            zipPath,
            default);
        var store = new NamedStreamStore();
        var streamName = EmbeddedArtifactConstants.GetStreamName(sessionId);
        await store.WriteFromFileAsync(fixture.TracePath, streamName, zipPath, default);
        var first = new EmbeddedFrameReference(
            fixture.TracePath, streamName, sessionId, "frames/a/image.png", descriptor, descriptor.PrimaryEtlSha256!);
        var second = new EmbeddedFrameReference(
            fixture.TracePath, streamName, sessionId, "frames/b/image.png", descriptor, descriptor.PrimaryEtlSha256!);

        var firstPath = first.Materialize();
        var secondPath = second.Materialize();
        try
        {
            Assert.NotEqual(firstPath, secondPath);
            Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(firstPath));
            Assert.Equal(new byte[] { 2 }, await File.ReadAllBytesAsync(secondPath));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static FrameCapturedEvent CreateFrame(
        Guid sessionId,
        string sourcePath,
        ulong frameNumber = 1,
        long timestampNanoseconds = 100) => new(
        Timestamp.FromNanoseconds(timestampNanoseconds),
        sessionId,
        frameNumber,
        1233 + checked((long)frameNumber),
        5677 + checked((long)frameNumber),
        2,
        2,
        1)
    {
        SourcePath = sourcePath,
    };

    private static ArtifactReferenceEvent CreateReference(Guid sessionId, string directory, string sourcePath) => new(
        Timestamp.FromNanoseconds(50),
        sessionId,
        2,
        Path.Combine(directory, "trace.etwsnap.zip"),
        "trace.etwsnap.zip",
        2,
        2,
        0)
    {
        SourcePath = sourcePath,
    };

    private sealed class ArtifactFixture : IDisposable
    {
        public ArtifactFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"etwsnap-wpa-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            TracePath = Path.Combine(Root, "trace.etl");
            File.WriteAllBytes(TracePath, "etl-primary"u8.ToArray());
        }

        public string Root { get; }
        public string TracePath { get; }

        public async Task RequireNamedStreamsAsync()
        {
            try
            {
                await new NamedStreamStore().PreflightAsync(Root, default);
            }
            catch (IOException)
            {
                throw SkipException.ForSkip("The test volume does not support writable named streams.");
            }
        }

        public async Task WriteEmbeddedBundleAsync(Guid sessionId, IReadOnlyList<byte[]> imageBytes)
        {
            var zipPath = Path.Combine(Root, $"{sessionId:N}.zip");
            await WriteBundleAsync(sessionId, imageBytes, zipPath);
            await new NamedStreamStore().WriteFromFileAsync(
                TracePath,
                EmbeddedArtifactConstants.GetStreamName(sessionId),
                zipPath,
                default);
            File.Delete(zipPath);
        }

        public Task<string> WriteSidecarBundleAsync(
            Guid sessionId,
            IReadOnlyList<byte[]> imageBytes,
            bool qualifyWithSessionId = false,
            string? framePathOverride = null)
        {
            var stem = Path.GetFileNameWithoutExtension(TracePath);
            var name = qualifyWithSessionId ? $"{stem}.{sessionId:N}" : stem;
            return WriteBundleAsync(
                sessionId,
                imageBytes,
                Path.Combine(Root, EmbeddedArtifactConstants.GetArtifactFileName(name)),
                framePathOverride);
        }

        public Task<string> WriteScreenshotOnlyBundleAsync(Guid sessionId, IReadOnlyList<byte[]> imageBytes) =>
            WriteBundleAsync(
                sessionId,
                imageBytes,
                Path.Combine(Root, EmbeddedArtifactConstants.GetArtifactFileName("screenshots")),
                bindToEtl: false);

        private async Task<string> WriteBundleAsync(
            Guid sessionId,
            IReadOnlyList<byte[]> imageBytes,
            string zipPath,
            string? framePathOverride = null,
            bool bindToEtl = true)
        {
            var source = Path.Combine(Root, "embedded-source");
            Directory.CreateDirectory(Path.Combine(source, "frames"));
            var frameCount = framePathOverride is null ? imageBytes.Count : 1;
            var frames = Enumerable.Range(0, frameCount).Select(index => new
            {
                frameNumber = checked((ulong)index + 1),
                presentationTime100ns = 1234L + index,
                callbackQpc = 5678L + index,
                width = 2,
                height = 2,
                pixelFormat = 1,
                path = framePathOverride ?? $"frames/frame_{index + 1:D8}.png",
                bytes = index < imageBytes.Count ? imageBytes[index] : null,
            }).ToArray();
            var manifest = new
            {
                schemaVersion = 2,
                sessionId,
                startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                stoppedAtUtc = DateTimeOffset.UtcNow,
                qpcFrequency = 10_000_000,
                capture = new
                {
                    trace = bindToEtl,
                    target = new { kind = 0, handle = 0 },
                    framesPerSecond = 30,
                    bufferMegabytes = 500,
                    captureCursor = true,
                },
                provider = new { name = "ETWSnap-Service", id = EtwSnapTraceParser.ProviderId },
                trace = bindToEtl ? new
                {
                    instanceName = "test",
                    supplementalProfilePath = "EtwSnap.wprp",
                    supplementalProfileHash = "hash",
                    userProfilePath = (string?)null,
                    userProfileSelector = (string?)null,
                    userProfileHash = (string?)null,
                } : null,
                statistics = new
                {
                    acceptedFrames = frames.Length,
                    retainedFrames = frames.Length,
                    evictedFrames = 0,
                    droppedFrames = 0,
                    nativeErrors = 0,
                    exportedFrames = frames.Length,
                    failedFrames = 0,
                },
                frames = frames.Select(frame => new
                {
                    frame.frameNumber,
                    frame.presentationTime100ns,
                    frame.callbackQpc,
                    frame.width,
                    frame.height,
                    frame.pixelFormat,
                    frame.path,
                }),
                marker = "embedded",
            };
            await File.WriteAllBytesAsync(
                Path.Combine(source, "manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(manifest));
            foreach (var frame in frames)
            {
                if (frame.bytes is not null && !frame.path.Contains("..", StringComparison.Ordinal))
                {
                    await File.WriteAllBytesAsync(Path.Combine(source, frame.path), frame.bytes);
                }
            }
            await new EmbeddedBundle().CreateAsync(
                source,
                bindToEtl ? TracePath : null,
                sessionId,
                EtwSnapTraceParser.ProviderId,
                2,
                zipPath,
                default);
            return zipPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

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