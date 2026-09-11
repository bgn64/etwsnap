using EtwSnap.Artifacts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Artifacts;
using Xunit.Sdk;

namespace EtwSnap.IntegrationTests.Artifacts;

public sealed class EmbeddedArtifactPublisherTests
{
    [Fact]
    public async Task PublishMovesVerifiedStreamWithPrimaryEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(fixture.OutputRoot, fixture.SessionId, fixture.StartedAtUtc, default);
        await fixture.PopulateAsync(reservation.Staging);

        var publication = await publisher.PublishAsync(reservation, fixture.Session, null, default);

        Assert.Equal(ArtifactTransport.Embedded, publication.Transport);
        Assert.Equal(reservation.FinalEtlPath, publication.TracePath);
        Assert.True(File.Exists(reservation.FinalEtlPath));
        Assert.False(Directory.Exists(reservation.Staging.DirectoryPath));
        var inspection = Assert.Single(await new EmbeddedArtifactManager().InspectAsync(reservation.FinalEtlPath, default));
        Assert.True(inspection.IsValid);
        Assert.Equal(fixture.SessionId, inspection.SessionId);
        Assert.DoesNotContain(inspection.Bundle!.Descriptor.Entries, entry => entry.Path == "trace.etl");
    }

    [Fact]
    public async Task PublicationFailureFallsBackToFolderWithCleanPrimaryEtl()
    {
        using var fixture = await PublisherFixture.CreateAsync();
        var publisher = new EmbeddedArtifactPublisher();
        var reservation = await publisher.ReserveAsync(fixture.OutputRoot, fixture.SessionId, fixture.StartedAtUtc, default);
        await fixture.PopulateAsync(reservation.Staging);

        var publication = await publisher.PublishAsync(reservation, fixture.Session, "forced packaging failure", default);

        Assert.Equal(ArtifactTransport.Folder, publication.Transport);
        Assert.Equal(reservation.FallbackDirectoryPath, publication.OutputDirectory);
        Assert.Contains("forced packaging failure", publication.Warning);
        Assert.True(File.Exists(publication.TracePath));
        Assert.True(File.Exists(publication.ManifestPath));
        Assert.Empty(new NamedStreamStore().EnumerateEtwSnapStreams(publication.TracePath!));
        Assert.Equal(fixture.PrimaryBytes, await File.ReadAllBytesAsync(publication.TracePath!));
        Assert.False(File.Exists(Path.Combine(publication.OutputDirectory!, ".reserved")));
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

        public async Task PopulateAsync(ArtifactReservation reservation)
        {
            Directory.CreateDirectory(reservation.FramesPath);
            await File.WriteAllBytesAsync(reservation.TracePath, PrimaryBytes);
            await File.WriteAllTextAsync(reservation.ManifestPath, "{\"schemaVersion\":1}");
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