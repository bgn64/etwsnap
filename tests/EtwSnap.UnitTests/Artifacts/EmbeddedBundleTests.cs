using EtwSnap.Artifacts;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace EtwSnap.UnitTests.Artifacts;

public sealed class EmbeddedBundleTests
{
    [Fact]
    public async Task BundleRoundTripPreservesManifestAndFrame()
    {
        using var fixture = new BundleFixture();
        var bundle = new EmbeddedBundle();
        var sessionId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var zipPath = Path.Combine(fixture.Root, "bundle.zip");

        var descriptor = await bundle.CreateAsync(
            fixture.SessionDirectory,
            fixture.EtlPath,
            sessionId,
            providerId,
            1,
            zipPath,
            default);
        await using var stream = File.OpenRead(zipPath);
        var inspection = await bundle.InspectAsync(stream, fixture.EtlPath, sessionId, default);

        Assert.Equal(sessionId, descriptor.SessionId);
        Assert.Equal(providerId, inspection.Descriptor.ProviderId);
        Assert.Equal(fixture.ManifestBytes, inspection.ManifestBytes);
        Assert.Contains(inspection.Descriptor.Entries, entry => entry.Path == "frames/frame_00000001.png");
    }

    [Theory]
    [InlineData("../manifest.json")]
    [InlineData("/manifest.json")]
    [InlineData("C:/manifest.json")]
    [InlineData("frames//image.png")]
    public void UnsafeEntryNamesAreRejected(string path)
    {
        Assert.Throws<EmbeddedArtifactException>(() => EmbeddedBundle.NormalizeEntryName(path));
    }

    [Fact]
    public void StreamNamesRoundTripFullSessionId()
    {
        var sessionId = Guid.NewGuid();
        var name = EmbeddedArtifactConstants.GetStreamName(sessionId);

        Assert.True(EmbeddedArtifactConstants.TryParseStreamName(name, out var parsed));
        Assert.Equal(sessionId, parsed);
        Assert.False(EmbeddedArtifactConstants.TryParseStreamName("Other.Stream", out _));
    }

    [Fact]
    public async Task InspectionRejectsNonCanonicalArchiveEntryName()
    {
        using var fixture = new BundleFixture();
        var sessionId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var zipPath = Path.Combine(fixture.Root, "noncanonical.zip");
        var frameBytes = new byte[] { 137, 80, 78, 71 };
        var descriptor = new EmbeddedBundleDescriptor(
            1,
            sessionId,
            providerId,
            DateTimeOffset.UtcNow,
            1,
            Hash(fixture.ManifestBytes),
            await EmbeddedBundle.HashFileAsync(fixture.EtlPath, default),
            [
                new EmbeddedBundleEntry("manifest.json", fixture.ManifestBytes.Length, Hash(fixture.ManifestBytes)),
                new EmbeddedBundleEntry("frames/frame.png", frameBytes.Length, Hash(frameBytes)),
            ]);
        await using (var output = File.Create(zipPath))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "bundle.json", JsonSerializer.SerializeToUtf8Bytes(descriptor, EmbeddedBundleJson.Options));
            await WriteEntryAsync(archive, "manifest.json", fixture.ManifestBytes);
            await WriteEntryAsync(archive, "frames\\frame.png", frameBytes);
        }

        await using var stream = File.OpenRead(zipPath);
        var exception = await Assert.ThrowsAsync<EmbeddedArtifactException>(() =>
            new EmbeddedBundle().InspectAsync(stream, fixture.EtlPath, sessionId, default));

        Assert.Contains("not canonical", exception.Message);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
        await stream.WriteAsync(bytes);
    }

    private sealed class BundleFixture : IDisposable
    {
        public BundleFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"etwsnap-bundle-{Guid.NewGuid():N}");
            SessionDirectory = Path.Combine(Root, "session");
            Directory.CreateDirectory(Path.Combine(SessionDirectory, "frames"));
            EtlPath = Path.Combine(SessionDirectory, "trace.etl");
            ManifestBytes = "{\"schemaVersion\":1}"u8.ToArray();
            File.WriteAllBytes(EtlPath, "etl-primary"u8.ToArray());
            File.WriteAllBytes(Path.Combine(SessionDirectory, "manifest.json"), ManifestBytes);
            File.WriteAllBytes(Path.Combine(SessionDirectory, "frames", "frame_00000001.png"), [137, 80, 78, 71]);
        }

        public string Root { get; }
        public string SessionDirectory { get; }
        public string EtlPath { get; }
        public byte[] ManifestBytes { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}