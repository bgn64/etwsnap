using System.Text.Json;
using EtwSnap.Artifacts;
using EtwSnap.WpaPlugin.Artifacts;
using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using EtwSnap.WpaPlugin.Tables;
using Microsoft.Performance.SDK;
using Xunit.Sdk;

namespace EtwSnap.WpaPlugin.Tests.Artifacts;

public sealed class ArtifactResolverTests
{
    [Fact]
    public void ResolvesMovedPortableSessionByFullSessionId()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var sessionDirectory = fixture.PortableSessionDirectory(sessionId);
        var imagePath = fixture.WriteManifest(sessionDirectory, sessionId, "frames/frame_00000001.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllBytes(imagePath, [1, 2, 3]);
        var frame = CreateFrame(sessionId, fixture.TracePath);

        var resolution = new ArtifactResolver().Resolve(sessionId, [frame]);
        var dataSet = EtwSnapDataSet.Build([frame]);

        Assert.Equal(ArtifactResolutionState.Resolved, resolution.State);
        Assert.Equal(imagePath, resolution.FramePaths[1]);
        var screenshot = Assert.Single(dataSet.Screenshots);
        Assert.Equal(ScreenshotAvailability.Saved, screenshot.Availability);
        Assert.Equal(imagePath, screenshot.ImagePath);
        Assert.Equal(1UL, Assert.Single(dataSet.Sessions).ExportedFrames);
    }

    [Fact]
    public void DistinguishesMissingFileFromFrameNotPersisted()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        fixture.WriteManifest(fixture.PortableSessionDirectory(sessionId), sessionId, "frames/frame_00000001.png");
        var savedFrame = CreateFrame(sessionId, fixture.TracePath);
        var evictedFrame = savedFrame with { FrameNumber = 2, Timestamp = Timestamp.FromNanoseconds(200) };

        var dataSet = EtwSnapDataSet.Build([savedFrame, evictedFrame]);

        Assert.Equal(ScreenshotAvailability.MissingFile, dataSet.Screenshots[0].Availability);
        Assert.Equal(ScreenshotAvailability.NotPersisted, dataSet.Screenshots[1].Availability);
    }

    [Fact]
    public void RejectsFramePathTraversal()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        fixture.WriteManifest(fixture.PortableSessionDirectory(sessionId), sessionId, "../outside.png");

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.Equal(ArtifactResolutionState.InvalidManifest, resolution.State);
    }

    [Fact]
    public void RejectsNullStatistics()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var sessionDirectory = fixture.PortableSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "manifest.json"),
            $$"""{"schemaVersion":1,"sessionId":"{{sessionId}}","provider":{"name":"ETWSnap-Service","id":"{{EtwSnapTraceParser.ProviderId}}"},"statistics":null,"frames":[]}""");

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.Equal(ArtifactResolutionState.InvalidManifest, resolution.State);
    }

    [Fact]
    public void ReportsUnsupportedManifestSchema()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        fixture.WriteManifest(fixture.PortableSessionDirectory(sessionId), sessionId, "frames/frame_00000001.png", schemaVersion: 2);

        var resolution = new ArtifactResolver().Resolve(sessionId, [CreateFrame(sessionId, fixture.TracePath)]);

        Assert.Equal(ArtifactResolutionState.UnsupportedVersion, resolution.State);
    }

    [Fact]
    public void UsesCanonicalPortableLayoutWhenEventHintIsStale()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        fixture.WriteManifest(fixture.PortableSessionDirectory(sessionId), sessionId, "frames/frame_00000001.png");
        var reference = CreateReference(sessionId, Path.Combine(fixture.Root, "missing"), fixture.TracePath) with
        {
            PortableManifestRelativePath = "stale/manifest.json",
        };

        var resolution = new ArtifactResolver().Resolve(
            sessionId,
            [CreateFrame(sessionId, fixture.TracePath), reference]);

        Assert.Equal(ArtifactResolutionState.Resolved, resolution.State);
        Assert.Equal(
            Path.Combine(fixture.PortableSessionDirectory(sessionId), "manifest.json"),
            resolution.ManifestPath);
    }

    [Fact]
    public void PrefersOriginalPathForIdenticalCandidates()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var originalDirectory = Path.Combine(fixture.Root, "original");
        fixture.WriteManifest(originalDirectory, sessionId, "frames/frame_00000001.png");
        fixture.WriteManifest(fixture.Root, sessionId, "frames/frame_00000001.png");
        var reference = CreateReference(sessionId, originalDirectory, fixture.TracePath);

        var resolution = new ArtifactResolver().Resolve(
            sessionId,
            [CreateFrame(sessionId, fixture.TracePath), reference]);

        Assert.Equal(Path.Combine(originalDirectory, "manifest.json"), resolution.ManifestPath);
    }

    [Fact]
    public void RejectsCommittedHashMismatch()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var sessionDirectory = fixture.PortableSessionDirectory(sessionId);
        fixture.WriteManifest(sessionDirectory, sessionId, "frames/frame_00000001.png");
        var reference = CreateReference(sessionId, sessionDirectory, fixture.TracePath);
        var committed = new ArtifactCommittedEvent(
            Timestamp.FromNanoseconds(300),
            sessionId,
            1,
            reference.ArtifactDirectory,
            reference.SessionDirectoryName,
            reference.ManifestRelativePath,
            reference.PortableManifestRelativePath,
            1,
            new string('0', 64),
            "Complete",
            1,
            1,
            0,
            0,
            0,
            1,
            0)
        {
            SourcePath = fixture.TracePath,
        };

        var resolution = new ArtifactResolver().Resolve(
            sessionId,
            [CreateFrame(sessionId, fixture.TracePath), reference, committed]);

        Assert.Equal(ArtifactResolutionState.IntegrityMismatch, resolution.State);
    }

    [Fact]
    public void DoesNotChooseBetweenNonIdenticalValidCandidates()
    {
        using var fixture = new ArtifactFixture();
        var sessionId = Guid.NewGuid();
        var originalDirectory = Path.Combine(fixture.Root, "original");
        fixture.WriteManifest(originalDirectory, sessionId, "frames/frame_00000001.png", "original");
        fixture.WriteManifest(fixture.Root, sessionId, "frames/frame_00000001.png", "adjacent");
        var reference = CreateReference(sessionId, originalDirectory, fixture.TracePath);

        var resolution = new ArtifactResolver().Resolve(
            sessionId,
            [CreateFrame(sessionId, fixture.TracePath), reference]);

        Assert.Equal(ArtifactResolutionState.Ambiguous, resolution.State);
    }

    [Fact]
    public async Task EmbeddedArtifactsWinAndMaterializeDuringResolution()
    {
        using var fixture = new ArtifactFixture();
        await fixture.RequireNamedStreamsAsync();
        var sessionId = Guid.NewGuid();
        var adjacentImage = fixture.WriteManifest(fixture.Root, sessionId, "frames/frame_00000001.png", "folder");
        Directory.CreateDirectory(Path.GetDirectoryName(adjacentImage)!);
        File.WriteAllBytes(adjacentImage, [1, 2, 3]);
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
    public async Task InvalidEmbeddedStreamIsNotMaskedByValidFolder()
    {
        using var fixture = new ArtifactFixture();
        await fixture.RequireNamedStreamsAsync();
        var sessionId = Guid.NewGuid();
        fixture.WriteManifest(fixture.Root, sessionId, "frames/frame_00000001.png");
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
        var first = new EmbeddedFrameReference(fixture.TracePath, streamName, sessionId, "frames/a/image.png", descriptor);
        var second = new EmbeddedFrameReference(fixture.TracePath, streamName, sessionId, "frames/b/image.png", descriptor);

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
        1,
        directory,
        Path.GetFileName(directory),
        "manifest.json",
        $"sessions/{sessionId:N}/manifest.json",
        1)
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

        public string PortableSessionDirectory(Guid sessionId) =>
            Path.Combine(Root, "sessions", sessionId.ToString("N"));

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
            var source = Path.Combine(Root, "embedded-source");
            Directory.CreateDirectory(Path.Combine(source, "frames"));
            var frames = imageBytes.Select((bytes, index) => new
            {
                frameNumber = checked((ulong)index + 1),
                presentationTime100ns = 1234L + index,
                callbackQpc = 5678L + index,
                width = 2,
                height = 2,
                pixelFormat = 1,
                path = $"frames/frame_{index + 1:D8}.png",
                bytes,
            }).ToArray();
            var manifest = new
            {
                schemaVersion = 1,
                sessionId,
                provider = new { name = "ETWSnap-Service", id = EtwSnapTraceParser.ProviderId },
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
                await File.WriteAllBytesAsync(Path.Combine(source, frame.path), frame.bytes);
            }
            var zipPath = Path.Combine(Root, $"{sessionId:N}.zip");
            await new EmbeddedBundle().CreateAsync(
                source,
                TracePath,
                sessionId,
                EtwSnapTraceParser.ProviderId,
                1,
                zipPath,
                default);
            await new NamedStreamStore().WriteFromFileAsync(
                TracePath,
                EmbeddedArtifactConstants.GetStreamName(sessionId),
                zipPath,
                default);
            File.Delete(zipPath);
        }

        public string WriteManifest(
            string sessionDirectory,
            Guid sessionId,
            string framePath,
            string? marker = null,
            int schemaVersion = 1)
        {
            Directory.CreateDirectory(sessionDirectory);
            var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
            var manifest = new
            {
                schemaVersion,
                sessionId,
                provider = new
                {
                    name = "ETWSnap-Service",
                    id = EtwSnapTraceParser.ProviderId,
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
                        presentationTime100ns = 1234,
                        callbackQpc = 5678,
                        width = 2,
                        height = 2,
                        pixelFormat = 1,
                        path = framePath,
                    },
                },
                marker,
            };
            File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
            return Path.GetFullPath(Path.Combine(sessionDirectory, framePath));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}