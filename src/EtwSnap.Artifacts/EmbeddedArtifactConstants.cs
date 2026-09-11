namespace EtwSnap.Artifacts;

public static class EmbeddedArtifactConstants
{
    public const int BundleSchemaVersion = 1;
    public const string StreamPrefix = "EtwSnap.Session.";
    public const string DescriptorFileName = "bundle.json";
    public const string ManifestFileName = "manifest.json";
    public const int MaximumEntryCount = 100_000;
    public const long MaximumDescriptorBytes = 16L * 1024 * 1024;
    public const long MaximumManifestBytes = 64L * 1024 * 1024;
    public const long MaximumEntryBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 16L * 1024 * 1024 * 1024;

    public static string GetStreamName(Guid sessionId) => $"{StreamPrefix}{sessionId:N}";

    public static bool TryParseStreamName(string streamName, out Guid sessionId)
    {
        sessionId = default;
        return streamName.StartsWith(StreamPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(streamName[StreamPrefix.Length..], "N", out sessionId);
    }
}
