using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.IPC;
using EtwSnap.Host.Sessions;
using EtwSnap.Host.Targets;

namespace EtwSnap.Host.Commands;

internal sealed class CommandDispatcher(CaptureSessionCoordinator coordinator, TargetEnumerator targets) : ICommandDispatcher
{
    public async Task<WireMessage> DispatchAsync(
        WireMessage request,
        Func<WireMessage, ValueTask> progress,
        CancellationToken cancellationToken)
    {
        if (request.MessageKind != MessageKind.Request)
        {
            return Error(request, ErrorCodes.InvalidRequest, "Expected a request message.");
        }

        if (request.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Error(request, ErrorCodes.UnsupportedProtocol, $"Protocol {request.ProtocolVersion} is not supported.");
        }

        try
        {
            return request.Command switch
            {
                CommandKind.Ping => Success(request, new PingResult(ProtocolConstants.CurrentVersion, typeof(CommandDispatcher).Assembly.GetName().Version?.ToString() ?? "unknown")),
                CommandKind.Status => Success(request, coordinator.GetStatus()),
                CommandKind.ListWindows => Success(request, new TargetList(targets.EnumerateWindows())),
                CommandKind.ListMonitors => Success(request, new TargetList(targets.EnumerateMonitors())),
                CommandKind.Start => Success(request, await coordinator.StartAsync(request.ReadPayload<StartCaptureRequest>(), cancellationToken).ConfigureAwait(false)),
                CommandKind.Stop => Success(request, await coordinator.StopAsync(request.ReadPayload<StopCaptureRequest>(), progress, request, cancellationToken).ConfigureAwait(false)),
                CommandKind.Cancel => Success(request, await coordinator.CancelAsync(cancellationToken).ConfigureAwait(false)),
                _ => Error(request, ErrorCodes.InvalidRequest, $"Unsupported command: {request.Command}."),
            };
        }
        catch (SessionException exception)
        {
            return Error(request, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception)
        {
            return Error(request, ErrorCodes.InternalError, exception.Message);
        }
    }

    private static WireMessage Success<T>(WireMessage request, T payload, string? message = null) =>
        WireMessage.CreateResponse(request, true, payload, message);

    private static WireMessage Error(WireMessage request, string errorCode, string message) =>
        WireMessage.CreateResponse(request, false, new { }, message, errorCode);
}
