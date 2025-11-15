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
            CommandType.ListWindows => HandleListWindowsCommand(request),
            CommandType.ListMonitors => HandleListMonitorsCommand(request),
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

        // Create recording options from request
                var options = new RecordingOptions
                {
                    WindowHandle = new IntPtr(request.WindowHandle),
                    MonitorHandle = new IntPtr(request.MonitorHandle),
                    IsUsingWpr = request.IsUsingWpr
                };        var success = _stateManager.StartRecording(options);
        
        if (success)
        {
            string target = "primary monitor";
            if (options.WindowHandle != IntPtr.Zero)
            {
                target = $"window 0x{options.WindowHandle:X}";
            }
            else if (options.MonitorHandle != IntPtr.Zero)
            {
                target = $"monitor 0x{options.MonitorHandle:X}";
            }

            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = $"Recording started successfully (capturing {target})",
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

        // Get IsUsingWpr flag before stopping
        var isUsingWpr = _stateManager.GetState().IsUsingWpr;
        
        var success = _stateManager.StopRecording(request.FilePath);
        
        if (success)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = $"Recording stopped and saved to: {request.FilePath}",
                IsRecording = false,
                IsUsingWpr = isUsingWpr
            });
        }
        else
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "Failed to stop recording",
                IsRecording = _stateManager.IsRecording,
                IsUsingWpr = isUsingWpr
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

        // Get IsUsingWpr flag before cancelling
        var isUsingWpr = _stateManager.GetState().IsUsingWpr;
        
        var success = _stateManager.CancelRecording();
        
        if (success)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Success,
                Message = "Recording cancelled successfully",
                IsRecording = false,
                IsUsingWpr = isUsingWpr
            });
        }
        else
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = "Failed to cancel recording",
                IsRecording = _stateManager.IsRecording,
                IsUsingWpr = isUsingWpr
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

    private Task<Response> HandleListWindowsCommand(Request request)
    {
        try
        {
            var windows = ScreenRecorder.EnumerateWindows();
            var response = new Response
            {
                Status = ResponseStatus.Success,
                Message = $"Found {windows.Count} capturable windows"
            };

            // Serialize window information to response data
            for (int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                response.Data[$"window_{i}_handle"] = $"0x{window.Handle:X}";
                response.Data[$"window_{i}_title"] = window.Title;
                response.Data[$"window_{i}_size"] = $"{window.Width}x{window.Height}";
            }
            response.Data["count"] = windows.Count.ToString();

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = $"Failed to enumerate windows: {ex.Message}"
            });
        }
    }

    private Task<Response> HandleListMonitorsCommand(Request request)
    {
        try
        {
            var monitors = ScreenRecorder.EnumerateMonitorsDetailed();
            var response = new Response
            {
                Status = ResponseStatus.Success,
                Message = $"Found {monitors.Count} monitors"
            };

            // Serialize monitor information to response data
            for (int i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i];
                response.Data[$"monitor_{i}_handle"] = $"0x{monitor.Handle:X}";
                response.Data[$"monitor_{i}_name"] = monitor.DeviceName;
                response.Data[$"monitor_{i}_bounds"] = $"{monitor.Width}x{monitor.Height} at ({monitor.Left},{monitor.Top})";
                response.Data[$"monitor_{i}_primary"] = monitor.IsPrimary.ToString();
            }
            response.Data["count"] = monitors.Count.ToString();

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new Response
            {
                Status = ResponseStatus.Error,
                Message = $"Failed to enumerate monitors: {ex.Message}"
            });
        }
    }
}
