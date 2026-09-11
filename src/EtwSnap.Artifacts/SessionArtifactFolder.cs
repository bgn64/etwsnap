using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtwSnap.Artifacts;

public sealed record ArtifactSessionManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("provider")] ArtifactProviderManifest? Provider,
    [property: JsonPropertyName("trace")] ArtifactTraceManifest? Trace,
    [property: JsonPropertyName("statistics")] ArtifactStatisticsManifest? Statistics,
    [property: JsonPropertyName("frames")] IReadOnlyList<ArtifactFrameManifest>? Frames);

public sealed record ArtifactProviderManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] Guid Id);

public sealed record ArtifactTraceManifest(
    [property: JsonPropertyName("tracePath")] string? TracePath);

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

public sealed record ValidatedSessionArtifactFolder(
    string DirectoryPath,
    string ManifestPath,
    ArtifactSessionManifest Manifest,
    byte[] ManifestBytes);

public static class SessionArtifactFolder
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ValidatedSessionArtifactFolder> ValidateAsync(
        string sessionDirectory,
        Guid expectedProviderId,
        int expectedSchemaVersion,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(sessionDirectory);
        if (!Directory.Exists(root))
        {
            throw new EmbeddedArtifactException($"The session directory does not exist: {root}");
        }
        NamedStreamStore.RejectReparsePoint(root);
        if (File.Exists(Path.Combine(root, ".reserved")))
        {
            throw new EmbeddedArtifactException("The session directory has not been finalized.");
        }

        var manifestPath = Path.Combine(root, EmbeddedArtifactConstants.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new EmbeddedArtifactException($"The session manifest does not exist: {manifestPath}");
        }
        RejectReparsePoints(root, manifestPath);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        ArtifactSessionManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ArtifactSessionManifest>(manifestBytes, ManifestJson)
                ?? throw new EmbeddedArtifactException("The session manifest is invalid.");
        }
        catch (JsonException exception)
        {
            throw new EmbeddedArtifactException("The session manifest is invalid.", exception);
        }

        if (manifest.SchemaVersion != expectedSchemaVersion || manifest.SessionId == Guid.Empty ||
            manifest.Provider is null || manifest.Provider.Id != expectedProviderId ||
            manifest.Statistics is null || manifest.Frames is null ||
            manifest.Statistics.ExportedFrames < 0 || manifest.Statistics.FailedFrames < 0 ||
            manifest.Frames.Count != manifest.Statistics.ExportedFrames ||
            manifest.Frames.Select(frame => frame.FrameNumber).Distinct().Count() != manifest.Frames.Count)
        {
            throw new EmbeddedArtifactException("The session manifest identity, schema, or statistics are invalid.");
        }

        foreach (var frame in manifest.Frames)
        {
            var relativePath = EmbeddedBundle.NormalizeEntryName(frame.Path);
            var framePath = ResolveContainedPath(root, relativePath);
            if (!File.Exists(framePath))
            {
                throw new EmbeddedArtifactException($"A manifest frame is missing: {relativePath}");
            }
            RejectReparsePoints(root, framePath);
        }

        return new ValidatedSessionArtifactFolder(root, manifestPath, manifest, manifestBytes);
    }

    internal static string ResolveContainedPath(string root, string relativePath)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddedArtifactException($"The artifact path escapes the session directory: {relativePath}");
        }
        return path;
    }

    internal static void RejectReparsePoints(string root, string path)
    {
        var current = Path.GetFullPath(root);
        foreach (var segment in Path.GetRelativePath(current, path).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new EmbeddedArtifactException($"Artifact paths cannot traverse reparse points: {path}");
            }
        }
    }
}