using System.Security.Cryptography;
using System.Text.Json;
using EtwSnap.WpaPlugin.Parsing;

namespace EtwSnap.WpaPlugin.Artifacts;

public sealed class ArtifactResolver
{
    internal const int SupportedContractVersion = 1;
    internal const int SupportedManifestSchemaVersion = 1;
    internal static readonly Guid ProviderId = new("524507bc-3009-5e8d-c071-00a1c641849f");

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ArtifactResolution Resolve(Guid sessionId, IReadOnlyList<EtwSnapEvent> events)
    {
        var references = events.OfType<ArtifactReferenceEvent>().ToArray();
        var commits = events.OfType<ArtifactCommittedEvent>().ToArray();
        if (references.Any(reference => reference.ContractVersion != SupportedContractVersion) ||
            commits.Any(commit => commit.ContractVersion != SupportedContractVersion))
        {
            return ArtifactResolution.Unresolved(
                ArtifactResolutionState.UnsupportedVersion,
                "The trace uses an unsupported artifact contract version.");
        }

        var expectedHash = commits.LastOrDefault()?.ManifestSha256;
        var frames = events
            .OfType<FrameCapturedEvent>()
            .GroupBy(frame => frame.FrameNumber)
            .ToDictionary(group => group.Key, group => group.First());
        var candidates = GetCandidates(sessionId, events, references, commits).ToArray();
        var valid = new List<ValidatedCandidate>();
        var sawIntegrityMismatch = false;
        var sawInvalidManifest = false;
        var sawUnsupportedVersion = false;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var validation = ValidateCandidate(candidate, sessionId, expectedHash, frames);
            if (validation.Candidate is not null)
            {
                valid.Add(validation.Candidate);
            }
            else if (validation.State == ArtifactResolutionState.IntegrityMismatch)
            {
                sawIntegrityMismatch = true;
            }
            else if (validation.State == ArtifactResolutionState.UnsupportedVersion)
            {
                sawUnsupportedVersion = true;
            }
            else
            {
                sawInvalidManifest = true;
            }
        }

        if (valid.Count == 0)
        {
            if (sawIntegrityMismatch)
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.IntegrityMismatch, "No manifest matched the committed SHA-256.");
            }
            if (sawUnsupportedVersion)
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.UnsupportedVersion, "No candidate uses a supported manifest schema.");
            }
            if (sawInvalidManifest)
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.InvalidManifest, "No candidate passed manifest validation.");
            }
            return ArtifactResolution.Unresolved(ArtifactResolutionState.NotFound, "No exact artifact candidate exists.");
        }

        if (valid.Select(candidate => candidate.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        {
            return ArtifactResolution.Unresolved(ArtifactResolutionState.Ambiguous, "Multiple non-identical manifests match the session.");
        }

        var selected = valid[0];
        var detail = BuildDiagnostics(selected, events.OfType<RecordingStoppedEvent>().LastOrDefault());
        return new ArtifactResolution(
            ArtifactResolutionState.Resolved,
            selected.ManifestPath,
            selected.FramePaths,
            selected.ManifestFrameNumbers,
            selected.Statistics,
            detail);
    }

    private static IEnumerable<string> GetCandidates(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        IReadOnlyList<ArtifactReferenceEvent> references,
        IReadOnlyList<ArtifactCommittedEvent> commits)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            AddContainedCandidate(candidates, seen, reference.ArtifactDirectory, reference.ManifestRelativePath);
        }
        foreach (var commit in commits)
        {
            AddContainedCandidate(candidates, seen, commit.ArtifactDirectory, commit.ManifestRelativePath);
        }

        foreach (var tracePath in events.Select(item => item.SourcePath).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var traceDirectory = Path.GetDirectoryName(Path.GetFullPath(tracePath));
            if (traceDirectory is null)
            {
                continue;
            }

            AddContainedCandidate(candidates, seen, traceDirectory, "manifest.json");
            foreach (var portablePath in references.Select(reference => reference.PortableManifestRelativePath)
                .Concat(commits.Select(commit => commit.PortableManifestRelativePath)))
            {
                AddContainedCandidate(candidates, seen, traceDirectory, portablePath);
            }
            AddContainedCandidate(candidates, seen, traceDirectory, $"sessions/{sessionId:N}/manifest.json");
        }

        return candidates;
    }

    private static void AddContainedCandidate(ICollection<string> candidates, ISet<string> seen, string root, string relativePath)
    {
        try
        {
            var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            if (IsWithin(candidate, canonicalRoot) && !ContainsReparsePoint(canonicalRoot, candidate) && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
        }
    }

    private static CandidateValidation ValidateCandidate(
        string manifestPath,
        Guid sessionId,
        string? expectedHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames)
    {
        try
        {
            var bytes = File.ReadAllBytes(manifestPath);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(expectedHash) && !string.Equals(sha256, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return new CandidateValidation(ArtifactResolutionState.IntegrityMismatch, null);
            }

            var manifest = JsonSerializer.Deserialize<SessionManifest>(bytes, ManifestJson);
            if (manifest is null)
            {
                return new CandidateValidation(ArtifactResolutionState.InvalidManifest, null);
            }
            if (manifest.SchemaVersion != SupportedManifestSchemaVersion)
            {
                return new CandidateValidation(ArtifactResolutionState.UnsupportedVersion, null);
            }
            if (manifest.Provider is null || manifest.Statistics is null || manifest.Frames is null ||
                manifest.SessionId != sessionId || manifest.Provider.Id != ProviderId ||
                manifest.Statistics.ExportedFrames < 0 || manifest.Statistics.FailedFrames < 0 ||
                manifest.Frames.Select(frame => frame.FrameNumber).Distinct().Count() != manifest.Frames.Count)
            {
                return new CandidateValidation(ArtifactResolutionState.InvalidManifest, null);
            }

            var sessionDirectory = Path.GetDirectoryName(manifestPath)!;
            var framePaths = new Dictionary<ulong, string>();
            var manifestFrameNumbers = new HashSet<ulong>();
            foreach (var frame in manifest.Frames)
            {
                manifestFrameNumbers.Add(frame.FrameNumber);
                if (traceFrames.TryGetValue(frame.FrameNumber, out var traceFrame) &&
                    (frame.PresentationTime100ns != traceFrame.PresentationTime100ns ||
                     frame.CallbackQpc != traceFrame.CallbackQpc ||
                     frame.Width != traceFrame.Width ||
                     frame.Height != traceFrame.Height ||
                     frame.PixelFormat != traceFrame.PixelFormat))
                {
                    return new CandidateValidation(ArtifactResolutionState.InvalidManifest, null);
                }

                var framePath = Path.GetFullPath(Path.Combine(sessionDirectory, frame.Path));
                if (!IsWithin(framePath, sessionDirectory) || ContainsReparsePoint(sessionDirectory, framePath))
                {
                    return new CandidateValidation(ArtifactResolutionState.InvalidManifest, null);
                }
                if (File.Exists(framePath))
                {
                    framePaths.Add(frame.FrameNumber, framePath);
                }
            }

            return new CandidateValidation(
                ArtifactResolutionState.Resolved,
                new ValidatedCandidate(
                    manifestPath,
                    sha256,
                    framePaths,
                    manifestFrameNumbers,
                    new ArtifactStatistics(
                        manifest.Statistics.AcceptedFrames,
                        manifest.Statistics.RetainedFrames,
                        manifest.Statistics.EvictedFrames,
                        manifest.Statistics.DroppedFrames,
                        manifest.Statistics.NativeErrors,
                        checked((ulong)manifest.Statistics.ExportedFrames),
                        checked((ulong)manifest.Statistics.FailedFrames))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new CandidateValidation(ArtifactResolutionState.InvalidManifest, null);
        }
    }

    private static bool IsWithin(string path, string directory)
    {
        var canonicalDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPath = Path.GetFullPath(path);
        return string.Equals(canonicalPath, canonicalDirectory, StringComparison.OrdinalIgnoreCase) ||
            canonicalPath.StartsWith(canonicalDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var current = Path.GetFullPath(root);
        foreach (var segment in relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string? BuildDiagnostics(ValidatedCandidate candidate, RecordingStoppedEvent? stopped)
    {
        var differences = new List<string>();
        if (candidate.Statistics.ExportedFrames != checked((ulong)candidate.ManifestFrameNumbers.Count))
        {
            differences.Add("manifest exported count does not match its frame list");
        }
        if (stopped is not null &&
            (candidate.Statistics.AcceptedFrames != stopped.AcceptedFrames ||
             candidate.Statistics.RetainedFrames != stopped.RetainedFrames ||
             candidate.Statistics.EvictedFrames != stopped.EvictedFrames ||
             candidate.Statistics.DroppedFrames != stopped.DroppedFrames ||
             candidate.Statistics.ErrorCount != stopped.ErrorCount))
        {
            differences.Add("manifest statistics differ from RecordingStopped");
        }
        return differences.Count == 0 ? null : string.Join("; ", differences);
    }

    private sealed record ValidatedCandidate(
        string ManifestPath,
        string Sha256,
        IReadOnlyDictionary<ulong, string> FramePaths,
        IReadOnlySet<ulong> ManifestFrameNumbers,
        ArtifactStatistics Statistics);
    private sealed record CandidateValidation(ArtifactResolutionState State, ValidatedCandidate? Candidate);
}