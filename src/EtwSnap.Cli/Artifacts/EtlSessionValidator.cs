using EtwSnap.Artifacts;
using Microsoft.Diagnostics.Tracing;

namespace EtwSnap.Cli;

internal static class EtlSessionValidator
{
    public static void RequireSession(string etlPath, Guid sessionId, Guid providerId)
    {
        var found = false;
        try
        {
            using var source = new ETWTraceEventSource(etlPath);
            source.Dynamic.All += data =>
            {
                if (found || data.ProviderGuid != providerId)
                {
                    return;
                }
                try
                {
                    var value = data.PayloadByName("SessionId");
                    found = value switch
                    {
                        Guid id => id == sessionId,
                        _ => Guid.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var id) && id == sessionId,
                    };
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
    }
}