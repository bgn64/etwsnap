using System.Security.Cryptography;
using System.Text.Json;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK.Processing;

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

    public ArtifactResolution Resolve(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        ILogger? logger = null)
    {
        var sourcePaths = events.Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Info(logger,
            $"ETWSnap artifact discovery started. Session={sessionId:N}; Sources={FormatPaths(sourcePaths)}; Events={events.Count}.");
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
            return Finish(logger, sessionId, ArtifactResolution.Unresolved(
                ArtifactResolutionState.UnsupportedVersion,
                "The trace uses an unsupported artifact contract version."));
        }

        var expectedHash = commits.LastOrDefault()?.ManifestSha256;
        var expectedArtifactHash = commits.LastOrDefault()?.ArtifactSha256;
        var frames = events
            .OfType<FrameCapturedEvent>()
            .GroupBy(frame => frame.FrameNumber)
            .ToDictionary(group => group.Key, group => group.First());
        var directArchive = ResolveDirectArchive(sessionId, events, expectedHash, expectedArtifactHash, frames, logger);
        if (directArchive is not null)
        {
            return Finish(logger, sessionId, directArchive);
        }
        var embedded = ResolveEmbedded(sessionId, events, expectedHash, expectedArtifactHash, frames, logger);
        if (embedded is not null)
        {
            return Finish(logger, sessionId, embedded);
        }
        var sidecar = ResolveSidecar(sessionId, events, expectedHash, expectedArtifactHash, frames, logger)
            ?? ArtifactResolution.Unresolved(ArtifactResolutionState.NotFound, "No exact embedded stream or sidecar artifact ZIP exists.");
        return Finish(logger, sessionId, sidecar);
    }

    private static ArtifactResolution? ResolveDirectArchive(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames,
        ILogger? logger)
    {
        var archivePaths = events.Select(item => item.SourcePath)
            .Where(path => path.EndsWith(EtwSnap.Artifacts.EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (archivePaths.Length == 0)
        {
            Verbose(logger, $"No direct artifact ZIP source applies to session {sessionId:N}.");
            return null;
        }
        if (archivePaths.Length != 1)
        {
            Warn(logger, $"Direct artifact ZIP discovery is ambiguous for session {sessionId:N}: {FormatPaths(archivePaths)}.");
            return ArtifactResolution.Unresolved(ArtifactResolutionState.Ambiguous, "The session came from multiple artifact ZIPs.");
        }
        var archivePath = archivePaths[0];
        var siblingEtl = GetSiblingEtlPath(archivePath);
        Info(logger,
            $"Checking direct artifact ZIP '{archivePath}'. Sibling ETL candidate='{siblingEtl}'; Exists={File.Exists(siblingEtl)}.");
        return ResolveArchive(
            archivePath,
            File.Exists(siblingEtl) ? siblingEtl : null,
            sessionId,
            events,
            expectedManifestHash,
            expectedArtifactHash,
            traceFrames,
            logger,
            "direct artifact ZIP");
    }

    private static ArtifactResolution? ResolveSidecar(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames,
        ILogger? logger)
    {
        var sourcePaths = events.Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length != 1 || !File.Exists(sourcePaths[0]))
        {
            Verbose(logger,
                $"Sidecar discovery is not applicable for session {sessionId:N}: expected one existing ETL source; Sources={FormatPaths(sourcePaths)}.");
            return null;
        }

        var etlPath = sourcePaths[0];
        var directory = Path.GetDirectoryName(etlPath)!;
        var stem = Path.GetFileNameWithoutExtension(etlPath);
        var candidates = new List<string>();
        var unsuffixed = Path.Combine(directory, EtwSnap.Artifacts.EmbeddedArtifactConstants.GetArtifactFileName(stem));
        Info(logger,
            $"Searching sidecar artifacts for session {sessionId:N}. Directory='{directory}'; Unsuffixed='{unsuffixed}'.");
        if (File.Exists(unsuffixed))
        {
            candidates.Add(unsuffixed);
            Info(logger, $"Found unsuffixed sidecar candidate '{unsuffixed}'.");
        }
        else
        {
            Verbose(logger, $"Unsuffixed sidecar candidate does not exist: '{unsuffixed}'.");
        }
        var numberedFiles = Directory
            .EnumerateFiles(
                directory,
                $"{stem}-*{EtwSnap.Artifacts.EmbeddedArtifactConstants.ArtifactFileExtension}",
                SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Parsed = EtwSnap.Artifacts.EmbeddedArtifactConstants.TryParseIndexedArtifactFileName(
                    stem,
                    Path.GetFileName(path),
                    out var index),
                Index = index,
            })
            .ToArray();
        foreach (var ignored in numberedFiles.Where(item => !item.Parsed))
        {
            Verbose(logger, $"Ignoring noncanonical numbered sidecar '{ignored.Path}'.");
        }
        var numberedCandidates = numberedFiles
            .Where(item => item.Parsed)
            .OrderBy(item => item.Index)
            .Select(item => item.Path)
            .ToArray();
        candidates.AddRange(numberedCandidates);
        Info(logger,
            $"Canonical sidecar candidates for session {sessionId:N}: {FormatPaths(candidates)}.");
        if (candidates.Count == 0)
        {
            Info(logger, $"No sidecar artifact ZIP candidates exist for session {sessionId:N} in '{directory}'.");
            return null;
        }

        ArtifactResolution? firstInvalid = null;
        var etlHash = EtwSnap.Artifacts.EmbeddedBundle.HashFileAsync(
            etlPath,
            CancellationToken.None).GetAwaiter().GetResult();
        foreach (var candidate in candidates)
        {
            Info(logger, $"Checking sidecar candidate '{candidate}' for session {sessionId:N}.");
            try
            {
                EtwSnap.Artifacts.EmbeddedBundleDescriptor descriptor;
                using (var candidateStream = File.OpenRead(candidate))
                {
                    descriptor = new EtwSnap.Artifacts.EmbeddedBundle().ReadDescriptorAsync(
                        candidateStream,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                if (descriptor.SessionId != sessionId ||
                    descriptor.PrimaryEtlSha256 is null ||
                    !string.Equals(descriptor.PrimaryEtlSha256, etlHash, StringComparison.OrdinalIgnoreCase))
                {
                    Verbose(logger,
                        $"Skipping sidecar candidate '{candidate}': DescriptorSession={descriptor.SessionId:N}; " +
                        $"ExpectedSession={sessionId:N}; HasEtlHash={descriptor.PrimaryEtlSha256 is not null}; " +
                        $"EtlHashMatches={string.Equals(descriptor.PrimaryEtlSha256, etlHash, StringComparison.OrdinalIgnoreCase)}.");
                    continue;
                }
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
                    Verbose(logger,
                        $"Skipping sidecar candidate '{candidate}': manifest session {archive.Manifest.SessionId:N} does not match {sessionId:N}.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                    !string.Equals(archive.Bundle.Descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    firstInvalid ??= ArtifactResolution.Unresolved(
                        ArtifactResolutionState.IntegrityMismatch,
                        "The sidecar artifact manifest does not match the committed SHA-256.");
                    Warn(logger, $"Rejected sidecar candidate '{candidate}': manifest SHA-256 does not match ArtifactCommitted.");
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
                    Warn(logger, $"Rejected sidecar candidate '{candidate}': ZIP SHA-256 does not match ArtifactCommitted.");
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
                Info(logger,
                    $"Selected sidecar artifact ZIP '{candidate}' for session {sessionId:N}; Frames={framePaths.Count}; Manifest='{validated.ManifestPath}'.");
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
                Warn(logger, exception, $"Rejected sidecar candidate '{candidate}' for session {sessionId:N}: {exception.Message}");
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
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames,
        ILogger? logger,
        string sourceKind)
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
                Warn(logger,
                    $"Rejected {sourceKind} '{archivePath}': manifest session {archive.Manifest.SessionId:N} does not match timeline session {sessionId:N}.");
                return ArtifactResolution.Unresolved(ArtifactResolutionState.InvalidManifest, "The artifact ZIP session does not match its timeline.");
            }
            if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                !string.Equals(archive.Bundle.Descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
            {
                Warn(logger, $"Rejected {sourceKind} '{archivePath}': manifest SHA-256 does not match ArtifactCommitted.");
                return ArtifactResolution.Unresolved(ArtifactResolutionState.IntegrityMismatch, "The artifact manifest does not match the committed SHA-256.");
            }
            if (!string.IsNullOrWhiteSpace(expectedArtifactHash) &&
                !string.Equals(
                    archiveHash,
                    expectedArtifactHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                Warn(logger, $"Rejected {sourceKind} '{archivePath}': ZIP SHA-256 does not match ArtifactCommitted.");
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
                    Warn(logger,
                        $"Rejected {sourceKind} '{archivePath}': frame {frame.FrameNumber} metadata does not match the source timeline.");
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
            Info(logger,
                $"Selected {sourceKind} '{archivePath}' for session {sessionId:N}; Frames={framePaths.Count}; Manifest='{validated.ManifestPath}'.");
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
            Warn(logger, exception, $"Rejected {sourceKind} '{archivePath}' for session {sessionId:N}: {exception.Message}");
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
        var fileName = Path.GetFileName(stem);
        var separator = fileName.LastIndexOf('-');
        if (separator > 0 && int.TryParse(fileName[(separator + 1)..], out var index) && index >= 1)
        {
            return Path.Combine(Path.GetDirectoryName(stem)!, fileName[..separator] + ".etl");
        }
        return direct;
    }

    private static ArtifactResolution? ResolveEmbedded(
        Guid sessionId,
        IReadOnlyList<EtwSnapEvent> events,
        string? expectedManifestHash,
        string? expectedArtifactHash,
        IReadOnlyDictionary<ulong, FrameCapturedEvent> traceFrames,
        ILogger? logger)
    {
        var sourcePaths = events.Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length == 0 || sourcePaths.Length == 1 && !File.Exists(sourcePaths[0]))
        {
            Verbose(logger,
                $"Embedded artifact discovery is not applicable for session {sessionId:N}: no existing ETL source; Sources={FormatPaths(sourcePaths)}.");
            return null;
        }
        if (sourcePaths.Length != 1)
        {
            Warn(logger,
                $"Embedded artifact discovery is ambiguous for session {sessionId:N}: Sources={FormatPaths(sourcePaths)}.");
            return ArtifactResolution.Unresolved(
                ArtifactResolutionState.Ambiguous,
                "The session events came from multiple ETL sources.");
        }

        var etlPath = sourcePaths[0];
        var streamName = EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(sessionId);
        var streams = new EtwSnap.Artifacts.NamedStreamStore();
        Info(logger, $"Checking embedded artifact stream '{etlPath}:{streamName}' for session {sessionId:N}.");
        FileStream stream;
        try
        {
            stream = streams.OpenRead(etlPath, streamName);
        }
        catch (FileNotFoundException)
        {
            Info(logger, $"Embedded artifact stream does not exist: '{etlPath}:{streamName}'.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn(logger, exception,
                $"Unable to open embedded artifact stream '{etlPath}:{streamName}' for session {sessionId:N}: {exception.Message}");
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
                        Warn(logger,
                            $"Rejected embedded artifact stream '{etlPath}:{streamName}': ZIP SHA-256 does not match ArtifactCommitted.");
                        return ArtifactResolution.Unresolved(
                            ArtifactResolutionState.IntegrityMismatch,
                            "The embedded artifact ZIP does not match the committed SHA-256.");
                    }
                }
                var descriptor = inspection.Descriptor;
                if (descriptor.ProviderId != ProviderId ||
                    descriptor.ManifestSchemaVersion != SupportedManifestSchemaVersion)
                {
                    Warn(logger,
                        $"Rejected embedded artifact stream '{etlPath}:{streamName}': provider or manifest schema is unsupported.");
                    return ArtifactResolution.Unresolved(
                        ArtifactResolutionState.UnsupportedVersion,
                        "The embedded bundle provider or manifest schema is not supported.");
                }
                if (!string.IsNullOrWhiteSpace(expectedManifestHash) &&
                    !string.Equals(descriptor.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    Warn(logger,
                        $"Rejected embedded artifact stream '{etlPath}:{streamName}': manifest SHA-256 does not match ArtifactCommitted.");
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
                    Warn(logger,
                        $"Rejected embedded artifact stream '{etlPath}:{streamName}': manifest identity, statistics, or frame list is invalid.");
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
                        Warn(logger,
                            $"Rejected embedded artifact stream '{etlPath}:{streamName}': frame entry '{entryPath}' is invalid.");
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
                        Warn(logger,
                            $"Rejected embedded artifact stream '{etlPath}:{streamName}': frame {frame.FrameNumber} metadata does not match ETW.");
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
                Info(logger,
                    $"Selected embedded artifact stream '{etlPath}:{streamName}' for session {sessionId:N}; Frames={framePaths.Count}; Manifest='{diagnosticCandidate.ManifestPath}'.");
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
                Warn(logger, exception,
                    $"Rejected embedded artifact stream '{etlPath}:{streamName}' for session {sessionId:N}: {exception.Message}");
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

    private static ArtifactResolution Finish(ILogger? logger, Guid sessionId, ArtifactResolution resolution)
    {
        var message =
            $"ETWSnap artifact discovery completed. Session={sessionId:N}; State={resolution.State}; " +
            $"Manifest='{resolution.ManifestPath ?? "none"}'; Frames={resolution.FramePaths.Count}; Detail='{resolution.Detail ?? "none"}'.";
        if (resolution.State == ArtifactResolutionState.Resolved)
        {
            Info(logger, message);
        }
        else
        {
            Warn(logger, message);
        }
        return resolution;
    }

    private static string FormatPaths(IEnumerable<string> paths)
    {
        var values = paths.ToArray();
        return values.Length == 0 ? "none" : string.Join("; ", values.Select(path => $"'{path}'"));
    }

    private static void Verbose(ILogger? logger, string message) => logger?.Verbose("{0}", message);
    private static void Info(ILogger? logger, string message) => logger?.Info("{0}", message);
    private static void Warn(ILogger? logger, string message) => logger?.Warn("{0}", message);
    private static void Warn(ILogger? logger, Exception exception, string message) => logger?.Warn(exception, "{0}", message);
}