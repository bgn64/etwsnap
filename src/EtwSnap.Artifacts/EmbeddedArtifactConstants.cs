namespace EtwSnap.Artifacts;

public static class EmbeddedArtifactConstants
{
    public const int BundleSchemaVersion = 2;
    public const string ArtifactFileExtension = ".etwsnap.zip";
    public const string StreamPrefix = "EtwSnap.Session.";
    public const string DescriptorFileName = "bundle.json";
    public const string ManifestFileName = "manifest.json";
    public const int MaximumEntryCount = 100_000;
    public const long MaximumDescriptorBytes = 16L * 1024 * 1024;
    public const long MaximumManifestBytes = 64L * 1024 * 1024;
    public const long MaximumEntryBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 16L * 1024 * 1024 * 1024;

    public static string GetStreamName(Guid sessionId) => $"{StreamPrefix}{sessionId:N}";

    public static string GetArtifactFileName(string baseName) => baseName + ArtifactFileExtension;

    public static ArtifactOutputPaths GetOutputPaths(string outputName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        var basePath = Path.GetFullPath(outputName);
        var name = Path.GetFileName(basePath);
        if (Directory.Exists(basePath) || string.IsNullOrWhiteSpace(name) ||
            name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Provide an explicit output name without an .etl or .zip extension.", nameof(outputName));
        }
        var directory = Path.GetDirectoryName(basePath)
            ?? throw new ArgumentException("The output name must have a parent directory.", nameof(outputName));
        return new ArtifactOutputPaths(
            basePath,
            directory,
            name,
            basePath + ".etl",
            basePath + ArtifactFileExtension);
    }

    public static string GetIndexedArtifactFileName(string baseName, int index)
    {
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return $"{baseName}-{index}{ArtifactFileExtension}";
    }

    public static bool TryParseIndexedArtifactFileName(string baseName, string fileName, out int index)
    {
        index = 0;
        var prefix = baseName + "-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var value = fileName[prefix.Length..^ArtifactFileExtension.Length];
        return int.TryParse(value, out index) && index >= 1 && value == index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool TryParseStreamName(string streamName, out Guid sessionId)
    {
        sessionId = default;
        return streamName.StartsWith(StreamPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(streamName[StreamPrefix.Length..], "N", out sessionId);
    }
}

public sealed record ArtifactOutputPaths(
    string BasePath,
    string DirectoryPath,
    string Name,
    string EtlPath,
    string ZipPath);
