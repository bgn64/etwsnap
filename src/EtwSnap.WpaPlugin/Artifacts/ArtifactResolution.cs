namespace EtwSnap.WpaPlugin.Artifacts;

public enum ArtifactResolutionState
{
    Resolved,
    NotFound,
    Ambiguous,
    UnsupportedVersion,
    IntegrityMismatch,
    InvalidManifest,
}

public sealed record ArtifactStatistics(
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong ErrorCount,
    ulong ExportedFrames,
    ulong FailedFrames);

public sealed record ArtifactResolution(
    ArtifactResolutionState State,
    string? ManifestPath,
    IReadOnlyDictionary<ulong, string> FramePaths,
    IReadOnlySet<ulong> ManifestFrameNumbers,
    ArtifactStatistics? Statistics,
    string? Detail,
    IReadOnlyDictionary<ulong, EmbeddedFrameReference>? EmbeddedFrames = null)
{
    public static ArtifactResolution Unresolved(ArtifactResolutionState state, string? detail = null) =>
        new(state, null, new Dictionary<ulong, string>(), new HashSet<ulong>(), null, detail);
}