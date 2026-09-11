using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Tracing;

namespace EtwSnap.Host.Artifacts;

internal interface IArtifactWriter
{
    ArtifactReservation Reserve(string outputRoot, Guid sessionId, DateTimeOffset startedAtUtc);
    Task<ArtifactWriteResult> WriteAsync(
        ArtifactReservation reservation,
        SessionMetadata session,
        INativeCaptureSession capture,
        NativeCaptureStats stats,
        string? tracePath,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken,
        Func<int, string, ValueTask> progress);
}

internal sealed record SessionMetadata(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    StartCaptureRequest Request,
    WprSession? Wpr);

internal sealed record ArtifactReservation(
    string DirectoryPath,
    string FramesPath,
    string ManifestPath,
    string TracePath,
    string ReservationMarker,
    string? Warning,
    bool DeferFinalization = false);

internal sealed record ArtifactWriteResult(
    string OutputDirectory,
    string ManifestPath,
    int ExportedFrames,
    int FailedFrames,
    IReadOnlyList<string> Errors,
    string ManifestSha256,
    string Status);

internal sealed class SessionArtifactWriter : IArtifactWriter
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public ArtifactReservation Reserve(string outputRoot, Guid sessionId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        var canonicalRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(canonicalRoot);

        var directoryName = GetDirectoryName(sessionId, startedAtUtc);
        var directoryPath = Path.Combine(canonicalRoot, directoryName);
        if (Directory.Exists(directoryPath))
        {
            throw new IOException($"The session output already exists: {directoryPath}");
        }

        Directory.CreateDirectory(directoryPath);
        var marker = Path.Combine(directoryPath, ".reserved");
        using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }

        var warning = GetFreeSpaceWarning(canonicalRoot);
        return new ArtifactReservation(
            directoryPath,
            Path.Combine(directoryPath, "frames"),
            Path.Combine(directoryPath, "manifest.json"),
            Path.Combine(directoryPath, "trace.etl"),
            marker,
            warning);
    }

    public async Task<ArtifactWriteResult> WriteAsync(
        ArtifactReservation reservation,
        SessionMetadata session,
        INativeCaptureSession capture,
        NativeCaptureStats stats,
        string? tracePath,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken,
        Func<int, string, ValueTask> progress)
    {
        Directory.CreateDirectory(reservation.FramesPath);
        var frames = new List<FrameManifest>();
        var errors = new List<string>();
        var frameCount = capture.GetFrameCount();
        byte[]? pixelBuffer = null;

        try
        {
            for (ulong index = 0; index < frameCount; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = capture.GetFrameInfo(index);
                if (info.RequiredBytes > int.MaxValue)
                {
                    errors.Add($"Frame {info.FrameNumber} exceeds the managed encoder size limit.");
                    continue;
                }

                var requiredLength = checked((int)info.RequiredBytes);
                if (pixelBuffer is null || pixelBuffer.Length < requiredLength)
                {
                    if (pixelBuffer is not null)
                    {
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                    }
                    pixelBuffer = ArrayPool<byte>.Shared.Rent(requiredLength);
                }

                try
                {
                    var stride = checked(info.Width * 4u);
                    capture.CopyFrameBgra(index, pixelBuffer, stride);
                    var relativePath = Path.Combine("frames", $"frame_{info.FrameNumber:D8}.png");
                    SavePng(pixelBuffer, info.Width, info.Height, stride, Path.Combine(reservation.DirectoryPath, relativePath));
                    frames.Add(new FrameManifest(
                        info.FrameNumber,
                        info.PresentationTime100ns,
                        info.CallbackQpc,
                        info.Width,
                        info.Height,
                        info.PixelFormat,
                        relativePath.Replace('\\', '/')));
                }
                catch (Exception exception)
                {
                    errors.Add($"Frame {info.FrameNumber}: {exception.Message}");
                }

                var percent = frameCount == 0 ? 100 : checked((int)(((index + 1) * 100) / frameCount));
                await progress(percent, $"Saving screenshots ({index + 1}/{frameCount})").ConfigureAwait(false);
            }

            if (frameCount == 0)
            {
                await progress(100, "No retained screenshots to save").ConfigureAwait(false);
            }
        }
        finally
        {
            if (pixelBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(pixelBuffer);
            }
        }

        var manifestWarnings = warnings.ToList();
        if (session.Wpr is not null)
        {
            try
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(reservation.DirectoryPath, "EtwSnap.wprp"),
                    session.Wpr.StagedSupplementalProfileBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                manifestWarnings.Add($"The supplemental profile could not be copied: {exception.Message}");
            }
        }

        var allWarnings = manifestWarnings.Concat(reservation.Warning is null ? [] : new[] { reservation.Warning }).ToArray();
        var status = errors.Count == 0 && manifestWarnings.Count == 0 ? "Complete" : "Partial";
        var manifest = new SessionManifest(
            EtwSnapConstants.ManifestSchemaVersion,
            session.SessionId,
            status,
            session.StartedAtUtc,
            DateTimeOffset.UtcNow,
            Stopwatch.Frequency,
            session.Request,
            new ProviderManifest(EtwSnapConstants.ProviderName, EtwSnapConstants.ProviderId),
            session.Wpr is null ? null : new TraceManifest(
                session.Wpr.InstanceName,
                tracePath is null ? null : Path.GetFileName(tracePath),
                "EtwSnap.wprp",
                session.Wpr.SupplementalProfileHash,
                session.Wpr.UserProfilePath,
                session.Wpr.UserProfileSelector,
                session.Wpr.UserProfileHash),
            new StatisticsManifest(
                stats.ObservedFrames,
                stats.AcceptedFrames,
                stats.RetainedFrames,
                stats.EvictedFrames,
                stats.DroppedFrames,
                stats.ErrorCount,
                frames.Count,
                errors.Count),
            frames,
            allWarnings,
            errors);

        var temporaryManifest = reservation.ManifestPath + ".tmp";
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);
        await File.WriteAllBytesAsync(temporaryManifest, manifestBytes, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryManifest, reservation.ManifestPath, overwrite: true);
        if (!reservation.DeferFinalization)
        {
            File.Delete(reservation.ReservationMarker);
        }
        var manifestSha256 = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

        return new ArtifactWriteResult(
            reservation.DirectoryPath,
            reservation.ManifestPath,
            frames.Count,
            errors.Count,
            errors,
            manifestSha256,
            status);
    }

    private static void SavePng(byte[] pixels, uint width, uint height, uint sourceStride, string path)
    {
        using var bitmap = new Bitmap(checked((int)width), checked((int)height), PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked((int)(width * 4u));
            for (var row = 0; row < bitmap.Height; ++row)
            {
                Marshal.Copy(
                    pixels,
                    checked((int)(row * sourceStride)),
                    IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride),
                    rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        var temporaryPath = path + ".tmp";
        bitmap.Save(temporaryPath, ImageFormat.Png);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string? GetFreeSpaceWarning(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (root is null)
            {
                return null;
            }
            var drive = new DriveInfo(root);
            const long warningThreshold = 1024L * 1024L * 1024L;
            return drive.AvailableFreeSpace < warningThreshold
                ? $"The output volume has less than 1 GiB free ({drive.AvailableFreeSpace} bytes)."
                : null;
        }
        catch
        {
            return "Available output disk space could not be determined.";
        }
    }

    internal static string GetDirectoryName(Guid sessionId, DateTimeOffset startedAtUtc) =>
        $"etwsnap-{startedAtUtc.UtcDateTime:yyyyMMddTHHmmssZ}-{sessionId.ToString("N")[..8]}";
}
