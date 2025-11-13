using ETWSnap.IPC;
using ETWSnap.Service.Recording;

namespace ETWSnap.Service.Commands;

/// <summary>
/// Interface for handling commands received from clients
/// </summary>
public interface ICommandHandler
{
    Task<Response> HandleCommandAsync(Request request);
}

/// <summary>
/// Handles commands and coordinates with the recording state manager
/// </summary>
public class CommandHandler : ICommandHandler
{
    private readonly IRecordingStateManager _stateManager;

    public CommandHandler(IRecordingStateManager stateManager)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public Task<Response> HandleCommandAsync(Request request)
    {
        Console.WriteLine($"[CommandHandler] Received command: {request.Command}");

        return request.Command switch
        {
            CommandType.Start => HandleStartCommand(request),
            CommandType.Stop => HandleStopCommand(request),
            CommandType.Cancel => HandleCancelCommand(request),
            CommandType.CheckStatus => HandleStatusCommand(request),
            _ => Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = $"Unknown command: {request.Command}"
            })
        };
    }

    private Task<Response> HandleStartCommand(Request request)
    {
        if (_stateManager.IsRecording)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.AlreadyRecording,
                Message = "A recording session is already in progress",
                IsRecording = true
            });
        }

        var success = _stateManager.StartRecording();
        
        if (success)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = "Recording started successfully",
                IsRecording = true
            });
        }
        else
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "Failed to start recording",
                IsRecording = false
            });
        }
    }

    private Task<Response> HandleStopCommand(Request request)
    {
        if (!_stateManager.IsRecording)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.NotRecording,
                Message = "No recording session is currently active",
                IsRecording = false
            });
        }

        if (string.IsNullOrEmpty(request.FilePath))
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "File path is required for stop command",
                IsRecording = true
            });
        }

        var success = _stateManager.StopRecording(request.FilePath);
        
        if (success)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = $"Recording stopped and saved to: {request.FilePath}",
                IsRecording = false
            });
        }
        else
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "Failed to stop recording",
                IsRecording = _stateManager.IsRecording
            });
        }
    }

    private Task<Response> HandleCancelCommand(Request request)
    {
        if (!_stateManager.IsRecording)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.NotRecording,
                Message = "No recording session is currently active",
                IsRecording = false
            });
        }

        var success = _stateManager.CancelRecording();
        
        if (success)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = "Recording cancelled successfully",
                IsRecording = false
            });
        }
        else
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "Failed to cancel recording",
                IsRecording = _stateManager.IsRecording
            });
        }
    }

    private Task<Response> HandleStatusCommand(Request request)
    {
        var state = _stateManager.GetState();
        
        var message = state.IsRecording
            ? $"Recording in progress (started at {state.StartTime?.ToLocalTime():HH:mm:ss})"
            : "No active recording";

        return Task.FromResult(new Response
        {
            Status = ResponseStatus.Success,
            Message = message,
            IsRecording = state.IsRecording
        });
    }
}
