using EtwSnap.Artifacts;
using Microsoft.Diagnostics.Tracing;

namespace EtwSnap.Cli;

internal static class EtlSessionValidator
{
    public static void RequireSession(string etlPath, ArtifactSessionManifest manifest, Guid providerId)
    {
        var sessionId = manifest.SessionId;
        var found = false;
        var matchedFrames = new HashSet<ulong>();
        var expectedFrames = manifest.Frames!.ToDictionary(frame => frame.FrameNumber);
        try
        {
            using var source = new ETWTraceEventSource(etlPath);
            source.Dynamic.All += data =>
            {
                if (data.ProviderGuid != providerId)
                {
                    return;
                }
                try
                {
                    var value = data.PayloadByName("SessionId");
                    var eventSessionId = value switch
                    {
                        Guid id => id,
                        _ when Guid.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var id) => id,
                        _ => Guid.Empty,
                    };
                    if (eventSessionId != sessionId)
                    {
                        return;
                    }
                    found = true;
                    if (data.EventName == "FrameCaptured")
                    {
                        var frameNumber = Convert.ToUInt64(data.PayloadByName("FrameNumber"));
                        if (expectedFrames.TryGetValue(frameNumber, out var frame) &&
                            frame.PresentationTime100ns == Convert.ToInt64(data.PayloadByName("PresentationTime100ns")) &&
                            frame.CallbackQpc == Convert.ToInt64(data.PayloadByName("CallbackQpc")) &&
                            frame.Width == Convert.ToUInt32(data.PayloadByName("Width")) &&
                            frame.Height == Convert.ToUInt32(data.PayloadByName("Height")) &&
                            frame.PixelFormat == Convert.ToUInt32(data.PayloadByName("PixelFormat")))
                        {
                            matchedFrames.Add(frameNumber);
                        }
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                }
            };
            source.Process();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new EmbeddedArtifactException($"The target ETL could not be parsed: {etlPath}", exception);
        }

        if (!found)
        {
            throw new EmbeddedArtifactException($"The target ETL does not contain ETWSnap events for session {sessionId:N}.");
        }
        if (matchedFrames.Count != expectedFrames.Count)
        {
            throw new EmbeddedArtifactException("The artifact ZIP frame metadata does not match the target ETL.");
        }
    }
}