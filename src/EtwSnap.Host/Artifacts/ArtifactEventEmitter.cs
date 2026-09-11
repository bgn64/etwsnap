using EtwSnap.Contracts;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Infrastructure;

namespace EtwSnap.Host.Artifacts;

internal sealed record ArtifactReferenceEvent(
    int ContractVersion,
    Guid SessionId,
    string ArtifactDirectory,
    string SessionDirectoryName,
    string ManifestRelativePath,
    string PortableManifestRelativePath,
    int ManifestSchemaVersion);

internal sealed record ArtifactCommittedEvent(
    ArtifactReferenceEvent Reference,
    string ManifestSha256,
    string Status,
    ulong AcceptedFrames,
    ulong RetainedFrames,
    ulong EvictedFrames,
    ulong DroppedFrames,
    ulong ErrorCount,
    ulong ExportedFrames,
    ulong FailedFrames);

internal interface IArtifactEventEmitter
{
    void EmitReference(ArtifactReferenceEvent artifact);
    void EmitCommitted(ArtifactCommittedEvent artifact);
}

internal sealed class NativeArtifactEventEmitter : IArtifactEventEmitter
{
    public static NativeArtifactEventEmitter Instance { get; } = new();

    private NativeArtifactEventEmitter()
    {
    }

    public void EmitReference(ArtifactReferenceEvent artifact)
    {
        var native = new NativeMethods.ArtifactReference
        {
            StructSize = checked((uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ArtifactReference>()),
            ApiVersion = NativeMethods.ApiVersion,
            ContractVersion = checked((uint)artifact.ContractVersion),
            ManifestSchemaVersion = checked((uint)artifact.ManifestSchemaVersion),
            SessionId = artifact.SessionId,
            ArtifactDirectory = artifact.ArtifactDirectory,
            SessionDirectoryName = artifact.SessionDirectoryName,
            ManifestRelativePath = artifact.ManifestRelativePath,
            PortableManifestRelativePath = artifact.PortableManifestRelativePath,
        };
        LogFailure(NativeMethods.EtwSnap_EmitArtifactReference(in native), "ArtifactReference");
    }

    public void EmitCommitted(ArtifactCommittedEvent artifact)
    {
        var reference = artifact.Reference;
        var native = new NativeMethods.ArtifactCommitted
        {
            StructSize = checked((uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ArtifactCommitted>()),
            ApiVersion = NativeMethods.ApiVersion,
            ContractVersion = checked((uint)reference.ContractVersion),
            ManifestSchemaVersion = checked((uint)reference.ManifestSchemaVersion),
            SessionId = reference.SessionId,
            ArtifactDirectory = reference.ArtifactDirectory,
            SessionDirectoryName = reference.SessionDirectoryName,
            ManifestRelativePath = reference.ManifestRelativePath,
            PortableManifestRelativePath = reference.PortableManifestRelativePath,
            ManifestSha256 = artifact.ManifestSha256,
            Status = artifact.Status,
            AcceptedFrames = artifact.AcceptedFrames,
            RetainedFrames = artifact.RetainedFrames,
            EvictedFrames = artifact.EvictedFrames,
            DroppedFrames = artifact.DroppedFrames,
            ErrorCount = artifact.ErrorCount,
            ExportedFrames = artifact.ExportedFrames,
            FailedFrames = artifact.FailedFrames,
        };
        LogFailure(NativeMethods.EtwSnap_EmitArtifactCommitted(in native), "ArtifactCommitted");
    }

    private static void LogFailure(NativeMethods.Result result, string eventName)
    {
        if (result != NativeMethods.Result.Success)
        {
            HostLog.Error($"Failed to emit {eventName}: native result {result}.");
        }
    }
}

internal static class ArtifactEvents
{
    public static ArtifactReferenceEvent CreateReference(Guid sessionId, ArtifactReservation reservation)
        => CreateReference(sessionId, reservation.DirectoryPath);

    public static ArtifactReferenceEvent CreateReference(Guid sessionId, string artifactDirectory)
    {
        var sessionDirectoryName = Path.GetFileName(artifactDirectory);
        return new ArtifactReferenceEvent(
            EtwSnapConstants.ArtifactContractVersion,
            sessionId,
            artifactDirectory,
            sessionDirectoryName,
            EtwSnapConstants.ManifestFileName,
            $"sessions/{sessionId:N}/{EtwSnapConstants.ManifestFileName}",
            EtwSnapConstants.ManifestSchemaVersion);
    }

    public static ArtifactCommittedEvent CreateCommitted(
        ArtifactReferenceEvent reference,
        ArtifactWriteResult result,
        NativeCaptureStats stats) => new(
            reference,
            result.ManifestSha256,
            result.Status,
            stats.AcceptedFrames,
            stats.RetainedFrames,
            stats.EvictedFrames,
            stats.DroppedFrames,
            stats.ErrorCount,
            checked((ulong)result.ExportedFrames),
            checked((ulong)result.FailedFrames));
}