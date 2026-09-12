using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Infrastructure;

namespace EtwSnap.Host.Artifacts;

internal sealed record ArtifactReferenceEvent(
    int ContractVersion,
    Guid SessionId,
    string ArtifactPath,
    string ArtifactFileName,
    int ManifestSchemaVersion,
    int BundleSchemaVersion,
    ArtifactTransport RequestedTransport);

internal sealed record ArtifactCommittedEvent(
    ArtifactReferenceEvent Reference,
    string ArtifactPath,
    string ArtifactFileName,
    string ManifestSha256,
    string ArtifactSha256,
    ArtifactTransport ActualTransport,
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
            BundleSchemaVersion = checked((uint)artifact.BundleSchemaVersion),
            RequestedTransport = checked((uint)artifact.RequestedTransport),
            SessionId = artifact.SessionId,
            ArtifactPath = artifact.ArtifactPath,
            ArtifactFileName = artifact.ArtifactFileName,
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
            BundleSchemaVersion = checked((uint)reference.BundleSchemaVersion),
            RequestedTransport = checked((uint)reference.RequestedTransport),
            ActualTransport = checked((uint)artifact.ActualTransport),
            SessionId = reference.SessionId,
            ArtifactPath = artifact.ArtifactPath,
            ArtifactFileName = artifact.ArtifactFileName,
            ManifestSha256 = artifact.ManifestSha256,
            ArtifactSha256 = artifact.ArtifactSha256,
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
    public static ArtifactReferenceEvent CreateReference(
        Guid sessionId,
        string artifactPath,
        string artifactFileName,
        ArtifactTransport requestedTransport)
    {
        return new ArtifactReferenceEvent(
            EtwSnapConstants.ArtifactContractVersion,
            sessionId,
            artifactPath,
            artifactFileName,
            EtwSnapConstants.ManifestSchemaVersion,
            EtwSnap.Artifacts.EmbeddedArtifactConstants.BundleSchemaVersion,
            requestedTransport);
    }

    public static ArtifactCommittedEvent CreateCommitted(
        ArtifactReferenceEvent reference,
        ArtifactWriteResult result,
        ArtifactPublication publication,
        NativeCaptureStats stats) => new(
            reference,
            publication.Transport == ArtifactTransport.Embedded
                ? $"{publication.TracePath}:{EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(reference.SessionId)}"
                : publication.ArtifactZipPath!,
            reference.ArtifactFileName,
            result.ManifestSha256,
            publication.ArtifactSha256,
            publication.Transport,
            result.Status,
            stats.AcceptedFrames,
            stats.RetainedFrames,
            stats.EvictedFrames,
            stats.DroppedFrames,
            stats.ErrorCount,
            checked((ulong)result.ExportedFrames),
            checked((ulong)result.FailedFrames));
}