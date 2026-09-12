using EtwSnap.Artifacts;
using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Artifacts;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Sdk;

namespace EtwSnap.IntegrationTests.Artifacts;

public sealed class EmbeddedArtifactPublisherTests
{
    [Fact]
    public async Task PublishMovesVerifiedStreamWithPrimaryEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(
            fixture.OutputRoot,
            fixture.SessionId,
            fixture.StartedAtUtc,
            ArtifactTransport.Embedded,
            true,
            default);
        await fixture.PopulateAsync(reservation.Staging);

        var publication = await publisher.PublishAsync(reservation, fixture.Session, ArtifactTransport.Embedded, null, default);

        Assert.Equal(ArtifactTransport.Embedded, publication.Transport);
        Assert.Null(publication.ArtifactZipPath);
        Assert.Equal(reservation.FinalEtlPath, publication.TracePath);
        Assert.True(File.Exists(reservation.FinalEtlPath));
        Assert.Empty(Directory.GetFiles(fixture.OutputRoot, "*.etwsnap.zip"));
        Assert.False(Directory.Exists(reservation.Staging.DirectoryPath));
        var inspection = Assert.Single(await new EmbeddedArtifactManager().InspectAsync(reservation.FinalEtlPath, default));
        Assert.True(inspection.IsValid);
        Assert.Equal(fixture.SessionId, inspection.SessionId);
        Assert.DoesNotContain(inspection.Bundle!.Descriptor.Entries, entry => entry.Path == "trace.etl");
        await using var embeddedStream = new NamedStreamStore().OpenRead(
            publication.TracePath!,
            EmbeddedArtifactConstants.GetStreamName(fixture.SessionId));
        Assert.Equal(
            publication.ArtifactSha256,
            Convert.ToHexString(SHA256.HashData(embeddedStream)).ToLowerInvariant());
    }

    [Fact]
    public async Task EmbeddedFailureFallsBackToSidecarZipWithCleanPrimaryEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(
            fixture.OutputRoot,
            fixture.SessionId,
            fixture.StartedAtUtc,
            ArtifactTransport.Embedded,
            true,
            default);
        await fixture.PopulateAsync(reservation.Staging);
        await File.WriteAllBytesAsync(
            new NamedStreamStore().GetStreamPath(
                reservation.Staging.TracePath,
                EmbeddedArtifactConstants.GetStreamName(fixture.SessionId)),
            "collision"u8.ToArray());

        var publication = await publisher.PublishAsync(
            reservation,
            fixture.Session,
            ArtifactTransport.Embedded,
            null,
            default);

        Assert.Equal(ArtifactTransport.Sidecar, publication.Transport);
        Assert.Equal(reservation.FinalZipPath, publication.ArtifactZipPath);
        Assert.Contains("Embedded artifact publication failed", publication.Warning);
        Assert.True(File.Exists(publication.TracePath));
        Assert.True(File.Exists(publication.ArtifactZipPath));
        Assert.Equal(
            Path.GetFileNameWithoutExtension(publication.TracePath),
            Path.GetFileName(publication.ArtifactZipPath)[..^EmbeddedArtifactConstants.ArtifactFileExtension.Length]);
        Assert.False(Directory.Exists(reservation.Staging.DirectoryPath));
        Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(publication.TracePath!));
        Assert.Equal(fixture.PrimaryBytes, await File.ReadAllBytesAsync(publication.TracePath!));
        var archive = await ArtifactArchive.ValidateAsync(
            publication.ArtifactZipPath!,
            publication.TracePath,
            EtwSnapConstants.ProviderId,
            EtwSnapConstants.ManifestSchemaVersion,
            default);
        Assert.DoesNotContain(archive.Bundle.Descriptor.Entries, entry => entry.Path.EndsWith(".etl", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            publication.ArtifactSha256,
            await EmbeddedBundle.HashFileAsync(publication.ArtifactZipPath!, default));
    }

    [Fact]
    public async Task TraceFailurePublishesTraceLessZipWithoutEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(
            fixture.OutputRoot,
            fixture.SessionId,
            fixture.StartedAtUtc,
            ArtifactTransport.Embedded,
            true,
            default);
        await fixture.PopulateAsync(reservation.Staging, includeTrace: false);

        var publication = await publisher.PublishAsync(
            reservation,
            fixture.Session,
            ArtifactTransport.Embedded,
            "WPR stop failed",
            default);

        Assert.Equal(ArtifactTransport.Sidecar, publication.Transport);
        Assert.Null(publication.TracePath);
        Assert.True(File.Exists(publication.ArtifactZipPath));
        Assert.Empty(Directory.GetFiles(fixture.OutputRoot, "*.etl"));
        var archive = await ArtifactArchive.ValidateAsync(
            publication.ArtifactZipPath!,
            null,
            EtwSnapConstants.ProviderId,
            EtwSnapConstants.ManifestSchemaVersion,
            default);
        Assert.Null(archive.Bundle.Descriptor.PrimaryEtlSha256);
        Assert.Null(archive.Manifest.Trace);
    }

    [Fact]
    public async Task ScreenshotOnlyPublishesZipWithoutEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(
            fixture.OutputRoot,
            fixture.SessionId,
            fixture.StartedAtUtc,
            ArtifactTransport.Sidecar,
            false,
            default);
        await fixture.PopulateAsync(reservation.Staging, includeTrace: false);

        var publication = await publisher.PublishAsync(reservation, fixture.Session with { Wpr = null }, ArtifactTransport.Sidecar, null, default);

        Assert.Equal(ArtifactTransport.Sidecar, publication.Transport);
        Assert.Null(publication.TracePath);
        Assert.True(File.Exists(publication.ArtifactZipPath));
        Assert.Empty(Directory.GetFiles(fixture.OutputRoot, "*.etl"));
        Assert.False(Directory.Exists(reservation.Staging.DirectoryPath));
        var archive = await ArtifactArchive.ValidateAsync(
            publication.ArtifactZipPath!,
            null,
            EtwSnapConstants.ProviderId,
            EtwSnapConstants.ManifestSchemaVersion,
            default);
        Assert.Null(archive.Bundle.Descriptor.PrimaryEtlSha256);
    }

    private sealed class PublisherFixture : IDisposable
    {
        private PublisherFixture(string root)
        {
            Root = root;
            OutputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(OutputRoot);
            SessionId = Guid.NewGuid();
            StartedAtUtc = DateTimeOffset.UtcNow;
            PrimaryBytes = "etl-primary-stream"u8.ToArray();
            Session = new SessionMetadata(
                SessionId,
                StartedAtUtc,
                new StartCaptureRequest(
                    true,
                    null,
                    new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                    30,
                    500,
                    true),
                null);
        }

        public string Root { get; }
        public string OutputRoot { get; }
        public Guid SessionId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public byte[] PrimaryBytes { get; }
        public SessionMetadata Session { get; }

        public static async Task<PublisherFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"etwsnap-publisher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                await new NamedStreamStore().PreflightAsync(root, default);
            }
            catch (IOException)
            {
                Directory.Delete(root, recursive: true);
                throw SkipException.ForSkip("The test volume does not support writable named streams.");
            }
            return new PublisherFixture(root);
        }

        public async Task PopulateAsync(ArtifactReservation reservation, bool includeTrace = true)
        {
            Directory.CreateDirectory(reservation.FramesPath);
            if (includeTrace)
            {
                await File.WriteAllBytesAsync(reservation.TracePath, PrimaryBytes);
            }
            var manifest = new
            {
                schemaVersion = 2,
                sessionId = SessionId,
                status = "Complete",
                startedAtUtc = StartedAtUtc,
                stoppedAtUtc = DateTimeOffset.UtcNow,
                qpcFrequency = 10_000_000,
                capture = new
                {
                    trace = includeTrace,
                    target = new { kind = 0, handle = 0 },
                    framesPerSecond = 30,
                    bufferMegabytes = 500,
                    captureCursor = true,
                },
                provider = new { name = EtwSnapConstants.ProviderName, id = EtwSnapConstants.ProviderId },
                trace = includeTrace ? new
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
            };
            await File.WriteAllBytesAsync(reservation.ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
            await File.WriteAllBytesAsync(Path.Combine(reservation.FramesPath, "frame_00000001.png"), [137, 80, 78, 71]);
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