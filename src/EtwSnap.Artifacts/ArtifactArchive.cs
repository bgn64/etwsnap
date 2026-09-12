using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtwSnap.Artifacts;

public sealed record ArtifactSessionManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("startedAtUtc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("stoppedAtUtc")] DateTimeOffset StoppedAtUtc,
    [property: JsonPropertyName("qpcFrequency")] long QpcFrequency,
    [property: JsonPropertyName("capture")] ArtifactCaptureManifest? Capture,
    [property: JsonPropertyName("provider")] ArtifactProviderManifest? Provider,
    [property: JsonPropertyName("trace")] ArtifactTraceManifest? Trace,
    [property: JsonPropertyName("statistics")] ArtifactStatisticsManifest? Statistics,
    [property: JsonPropertyName("frames")] IReadOnlyList<ArtifactFrameManifest>? Frames);

public sealed record ArtifactCaptureManifest(
    [property: JsonPropertyName("trace")] bool Trace,
    [property: JsonPropertyName("target")] ArtifactCaptureTargetManifest? Target,
    [property: JsonPropertyName("framesPerSecond")] int FramesPerSecond,
    [property: JsonPropertyName("bufferMegabytes")] long BufferMegabytes,
    [property: JsonPropertyName("captureCursor")] bool CaptureCursor);

public sealed record ArtifactCaptureTargetManifest(
    [property: JsonPropertyName("kind")] int Kind,
    [property: JsonPropertyName("handle")] long Handle);

public sealed record ArtifactProviderManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] Guid Id);

public sealed record ArtifactTraceManifest(
    [property: JsonPropertyName("instanceName")] string InstanceName,
    [property: JsonPropertyName("supplementalProfilePath")] string SupplementalProfilePath,
    [property: JsonPropertyName("supplementalProfileHash")] string SupplementalProfileHash,
    [property: JsonPropertyName("userProfilePath")] string? UserProfilePath,
    [property: JsonPropertyName("userProfileSelector")] string? UserProfileSelector,
    [property: JsonPropertyName("userProfileHash")] string? UserProfileHash);

public sealed record ArtifactStatisticsManifest(
    [property: JsonPropertyName("acceptedFrames")] ulong AcceptedFrames,
    [property: JsonPropertyName("retainedFrames")] ulong RetainedFrames,
    [property: JsonPropertyName("evictedFrames")] ulong EvictedFrames,
    [property: JsonPropertyName("droppedFrames")] ulong DroppedFrames,
    [property: JsonPropertyName("nativeErrors")] ulong NativeErrors,
    [property: JsonPropertyName("exportedFrames")] int ExportedFrames,
    [property: JsonPropertyName("failedFrames")] int FailedFrames);

public sealed record ArtifactFrameManifest(
    [property: JsonPropertyName("frameNumber")] ulong FrameNumber,
    [property: JsonPropertyName("presentationTime100ns")] long PresentationTime100ns,
    [property: JsonPropertyName("callbackQpc")] long CallbackQpc,
    [property: JsonPropertyName("width")] uint Width,
    [property: JsonPropertyName("height")] uint Height,
    [property: JsonPropertyName("pixelFormat")] uint PixelFormat,
    [property: JsonPropertyName("path")] string Path);

public sealed record ValidatedArtifactArchive(
    string ArchivePath,
    ArtifactSessionManifest Manifest,
    EmbeddedBundleInspection Bundle);

public static class ArtifactArchive
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ValidatedArtifactArchive> ValidateAsync(
        string archivePath,
        string? etlPath,
        Guid expectedProviderId,
        int expectedManifestSchemaVersion,
        CancellationToken cancellationToken)
    {
        var canonicalArchive = Path.GetFullPath(archivePath);
        if (!File.Exists(canonicalArchive) ||
            !canonicalArchive.EndsWith(EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddedArtifactException($"A regular existing {EmbeddedArtifactConstants.ArtifactFileExtension} file is required: {canonicalArchive}");
        }
        NamedStreamStore.RejectReparsePoint(canonicalArchive);

        await using var stream = File.OpenRead(canonicalArchive);
        var bundle = await new EmbeddedBundle().InspectAsync(stream, etlPath, null, cancellationToken).ConfigureAwait(false);
        ArtifactSessionManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ArtifactSessionManifest>(bundle.ManifestBytes, ManifestJson)
                ?? throw new EmbeddedArtifactException("The artifact manifest is invalid.");
        }
        catch (JsonException exception)
        {
            throw new EmbeddedArtifactException("The artifact manifest is invalid.", exception);
        }

        if (manifest.SchemaVersion != expectedManifestSchemaVersion ||
            manifest.SessionId == Guid.Empty || manifest.SessionId != bundle.Descriptor.SessionId ||
            manifest.Provider is null || manifest.Provider.Id != expectedProviderId ||
            bundle.Descriptor.ProviderId != expectedProviderId ||
            manifest.QpcFrequency <= 0 || manifest.Capture is null || manifest.Capture.Target is null ||
            manifest.Statistics is null || manifest.Frames is null ||
            (manifest.Trace is null) != (bundle.Descriptor.PrimaryEtlSha256 is null) ||
            manifest.Statistics.ExportedFrames < 0 || manifest.Statistics.FailedFrames < 0 ||
            manifest.Frames.Count != manifest.Statistics.ExportedFrames ||
            manifest.Frames.Select(frame => frame.FrameNumber).Distinct().Count() != manifest.Frames.Count)
        {
            throw new EmbeddedArtifactException("The artifact manifest identity, schema, or statistics are invalid.");
        }

        var indexedPaths = bundle.Descriptor.Entries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var frame in manifest.Frames)
        {
            var path = EmbeddedBundle.NormalizeEntryName(frame.Path);
            if (!path.StartsWith("frames/", StringComparison.Ordinal) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                !indexedPaths.Contains(path))
            {
                throw new EmbeddedArtifactException($"The artifact frame entry is invalid: {path}");
            }
        }

        return new ValidatedArtifactArchive(canonicalArchive, manifest, bundle);
    }
}