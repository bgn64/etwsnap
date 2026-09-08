using System.CommandLine;
using System.CommandLine.Parsing;
using EtwSnap.Cli.IPC;
using EtwSnap.Contracts;
using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;

namespace EtwSnap.Cli;

internal sealed class CliApplication
{
    private const int UsageError = 2;
    private const int HostError = 3;
    private const int OperationError = 4;

    private readonly HostClient _host = new();
    private readonly RootCommand _root;

    public CliApplication()
    {
        _root = new RootCommand("Capture rolling screenshots correlated with ETW events.");
        _root.Subcommands.Add(CreateStartCommand());
        _root.Subcommands.Add(CreateStopCommand());
        _root.Subcommands.Add(CreateCancelCommand());
        _root.Subcommands.Add(CreateStatusCommand());
        _root.Subcommands.Add(CreateTargetsCommand());
        _root.Subcommands.Add(CreateProviderCommand());
    }

    public async Task<int> InvokeAsync(string[] args)
    {
        var parseResult = _root.Parse(args);
        var exitCode = await parseResult.InvokeAsync().ConfigureAwait(false);
        return parseResult.Errors.Count == 0 ? exitCode : UsageError;
    }

    private Command CreateStartCommand()
    {
        var trace = new Option<bool>("--trace") { Description = "Collect an ETL using ETWSnap's bundled WPR profile." };
        var profile = new Option<string?>("--profile") { Description = "Additional WPR profile selector: <file.wprp>!<Profile>[.light|.verbose]. Requires --trace." };
        var window = new Option<string?>("--window") { Description = "Capture a window by hexadecimal or decimal HWND." };
        window.Aliases.Add("-w");
        var monitor = new Option<string?>("--monitor") { Description = "Capture a monitor by hexadecimal or decimal HMONITOR." };
        monitor.Aliases.Add("-m");
        var fps = new Option<int>("--fps")
        {
            Description = "Accepted frames per second (1-120).",
            DefaultValueFactory = _ => EtwSnapConstants.DefaultFramesPerSecond,
        };
        var buffer = new Option<long>("--buffer-mb")
        {
            Description = "Maximum retained BGRA pixel payload in MiB.",
            DefaultValueFactory = _ => EtwSnapConstants.DefaultBufferMegabytes,
        };
        var cursor = new Option<bool>("--cursor")
        {
            Description = "Include the mouse cursor.",
        };
        var noCursor = new Option<bool>("--no-cursor") { Description = "Exclude the mouse cursor." };

        var command = new Command("start", "Start a rolling screenshot session.");
        command.Options.Add(trace);
        command.Options.Add(profile);
        command.Options.Add(window);
        command.Options.Add(monitor);
        command.Options.Add(fps);
        command.Options.Add(buffer);
        command.Options.Add(cursor);
        command.Options.Add(noCursor);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var traceValue = parseResult.GetValue(trace);
            var profileValue = parseResult.GetValue(profile);
            var windowValue = parseResult.GetValue(window);
            var monitorValue = parseResult.GetValue(monitor);
            var fpsValue = parseResult.GetValue(fps);
            var bufferValue = parseResult.GetValue(buffer);

            if (profileValue is not null && !traceValue)
            {
                return WriteUsageError("--profile requires --trace.");
            }

            if (windowValue is not null && monitorValue is not null)
            {
                return WriteUsageError("--window and --monitor are mutually exclusive.");
            }

            if (parseResult.GetValue(cursor) && parseResult.GetValue(noCursor))
            {
                return WriteUsageError("--cursor and --no-cursor are mutually exclusive.");
            }

            if (fpsValue is < 1 or > 120)
            {
                return WriteUsageError("--fps must be between 1 and 120.");
            }

            if (bufferValue <= 0)
            {
                return WriteUsageError("--buffer-mb must be positive.");
            }

            if (!TryCreateTarget(windowValue, monitorValue, out var target, out var targetError))
            {
                return WriteUsageError(targetError!);
            }

            var request = new StartCaptureRequest(
                traceValue,
                profileValue,
                target!,
                fpsValue,
                bufferValue,
                parseResult.GetValue(noCursor) ? false : EtwSnapConstants.DefaultCaptureCursor);

            return await SendAsync<StartCaptureRequest, StartCaptureResult>(
                CommandKind.Start,
                request,
                result => Console.WriteLine($"Recording {result.SessionId:N} started at {result.StartedAtUtc:O}."),
                cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    private Command CreateStopCommand()
    {
        var outputRoot = new Argument<string>("output-root") { Description = "Directory under which the session artifact will be created." };
        var command = new Command("stop", "Stop recording and save screenshots and any trace.");
        command.Arguments.Add(outputRoot);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new StopCaptureRequest(parseResult.GetValue(outputRoot)!);
            return await SendAsync<StopCaptureRequest, StopCaptureResult>(
                CommandKind.Stop,
                request,
                result =>
                {
                    Console.WriteLine($"Saved {result.ExportedFrames} frames to {result.OutputDirectory}");
                    if (result.TracePath is not null)
                    {
                        Console.WriteLine($"Trace: {result.TracePath}");
                    }
                },
                cancellationToken,
                progress => Console.Write($"\r{progress.Message} {progress.ProgressPercent}%   ")).ConfigureAwait(false);
        });
        return command;
    }

    private Command CreateCancelCommand()
    {
        var command = new Command("cancel", "Stop recording and discard retained screenshots.");
        command.SetAction(async (_, cancellationToken) =>
            await SendAsync<EmptyRequest, CancelCaptureResult>(
                CommandKind.Cancel,
                new EmptyRequest(),
                result => Console.WriteLine($"Cancelled session {result.SessionId:N}."),
                cancellationToken).ConfigureAwait(false));
        return command;
    }

    private Command CreateStatusCommand()
    {
        var command = new Command("status", "Show the current session state.");
        command.SetAction(async (_, cancellationToken) =>
            await SendAsync<EmptyRequest, SessionStatus>(
                CommandKind.Status,
                new EmptyRequest(),
                status =>
                {
                    Console.WriteLine($"State: {status.State}");
                    if (status.SessionId is not null)
                    {
                        Console.WriteLine($"Session: {status.SessionId:N}");
                        Console.WriteLine($"Frames: {status.RetainedFrames} retained, {status.EvictedFrames} evicted");
                    }
                },
                cancellationToken).ConfigureAwait(false));
        return command;
    }

    private Command CreateTargetsCommand()
    {
        var targets = new Command("targets", "List capturable windows or monitors.");
        targets.Subcommands.Add(CreateTargetListCommand("windows", CommandKind.ListWindows));
        targets.Subcommands.Add(CreateTargetListCommand("monitors", CommandKind.ListMonitors));
        return targets;
    }

    private Command CreateTargetListCommand(string name, CommandKind commandKind)
    {
        var command = new Command(name);
        command.SetAction(async (_, cancellationToken) =>
            await SendAsync<EmptyRequest, TargetList>(
                commandKind,
                new EmptyRequest(),
                list =>
                {
                    foreach (var target in list.Targets)
                    {
                        var primary = target.IsPrimary ? " [primary]" : string.Empty;
                        Console.WriteLine($"{target.Index,3}  0x{target.Handle:X}  {target.Width}x{target.Height}  {target.Name}{primary}");
                    }
                },
                cancellationToken).ConfigureAwait(false));
        return command;
    }

    private static Command CreateProviderCommand()
    {
        var provider = new Command("provider", "Inspect the ETWSnap ETW provider.");
        var info = new Command("info");
        info.SetAction(_ =>
        {
            Console.WriteLine($"Name: {EtwSnapConstants.ProviderName}");
            Console.WriteLine($"GUID: {EtwSnapConstants.ProviderId:B}");
            return 0;
        });
        provider.Subcommands.Add(info);
        return provider;
    }

    private async Task<int> SendAsync<TRequest, TResponse>(
        CommandKind command,
        TRequest request,
        Action<TResponse> onSuccess,
        CancellationToken cancellationToken,
        Action<WireMessage>? onProgress = null)
    {
        try
        {
            var response = await _host.SendAsync(command, request, onProgress, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                if (onProgress is not null)
                {
                    Console.WriteLine();
                }
                Console.Error.WriteLine($"Error [{response.ErrorCode}]: {response.Message}");
                return OperationError;
            }

            if (onProgress is not null)
            {
                Console.WriteLine();
            }
            onSuccess(response.ReadPayload<TResponse>());
            return 0;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or InvalidDataException or FileNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Host error: {exception.Message}");
            return HostError;
        }
    }

    private static bool TryCreateTarget(
        string? window,
        string? monitor,
        out CaptureTarget? target,
        out string? error)
    {
        target = null;
        error = null;

        if (window is not null)
        {
            if (!TryParseHandle(window, out var handle))
            {
                error = $"Invalid window handle: {window}.";
                return false;
            }
            target = new CaptureTarget(CaptureTargetKind.Window, handle);
            return true;
        }

        if (monitor is not null)
        {
            if (!TryParseHandle(monitor, out var handle))
            {
                error = $"Invalid monitor handle: {monitor}.";
                return false;
            }
            target = new CaptureTarget(CaptureTargetKind.Monitor, handle);
            return true;
        }

        target = new CaptureTarget(CaptureTargetKind.PrimaryMonitor);
        return true;
    }

    private static bool TryParseHandle(string text, out long handle)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out handle) && handle != 0;
        }

        return long.TryParse(text, out handle) && handle != 0;
    }

    private static int WriteUsageError(string message)
    {
        Console.Error.WriteLine($"Usage error: {message}");
        return UsageError;
    }
}
