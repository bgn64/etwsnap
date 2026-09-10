using System.Text.Json;
using EtwSnap.WpaPlugin.Artifacts;
using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;

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

    private static FrameCapturedEvent CreateFrame(Guid sessionId, string sourcePath) => new(
        Timestamp.FromNanoseconds(100),
        sessionId,
        1,
        1234,
        5678,
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
        }

        public string Root { get; }
        public string TracePath { get; }

        public string PortableSessionDirectory(Guid sessionId) =>
            Path.Combine(Root, "sessions", sessionId.ToString("N"));

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