using System.Text.Json.Serialization;

namespace EtwSnap.WpaPlugin.Artifacts;

internal sealed record SessionManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("provider")] ProviderManifest Provider,
    [property: JsonPropertyName("statistics")] StatisticsManifest Statistics,
    [property: JsonPropertyName("frames")] IReadOnlyList<FrameManifest> Frames);

internal sealed record ProviderManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] Guid Id);

internal sealed record StatisticsManifest(
    [property: JsonPropertyName("acceptedFrames")] ulong AcceptedFrames,
    [property: JsonPropertyName("retainedFrames")] ulong RetainedFrames,
    [property: JsonPropertyName("evictedFrames")] ulong EvictedFrames,
    [property: JsonPropertyName("droppedFrames")] ulong DroppedFrames,
    [property: JsonPropertyName("nativeErrors")] ulong NativeErrors,
    [property: JsonPropertyName("exportedFrames")] int ExportedFrames,
    [property: JsonPropertyName("failedFrames")] int FailedFrames);

internal sealed record FrameManifest(
    [property: JsonPropertyName("frameNumber")] ulong FrameNumber,
    [property: JsonPropertyName("presentationTime100ns")] long PresentationTime100ns,
    [property: JsonPropertyName("callbackQpc")] long CallbackQpc,
    [property: JsonPropertyName("width")] uint Width,
    [property: JsonPropertyName("height")] uint Height,
    [property: JsonPropertyName("pixelFormat")] uint PixelFormat,
    [property: JsonPropertyName("path")] string Path);