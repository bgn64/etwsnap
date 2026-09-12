using System.Security.Cryptography;
using System.Text.Json;
using EtwSnap.WpaPlugin.Parsing;

namespace EtwSnap.WpaPlugin.Artifacts;

public sealed class ArtifactResolver
{
    internal const int SupportedContractVersion = 2;
    internal const int SupportedManifestSchemaVersion = 2;
    internal static readonly Guid ProviderId = new("524507bc-3009-5e8d-c071-00a1c641849f");

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ArtifactResolution Resolve(Guid sessionId, IReadOnlyList<EtwSnapEvent> events)
    {
        var references = events.OfType<ArtifactReferenceEvent>().ToArray();
        var commits = events.OfType<ArtifactCommittedEvent>().ToArray();
        if (references.Any(reference =>
                reference.ContractVersion != SupportedContractVersion ||
                reference.ManifestSchemaVersion != SupportedManifestSchemaVersion ||
                reference.BundleSchemaVersion != EtwSnap.Artifacts.EmbeddedArtifactConstants.BundleSchemaVersion) ||
            commits.Any(commit =>
                commit.ContractVersion != SupportedContractVersion ||
                commit.ManifestSchemaVersion != SupportedManifestSchemaVersion ||
                commit.BundleSchemaVersion != EtwSnap.Artifacts.EmbeddedArtifactConstants.BundleSchemaVersion))
        {
            return ArtifactResolution.Unresolved(
                ArtifactResolutionState.UnsupportedVersion,
                "The trace uses an unsupported artifact contract version.");
        }

        var expectedHash = commits.LastOrDefault()?.ManifestSha256;
        var expectedArtifactHash = commits.LastOrDefault()?.ArtifactSha256;
        var frames = events
            .OfType<FrameCapturedEvent>()
            .GroupBy(frame => frame.FrameNumber)
            .ToDictionary(group => group.Key, group => group.First());
        var directArchive = ResolveDirectArchive(sessionId, events, expectedHash, expectedArtifactHash, frames);
        if (directArchive is not null)
        {
            return directArchive;
        }
        var embedded = ResolveEmbedded(sessionId, events, expectedHash, expectedArtifactHash, frames);
        if (embedded is not null)
        {
            return embedded;
        }
        return ResolveSidecar(sessionId, events, expectedHash, expectedArtifactHash, frames)
            ?? ArtifactResolution.Unresolved(ArtifactResolutionState.NotFound, "No exact embedded stream or sidecar artifact ZIP exists.");
    }

    private static ArtifactResolution? ResolveDirectArchive(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames)
    {
        var archivePaths = events.Select(item => item.SourcePath)
            .Where(path => path.EndsWith(EtwSnap.Artifacts.EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (archivePaths.Length == 0)
        {
            return null;
        }
        if (archivePaths.Length != 1)
        {
            return ArtifactResolution.Unresolved(ArtifactResolutionState.Ambiguous, "The session came from multiple artifact ZIPs.");
        }
        var archivePath = archivePaths[0];
        var siblingEtl = GetSiblingEtlPath(archivePath);
        return ResolveArchive(
            archivePath,
            File.Exists(siblingEtl) ? siblingEtl : null,
            sessionId,
            events,
            expectedManifestHash,
            expectedArtifactHash,
            traceFrames);
    }

    private static ArtifactResolution? ResolveSidecar(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames)
    {
        var sourcePaths = events.Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length != 1 || !File.Exists(sourcePaths[0]))
        {
            return null;
        }

        var etlPath = sourcePaths[0];
        var directory = Path.GetDirectoryName(etlPath)!;
        var stem = Path.GetFileNameWithoutExtension(etlPath);
        var candidates = new[]
        {
            Path.Combine(directory, EtwSnap.Artifacts.EmbeddedArtifactConstants.GetArtifactFileName(stem)),
            Path.Combine(directory, EtwSnap.Artifacts.EmbeddedArtifactConstants.GetArtifactFileName($"{stem}.{sessionId:N}")),
        }.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        ArtifactResolution? firstInvalid = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var archive = EtwSnap.Artifacts.ArtifactArchive.ValidateAsync(
                    candidate,
                    etlPath,
                    ProviderId,
                    SupportedManifestSchemaVersion,
                    CancellationToken.None).GetAwaiter().GetResult();
                var archiveHash = EtwSnap.Artifacts.EmbeddedBundle.HashFileAsync(
                    candidate,
                    CancellationToken.None).GetAwaiter().GetResult();
                if (archive.Manifest.SessionId != sessionId)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                    !string.Equals(archive.Bundle.Descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    firstInvalid ??= ArtifactResolution.Unresolved(
                        ArtifactResolutionState.IntegrityMismatch,
                        "The sidecar artifact manifest does not match the committed SHA-256.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(expectedArtifactHash) &&
                    !string.Equals(
                        archiveHash,
                        expectedArtifactHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    firstInvalid ??= ArtifactResolution.Unresolved(
                        ArtifactResolutionState.IntegrityMismatch,
                        "The sidecar artifact ZIP does not match the committed SHA-256.");
                    continue;
                }

                var framePaths = new Dictionary<ulong, string>();
                var manifestFrameNumbers = new HashSet<ulong>();
                foreach (var frame in archive.Manifest.Frames!)
                {
                    manifestFrameNumbers.Add(frame.FrameNumber);
                    if (!traceFrames.TryGetValue(frame.FrameNumber, out var traceFrame) ||
                        frame.PresentationTime100ns != traceFrame.PresentationTime100ns ||
                        frame.CallbackQpc != traceFrame.CallbackQpc ||
                        frame.Width != traceFrame.Width || frame.Height != traceFrame.Height ||
                        frame.PixelFormat != traceFrame.PixelFormat)
                    {
                        throw new EtwSnap.Artifacts.EmbeddedArtifactException(
                            $"Sidecar frame metadata does not match ETW for frame {frame.FrameNumber}.");
                    }
                    framePaths.Add(
                        frame.FrameNumber,
                        new EmbeddedFrameReference(
                            candidate,
                            null,
                            sessionId,
                            frame.Path,
                            archive.Bundle.Descriptor,
                            archive.Bundle.Descriptor.PrimaryEtlSha256 ?? archiveHash).Materialize());
                }

                var manifestStatistics = archive.Manifest.Statistics!;
                var statistics = new ArtifactStatistics(
                    manifestStatistics.AcceptedFrames,
                    manifestStatistics.RetainedFrames,
                    manifestStatistics.EvictedFrames,
                    manifestStatistics.DroppedFrames,
                    manifestStatistics.NativeErrors,
                    checked((ulong)manifestStatistics.ExportedFrames),
                    checked((ulong)manifestStatistics.FailedFrames));
                var validated = new ValidatedCandidate(
                    $"{candidate}!/{EtwSnap.Artifacts.EmbeddedArtifactConstants.ManifestFileName}",
                    archive.Bundle.Descriptor.ManifestSha256,
                    framePaths,
                    manifestFrameNumbers,
                    statistics);
                return new ArtifactResolution(
                    ArtifactResolutionState.Resolved,
                    validated.ManifestPath,
                    framePaths,
                    manifestFrameNumbers,
                    statistics,
                    BuildDiagnostics(validated, events.OfType<RecordingStoppedEvent>().LastOrDefault()));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
            {
                firstInvalid ??= ArtifactResolution.Unresolved(
                    exception.Message.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                    exception.Message.Contains("belong", StringComparison.OrdinalIgnoreCase)
                        ? ArtifactResolutionState.IntegrityMismatch
                        : ArtifactResolutionState.InvalidManifest,
                    exception.Message);
            }
        }
        return firstInvalid;
    }

    private static ArtifactResolution ResolveArchive(
        string archivePath,
        string? etlPath,
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames)
    {
        try
        {
            var archive = EtwSnap.Artifacts.ArtifactArchive.ValidateAsync(
                archivePath,
                etlPath,
                ProviderId,
                SupportedManifestSchemaVersion,
                CancellationToken.None).GetAwaiter().GetResult();
            var archiveHash = EtwSnap.Artifacts.EmbeddedBundle.HashFileAsync(
                archivePath,
                CancellationToken.None).GetAwaiter().GetResult();
            if (archive.Manifest.SessionId != sessionId)
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.InvalidManifest, "The artifact ZIP session does not match its timeline.");
            }
            if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                !string.Equals(archive.Bundle.Descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.IntegrityMismatch, "The artifact manifest does not match the committed SHA-256.");
            }
            if (!string.IsNullOrWhiteSpace(expectedArtifactHash) &&
                !string.Equals(
                    archiveHash,
                    expectedArtifactHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ArtifactResolution.Unresolved(ArtifactResolutionState.IntegrityMismatch, "The artifact ZIP does not match the committed SHA-256.");
            }

            var framePaths = new Dictionary<ulong, string>();
            var manifestFrameNumbers = new HashSet<ulong>();
            foreach (var frame in archive.Manifest.Frames!)
            {
                manifestFrameNumbers.Add(frame.FrameNumber);
                if (!traceFrames.TryGetValue(frame.FrameNumber, out var sourceFrame) ||
                    frame.PresentationTime100ns != sourceFrame.PresentationTime100ns ||
                    frame.CallbackQpc != sourceFrame.CallbackQpc ||
                    frame.Width != sourceFrame.Width || frame.Height != sourceFrame.Height ||
                    frame.PixelFormat != sourceFrame.PixelFormat)
                {
                    return ArtifactResolution.Unresolved(
                        ArtifactResolutionState.InvalidManifest,
                        $"Artifact frame metadata does not match the timeline for frame {frame.FrameNumber}.");
                }
                framePaths.Add(
                    frame.FrameNumber,
                    new EmbeddedFrameReference(
                        archivePath,
                        null,
                        sessionId,
                        frame.Path,
                        archive.Bundle.Descriptor,
                        archive.Bundle.Descriptor.PrimaryEtlSha256 ?? archiveHash).Materialize());
            }

            var manifestStatistics = archive.Manifest.Statistics!;
            var statistics = new ArtifactStatistics(
                manifestStatistics.AcceptedFrames,
                manifestStatistics.RetainedFrames,
                manifestStatistics.EvictedFrames,
                manifestStatistics.DroppedFrames,
                manifestStatistics.NativeErrors,
                checked((ulong)manifestStatistics.ExportedFrames),
                checked((ulong)manifestStatistics.FailedFrames));
            var validated = new ValidatedCandidate(
                $"{archivePath}!/{EtwSnap.Artifacts.EmbeddedArtifactConstants.ManifestFileName}",
                archive.Bundle.Descriptor.ManifestSha256,
                framePaths,
                manifestFrameNumbers,
                statistics);
            return new ArtifactResolution(
                ArtifactResolutionState.Resolved,
                validated.ManifestPath,
                framePaths,
                manifestFrameNumbers,
                statistics,
                BuildDiagnostics(validated, events.OfType<RecordingStoppedEvent>().LastOrDefault()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return ArtifactResolution.Unresolved(
                exception.Message.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("belong", StringComparison.OrdinalIgnoreCase)
                    ? ArtifactResolutionState.IntegrityMismatch
                    : ArtifactResolutionState.InvalidManifest,
                exception.Message);
        }
    }

    private static string GetSiblingEtlPath(string archivePath)
    {
        var stem = archivePath[..^EtwSnap.Artifacts.EmbeddedArtifactConstants.ArtifactFileExtension.Length];
        var direct = stem + ".etl";
        if (File.Exists(direct))
        {
            return direct;
        }
        var suffix = Path.GetExtension(stem);
        return suffix.Length == 33 && Guid.TryParseExact(suffix[1..], "N", out _)
            ? stem[..^suffix.Length] + ".etl"
            : direct;
    }

    private static ArtifactResolution? ResolveEmbedded(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames)
    {
        var sourcePaths = events.Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length == 0 || sourcePaths.Length == 1 && !File.Exists(sourcePaths[0]))
        {
            return null;
        }
        if (sourcePaths.Length != 1)
        {
            return ArtifactResolution.Unresolved(
                ArtifactResolutionState.Ambiguous,
                "The session events came from multiple ETL sources.");
        }

        var etlPath = sourcePaths[0];
        var streamName = EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(sessionId);
        var streams = new EtwSnap.Artifacts.NamedStreamStore();
        FileStream stream;
        try
        {
            stream = streams.OpenRead(etlPath, streamName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ArtifactResolution.Unresolved(ArtifactResolutionState.InvalidManifest, exception.Message);
        }

        using (stream)
        {
            try
            {
                var inspection = new EtwSnap.Artifacts.EmbeddedBundle().InspectAsync(
                    stream,
                    etlPath,
                    sessionId,
                    CancellationToken.None).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(expectedArtifactHash))
                {
                    stream.Position = 0;
                    var actualArtifactHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (!string.Equals(actualArtifactHash, expectedArtifactHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return ArtifactResolution.Unresolved(
                            ArtifactResolutionState.IntegrityMismatch,
                            "The embedded artifact ZIP does not match the committed SHA-256.");
                    }
                }
                var descriptor = inspection.Descriptor;
                if (descriptor.ProviderId != ProviderId ||
                    descriptor.ManifestSchemaVersion != SupportedManifestSchemaVersion)
                {
                    return ArtifactResolution.Unresolved(
                        ArtifactResolutionState.UnsupportedVersion,
                        "The embedded bundle provider or manifest schema is not supported.");
                }
                if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                    !string.Equals(descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    return ArtifactResolution.Unresolved(
                        ArtifactResolutionState.IntegrityMismatch,
                        "The embedded manifest does not match the committed SHA-256.");
                }

                var manifest = JsonSerializer.Deserialize<SessionManifest>(inspection.ManifestBytes, ManifestJson);
                if (manifest is null || manifest.Provider is null || manifest.Statistics is null || manifest.Frames is null ||
                    manifest.SchemaVersion != SupportedManifestSchemaVersion || manifest.SessionId != sessionId ||
                    manifest.Provider.Id != ProviderId || manifest.Statistics.ExportedFrames < 0 ||
                    manifest.Statistics.FailedFrames < 0 ||
                    manifest.Statistics.ExportedFrames != manifest.Frames.Count ||
                    manifest.Frames.Select(frame => frame.FrameNumber).Distinct().Count() != manifest.Frames.Count)
                {
                    return ArtifactResolution.Unresolved(
                        ArtifactResolutionState.InvalidManifest,
                        "The embedded manifest identity, statistics, or frame list is invalid.");
                }

                var indexedPaths = descriptor.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
                var embeddedFrames = new Dictionary<ulong, EmbeddedFrameReference>();
                var manifestFrameNumbers = new HashSet<ulong>();
                foreach (var frame in manifest.Frames)
                {
                    manifestFrameNumbers.Add(frame.FrameNumber);
                    var entryPath = EtwSnap.Artifacts.EmbeddedBundle.NormalizeEntryName(frame.Path);
                    if (!entryPath.StartsWith("frames/", StringComparison.Ordinal) ||
                        !entryPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        !indexedPaths.ContainsKey(entryPath))
                    {
                        return ArtifactResolution.Unresolved(
                            ArtifactResolutionState.InvalidManifest,
                            $"The embedded frame entry is invalid: {entryPath}");
                    }
                    if (!traceFrames.TryGetValue(frame.FrameNumber, out var traceFrame) ||
                        frame.PresentationTime100ns != traceFrame.PresentationTime100ns ||
                         frame.CallbackQpc != traceFrame.CallbackQpc ||
                         frame.Width != traceFrame.Width ||
                         frame.Height != traceFrame.Height ||
                         frame.PixelFormat != traceFrame.PixelFormat)
                    {
                        return ArtifactResolution.Unresolved(
                            ArtifactResolutionState.InvalidManifest,
                            $"Embedded frame metadata does not match ETW for frame {frame.FrameNumber}.");
                    }
                    embeddedFrames.Add(
                        frame.FrameNumber,
                        new EmbeddedFrameReference(
                            etlPath,
                            streamName,
                            sessionId,
                            entryPath,
                            descriptor,
                            descriptor.PrimaryEtlSha256!));
                }

                var statistics = new ArtifactStatistics(
                    manifest.Statistics.AcceptedFrames,
                    manifest.Statistics.RetainedFrames,
                    manifest.Statistics.EvictedFrames,
                    manifest.Statistics.DroppedFrames,
                    manifest.Statistics.NativeErrors,
                    checked((ulong)manifest.Statistics.ExportedFrames),
                    checked((ulong)manifest.Statistics.FailedFrames));
                var framePaths = embeddedFrames.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Materialize());
                var diagnosticCandidate = new ValidatedCandidate(
                    $"{etlPath}:{streamName}!/{EtwSnap.Artifacts.EmbeddedArtifactConstants.ManifestFileName}",
                    descriptor.ManifestSha256,
                    framePaths,
                    manifestFrameNumbers,
                    statistics);
                return new ArtifactResolution(
                    ArtifactResolutionState.Resolved,
                    diagnosticCandidate.ManifestPath,
                    framePaths,
                    manifestFrameNumbers,
                    statistics,
                    BuildDiagnostics(diagnosticCandidate, events.OfType<RecordingStoppedEvent>().LastOrDefault()));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
            {
                var state = exception.Message.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                    exception.Message.Contains("belong", StringComparison.OrdinalIgnoreCase)
                    ? ArtifactResolutionState.IntegrityMismatch
                    : ArtifactResolutionState.InvalidManifest;
                return ArtifactResolution.Unresolved(state, exception.Message);
            }
        }
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
}