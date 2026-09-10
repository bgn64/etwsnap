using System.Text.Json;
using System.Security.Cryptography;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Tracing;

namespace EtwSnap.UnitTests.Artifacts;

public sealed class SessionArtifactWriterTests
{
    [Fact]
    public async Task WritesPngAndMatchingManifestFrameMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-test-{Guid.NewGuid():N}");
        try
        {
            var writer = new SessionArtifactWriter();
            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            var request = new StartCaptureRequest(
                false,
                null,
                new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                30,
                8,
                true);
            var reservation = writer.Reserve(root, sessionId, startedAt);
            using var capture = new SingleFrameCapture();

            var result = await writer.WriteAsync(
                reservation,
                new SessionMetadata(sessionId, startedAt, request, null),
                capture,
                capture.GetStats(),
                null,
                [],
                default,
                (_, _) => ValueTask.CompletedTask);

            Assert.Equal(1, result.ExportedFrames);
            var pngPath = Path.Combine(result.OutputDirectory, "frames", "frame_00000042.png");
            var signature = (await File.ReadAllBytesAsync(pngPath)).Take(8).ToArray();
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath));
            Assert.Equal(sessionId, manifest.RootElement.GetProperty("sessionId").GetGuid());
            var frame = manifest.RootElement.GetProperty("frames")[0];
            Assert.Equal(42UL, frame.GetProperty("frameNumber").GetUInt64());
            Assert.Equal(1234, frame.GetProperty("presentationTime100ns").GetInt64());
            Assert.Equal(5678, frame.GetProperty("callbackQpc").GetInt64());
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(result.ManifestPath))),
                result.ManifestSha256);
            Assert.Equal("Complete", result.Status);
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
    public async Task WritesExactSupplementalProfileContents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-profile-artifact-{Guid.NewGuid():N}");
        var profileContents = "<WindowsPerformanceRecorder Version=\"staged\" />"u8.ToArray();
        try
        {
            var writer = new SessionArtifactWriter();
            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            var request = new StartCaptureRequest(
                true,
                null,
                new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
                30,
                8,
                true);
            var reservation = writer.Reserve(root, sessionId, startedAt);
            var wpr = new WprSession("test", profileContents, "hash", null, null, null, "staging");
            using var capture = new SingleFrameCapture();

            var result = await writer.WriteAsync(
                reservation,
                new SessionMetadata(sessionId, startedAt, request, wpr),
                capture,
                capture.GetStats(),
                "trace.etl",
                [],
                default,
                (_, _) => ValueTask.CompletedTask);

            Assert.Equal(profileContents, await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, "EtwSnap.wprp")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class SingleFrameCapture : INativeCaptureSession
    {
        public void Start()
        {
        }

        public void Stop()
        {
        }

        public NativeCaptureStats GetStats() => new(1, 1, 1, 0, 0, 0);

        public ulong GetFrameCount() => 1;

        public NativeFrameInfo GetFrameInfo(ulong index) => new(42, 1234, 5678, 2, 2, 1, 16);

        public void CopyFrameBgra(ulong index, byte[] destination, uint stride)
        {
            var pixels = new byte[]
            {
                0, 0, 255, 255,
                0, 255, 0, 255,
                255, 0, 0, 255,
                255, 255, 255, 255,
            };
            pixels.CopyTo(destination, 0);
        }

        public void Dispose()
        {
        }
    }
}