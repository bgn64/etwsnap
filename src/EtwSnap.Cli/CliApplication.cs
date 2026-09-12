using System.CommandLine;
using System.CommandLine.Parsing;
using EtwSnap.Artifacts;
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
        _root.Subcommands.Add(CreateArtifactsCommand());
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
        var embedArtifacts = new Option<bool>("--embed-artifacts") { Description = "Attach session artifacts to the generated ETL when supported." };
        var command = new Command("stop", "Stop recording and save screenshots and any trace.");
        command.Arguments.Add(outputRoot);
        command.Options.Add(embedArtifacts);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new StopCaptureRequest(
                parseResult.GetValue(outputRoot)!,
                parseResult.GetValue(embedArtifacts) ? ArtifactTransport.Embedded : ArtifactTransport.Sidecar);
            return await SendAsync<StopCaptureRequest, StopCaptureResult>(
                CommandKind.Stop,
                request,
                result =>
                {
                    Console.WriteLine($"Saved {result.ExportedFrames} frames.");
                    if (result.ArtifactZipPath is not null)
                    {
                        Console.WriteLine($"Artifacts: {result.ArtifactZipPath}");
                    }
                    if (result.TracePath is not null)
                    {
                        Console.WriteLine($"Trace: {result.TracePath}");
                    }
                    Console.WriteLine($"Artifact SHA-256: {result.ArtifactSha256}");
                    if (result.ArtifactWarning is not null)
                    {
                        Console.Error.WriteLine($"WARNING: {result.ArtifactWarning}");
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

    private static Command CreateArtifactsCommand()
    {
        var artifacts = new Command("artifacts", "Inspect, add, export, or remove ETWSnap artifact ZIPs.");
        artifacts.Subcommands.Add(CreateArtifactsInspectCommand());
        artifacts.Subcommands.Add(CreateArtifactsAddCommand());
        artifacts.Subcommands.Add(CreateArtifactsRemoveCommand());
        return artifacts;
    }

    private static Command CreateArtifactsInspectCommand()
    {
        var artifact = new Argument<string>("artifact") { Description = "Artifact ZIP or ETL whose embedded artifacts will be inspected." };
        var command = new Command("inspect", "Verify and describe ETWSnap artifact ZIPs or embedded artifacts.");
        command.Arguments.Add(artifact);
        command.SetAction(async (parseResult, cancellationToken) =>
            await RunArtifactOperationAsync(async () =>
            {
                var path = Path.GetFullPath(parseResult.GetValue(artifact)!);
                if (path.EndsWith(EmbeddedArtifactConstants.ArtifactFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var siblingEtl = GetSiblingEtlPath(path);
                    var archive = await ArtifactArchive.ValidateAsync(
                        path,
                        File.Exists(siblingEtl) ? siblingEtl : null,
                        EtwSnapConstants.ProviderId,
                        EtwSnapConstants.ManifestSchemaVersion,
                        cancellationToken).ConfigureAwait(false);
                    WriteArchiveInspection(archive);
                    return 0;
                }

                var tracePath = path;
                var inspections = await new EmbeddedArtifactManager().InspectAsync(tracePath, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"Trace: {tracePath}");
                if (inspections.Count == 0)
                {
                    Console.WriteLine("Embedded artifacts: none");
                    return 0;
                }

                foreach (var inspection in inspections)
                {
                    WriteInspection(inspection);
                }
                return inspections.All(inspection => inspection.IsValid) ? 0 : OperationError;
            }).ConfigureAwait(false));
        return command;
    }

    private static Command CreateArtifactsAddCommand()
    {
        var trace = new Argument<string>("trace.etl") { Description = "Existing ETL that will receive embedded artifacts." };
        var artifactZip = new Argument<string>("artifact.etwsnap.zip") { Description = "Canonical ETWSnap artifact ZIP to embed." };
        var command = new Command("add", "Add an artifact ZIP to a matching ETL.");
        command.Arguments.Add(trace);
        command.Arguments.Add(artifactZip);
        command.SetAction(async (parseResult, cancellationToken) =>
            await RunArtifactOperationAsync(async () =>
            {
                var tracePath = Path.GetFullPath(parseResult.GetValue(trace)!);
                var archive = await ArtifactArchive.ValidateAsync(
                    parseResult.GetValue(artifactZip)!,
                    tracePath,
                    EtwSnapConstants.ProviderId,
                    EtwSnapConstants.ManifestSchemaVersion,
                    cancellationToken).ConfigureAwait(false);
                EtlSessionValidator.RequireSession(tracePath, archive.Manifest, EtwSnapConstants.ProviderId);
                var result = await new EmbeddedArtifactManager().AddAsync(tracePath, archive, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"Added session {result.Bundle.Descriptor.SessionId:N} to {result.EtlPath}");
                Console.WriteLine($"Stream: {result.StreamName}");
                Console.WriteLine($"Frames: {result.Bundle.Descriptor.Entries.Count(entry => entry.Path.StartsWith("frames/", StringComparison.Ordinal))}");
                return 0;
            }).ConfigureAwait(false));
        return command;
    }

    private static Command CreateArtifactsRemoveCommand()
    {
        var trace = new Argument<string>("trace.etl") { Description = "ETL whose embedded artifacts will be removed." };
        var session = new Option<Guid?>("--session") { Description = "Remove only the specified session. All sessions are selected by default." };
        var outputRoot = new Option<string?>("--output-root") { Description = "Export a clean ETL and selected artifact ZIPs here before removal." };
        var force = new Option<bool>("--force") { Description = "Bypass destructive-removal confirmation." };
        var command = new Command("remove", "Export or discard embedded ETWSnap artifacts, then remove their streams.");
        command.Arguments.Add(trace);
        command.Options.Add(session);
        command.Options.Add(outputRoot);
        command.Options.Add(force);
        command.SetAction(async (parseResult, cancellationToken) =>
            await RunArtifactOperationAsync(async () =>
            {
                var tracePath = Path.GetFullPath(parseResult.GetValue(trace)!);
                var manager = new EmbeddedArtifactManager();
                var inspections = await manager.InspectAsync(tracePath, cancellationToken).ConfigureAwait(false);
                var requestedSession = parseResult.GetValue(session);
                var selected = requestedSession is null
                    ? inspections
                    : inspections.Where(item => item.SessionId == requestedSession).ToArray();
                if (selected.Count == 0)
                {
                    throw new EmbeddedArtifactException(requestedSession is null
                        ? "The ETL contains no embedded ETWSnap artifacts."
                        : $"The ETL contains no embedded artifacts for session {requestedSession:N}.");
                }

                var extractionRoot = parseResult.GetValue(outputRoot);
                if (extractionRoot is not null)
                {
                    var export = await manager.ExportAsync(tracePath, selected, extractionRoot, cancellationToken).ConfigureAwait(false);
                    manager.Remove(tracePath, selected);
                    Console.WriteLine($"Trace: {export.EtlPath}");
                    foreach (var zipPath in export.ArtifactZipPaths)
                    {
                        Console.WriteLine($"Artifacts: {zipPath}");
                    }
                    Console.WriteLine($"Removed {selected.Count} embedded artifact stream(s) from {tracePath}");
                    return 0;
                }

                WriteRemovalWarning(tracePath, selected);
                if (!parseResult.GetValue(force))
                {
                    if (Console.IsInputRedirected)
                    {
                        throw new EmbeddedArtifactException("Destructive removal requires --force when input is redirected.");
                    }
                    Console.Write("Remove these artifacts permanently? [y/N]: ");
                    var answer = Console.ReadLine();
                    if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("No artifacts were removed.");
                        return 0;
                    }
                }

                manager.Remove(tracePath, selected);
                Console.WriteLine($"Removed {selected.Count} embedded artifact stream(s) from {tracePath}");
                return 0;
            }).ConfigureAwait(false));
        return command;
    }

    private static async Task<int> RunArtifactOperationAsync(Func<Task<int>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or OverflowException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Artifact error: {exception.Message}");
            return OperationError;
        }
    }

    private static void WriteInspection(EmbeddedStreamInspection inspection)
    {
        Console.WriteLine();
        Console.WriteLine($"Session: {inspection.SessionId?.ToString("N") ?? "unknown"}");
        Console.WriteLine($"Stream: {inspection.Stream.Name}");
        Console.WriteLine($"Status: {(inspection.IsValid ? "Valid" : "Invalid")}");
        Console.WriteLine($"Stream bytes: {inspection.Stream.Size}");
        if (inspection.Bundle is not null)
        {
            Console.WriteLine($"Expanded bytes: {inspection.Bundle.ExpandedBytes}");
            Console.WriteLine($"Frames: {inspection.Bundle.Descriptor.Entries.Count(entry => entry.Path.StartsWith("frames/", StringComparison.Ordinal))}");
            Console.WriteLine($"Manifest SHA-256: {inspection.Bundle.Descriptor.ManifestSha256}");
            Console.WriteLine($"ETL SHA-256: {inspection.Bundle.Descriptor.PrimaryEtlSha256 ?? "none"}");
        }
        if (inspection.Error is not null)
        {
            Console.WriteLine($"Reason: {inspection.Error}");
        }
    }

    private static void WriteArchiveInspection(ValidatedArtifactArchive archive)
    {
        Console.WriteLine($"Artifacts: {archive.ArchivePath}");
        Console.WriteLine($"Session: {archive.Manifest.SessionId:N}");
        Console.WriteLine("Status: Valid");
        Console.WriteLine($"ZIP bytes: {archive.Bundle.CompressedBytes}");
        Console.WriteLine($"Expanded bytes: {archive.Bundle.ExpandedBytes}");
        Console.WriteLine($"Frames: {archive.Manifest.Frames!.Count}");
        Console.WriteLine($"Manifest SHA-256: {archive.Bundle.Descriptor.ManifestSha256}");
        Console.WriteLine($"ETL SHA-256: {archive.Bundle.Descriptor.PrimaryEtlSha256 ?? "none"}");
    }

    private static string GetSiblingEtlPath(string artifactZipPath)
    {
        var stem = artifactZipPath[..^EmbeddedArtifactConstants.ArtifactFileExtension.Length];
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

    private static void WriteRemovalWarning(string tracePath, IReadOnlyList<EmbeddedStreamInspection> selected)
    {
        Console.WriteLine("WARNING: This permanently deletes embedded ETWSnap artifacts.");
        Console.WriteLine($"Trace: {tracePath}");
        foreach (var inspection in selected)
        {
            var frameCount = inspection.Bundle?.Descriptor.Entries.Count(entry => entry.Path.StartsWith("frames/", StringComparison.Ordinal));
            Console.WriteLine(
                $"  Session {inspection.SessionId?.ToString("N") ?? "unknown"}: " +
                $"{(inspection.IsValid ? "Valid" : "Invalid")}, " +
                $"{(frameCount is null ? "unknown frames" : $"{frameCount} frames")}, " +
                $"{inspection.Stream.Size} bytes");
        }
        Console.WriteLine("This operation is irreversible unless the artifacts exist elsewhere.");
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
