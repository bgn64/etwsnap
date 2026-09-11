using System.Text.Json;

namespace EtwSnap.Artifacts;

public sealed record EmbeddedBundleEntry(string Path, long Length, string Sha256);

public sealed record EmbeddedBundleDescriptor(
    int SchemaVersion,
    Guid SessionId,
    Guid ProviderId,
    DateTimeOffset CreatedAtUtc,
    int ManifestSchemaVersion,
    string ManifestSha256,
    string PrimaryEtlSha256,
    IReadOnlyList<EmbeddedBundleEntry> Entries);

public sealed record EmbeddedBundleInspection(
    EmbeddedBundleDescriptor Descriptor,
    byte[] ManifestBytes,
    long CompressedBytes,
    long ExpandedBytes);

public static class EmbeddedBundleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

public sealed class EmbeddedArtifactException(string message, Exception? innerException = null)
    : IOException(message, innerException);