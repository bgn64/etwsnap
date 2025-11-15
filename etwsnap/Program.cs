using ETWSnap;
using ETWSnap.Client;
using ETWSnap.IPC;
using ETWSnap.WPR;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var parser = new CommandParser();
        var serviceManager = new ServiceManager();
        
        // Parse command-line arguments
        var parsedCommand = parser.Parse(args);
        
        if (!parsedCommand.IsValid)
        {
            Console.Error.WriteLine($"Error: {parsedCommand.ErrorMessage}");
            Console.WriteLine();
            parser.PrintUsage();
            return 1;
        }

        // Initialize logger with verbose mode setting
        Logger.VerboseEnabled = parsedCommand.VerboseMode;

        try
        {
            // Determine command name for routing (strip leading dash if present)
            string commandName = args[0].ToLowerInvariant();
            if (commandName.StartsWith("-"))
            {
                commandName = commandName.Substring(1);
            }
            
            // Ensure service is running if this command requires it
            bool needsService = RequiresService(commandName);
            if (needsService && !await EnsureServiceRunningAsync(serviceManager))
            {
                Console.Error.WriteLine("Failed to start or connect to service");
                return 1;
            }

            // Create pipe client if needed (will be used by commands that need it)
            using var pipeClient = needsService ? new NamedPipeClient() : null;

            // Delegate to individual command handlers
            return commandName switch
            {
                "provider-info" => await HandleProviderInfoCommand(parsedCommand),
                "add-provider" => await HandleAddProviderCommand(parsedCommand),
                "start" => await HandleStartCommand(pipeClient!, parsedCommand),
                "stop" => await HandleStopCommand(pipeClient!, parsedCommand),
                "cancel" => await HandleCancelCommand(pipeClient!, parsedCommand),
                "status" => await HandleCheckStatusCommand(pipeClient!, parsedCommand),
                "list-windows" => await HandleListWindowsCommand(pipeClient!, parsedCommand),
                "list-monitors" => await HandleListMonitorsCommand(pipeClient!, parsedCommand),
                _ => throw new InvalidOperationException($"Unhandled command: {commandName}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static bool RequiresService(string commandName)
    {
        return commandName switch
        {
            "provider-info" => false,
            "add-provider" => false,
            _ => true  // Most commands require the service
        };
    }

    // Command handlers

    private static Task<int> HandleProviderInfoCommand(ParsedCommand parsedCommand)
    {
        Logger.Output("ETWSnap ETW Provider Information:");
        Logger.OutputLine();
        Logger.Output($"Provider Name: {ETWSnapConstants.ProviderName}");
        Logger.Output($"Provider GUID: {ETWSnapConstants.ProviderGuid}");
        Logger.OutputLine();
        Logger.Output("Use this information when configuring ETW tracing tools like WPR or PerfView.");
        Logger.OutputLine();
        Logger.Output("Example WPRP EventProvider definition:");
        Logger.Output($"  <EventProvider Id=\"{ETWSnapConstants.ProviderName}\" Name=\"{ETWSnapConstants.ProviderGuid}\">");
        Logger.Output("  </EventProvider>");
        return Task.FromResult(0);
    }

    private static Task<int> HandleAddProviderCommand(ParsedCommand parsedCommand)
    {
        Logger.Info($"Reading profile from: {parsedCommand.FilePath}");
        Logger.Info($"Writing modified profile to: {parsedCommand.OutputFilePath}");
        Logger.InfoLine();
        
        bool success = WprpModifier.AddEtwSnapProviderToDefaultProfile(parsedCommand.FilePath!, parsedCommand.OutputFilePath!);
        
        return Task.FromResult(success ? 0 : 1);
    }

    private static async Task<int> HandleStartCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        bool wprStarted = false;
        string? tempWprpPath = null;

        // If WPRP path is provided, start WPR tracing first
        if (!string.IsNullOrWhiteSpace(parsedCommand.WprpPath))
        {
            // Create a modified copy of the WPRP with ETWSnap provider added
            tempWprpPath = Path.Combine(Path.GetTempPath(), $"etwsnap_{Path.GetFileName(parsedCommand.WprpPath)}");
            
            Logger.Info($"Adding ETWSnap provider to profile: {parsedCommand.WprpPath}");
            Logger.Info($"Creating temporary profile: {tempWprpPath}");
            
            bool modified = WprpModifier.AddEtwSnapProviderToDefaultProfile(parsedCommand.WprpPath, tempWprpPath);
            if (!modified)
            {
                Console.Error.WriteLine("Failed to modify WPRP profile with ETWSnap provider. Aborting start command.");
                return 1;
            }
            
            Logger.InfoLine();
            
            // Start WPR with the modified profile
            wprStarted = WprManager.Start(tempWprpPath);
            if (!wprStarted)
            {
                Console.Error.WriteLine("Failed to start WPR tracing. Aborting start command.");
                // Clean up temp file
                try { File.Delete(tempWprpPath); } catch { }
                return 1;
            }
        }

        // Send start command to service
        int result = await SendServiceCommand(pipeClient, parsedCommand);

        // If service start failed and we started WPR, cancel it
        if (result != 0 && wprStarted)
        {
            Logger.Info("Service start failed, cancelling WPR tracing...");
            WprManager.Cancel();
            
            // Clean up temp file
            if (tempWprpPath != null)
            {
                try { File.Delete(tempWprpPath); } catch { }
            }
        }

        return result;
    }

    private static async Task<int> HandleStopCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        // Send stop command to service
        var request = new Request
        {
            Command = parsedCommand.Type,
            FilePath = parsedCommand.FilePath,
            WindowHandle = parsedCommand.WindowHandle,
            MonitorHandle = parsedCommand.MonitorHandle,
            IsUsingWpr = false  // Not used for stop command
        };

        var response = await pipeClient.SendCommandAsync(request);

        // Handle response and stop WPR if needed
        int result = HandleResponse(response, parsedCommand.Type);

        // If stop was successful and WPR was used, stop WPR tracing
        if (result == 0 && response.IsUsingWpr)
        {
            // Determine WPR output path
            string wprOutputPath = GetWprOutputPath(parsedCommand.FilePath);
            Logger.InfoLine();
            
            if (!WprManager.Stop(wprOutputPath))
            {
                Console.Error.WriteLine("Warning: Failed to stop WPR tracing");
                return 1;
            }
        }

        return result;
    }

    private static async Task<int> HandleCancelCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        // Send cancel command to service
        var request = new Request
        {
            Command = parsedCommand.Type,
            FilePath = parsedCommand.FilePath,
            WindowHandle = parsedCommand.WindowHandle,
            MonitorHandle = parsedCommand.MonitorHandle,
            IsUsingWpr = false  // Not used for cancel command
        };

        var response = await pipeClient.SendCommandAsync(request);

        // Handle response and cancel WPR if needed
        int result = HandleResponse(response, parsedCommand.Type);

        // If cancel was successful and WPR was used, cancel WPR tracing
        if (result == 0 && response.IsUsingWpr)
        {
            Logger.InfoLine();
            if (!WprManager.Cancel())
            {
                Console.Error.WriteLine("Warning: Failed to cancel WPR tracing");
                return 1;
            }
        }

        return result;
    }

    private static async Task<int> HandleCheckStatusCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        return await SendServiceCommand(pipeClient, parsedCommand);
    }

    private static async Task<int> HandleListWindowsCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        return await SendServiceCommand(pipeClient, parsedCommand);
    }

    private static async Task<int> HandleListMonitorsCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        return await SendServiceCommand(pipeClient, parsedCommand);
    }

    // Helper methods

    private static async Task<bool> EnsureServiceRunningAsync(IServiceManager serviceManager)
    {
        // Check if service is already running
        if (await serviceManager.IsServiceRunningAsync())
        {
            return true;
        }

        Logger.Info("Service not running, starting...");
        
        if (!await serviceManager.StartServiceAsync(Logger.VerboseEnabled))
        {
            return false;
        }

        Logger.Info("Service started successfully");
        return true;
    }

    private static async Task<int> SendServiceCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        // Create request based on parsed command
        var request = new Request
        {
            Command = parsedCommand.Type,
            FilePath = parsedCommand.FilePath,
            WindowHandle = parsedCommand.WindowHandle,
            MonitorHandle = parsedCommand.MonitorHandle,
            IsUsingWpr = !string.IsNullOrWhiteSpace(parsedCommand.WprpPath),
            FramesPerSecond = parsedCommand.FramesPerSecond,
            MaxBufferSizeMB = parsedCommand.MaxBufferSizeMB,
            CaptureCursor = parsedCommand.CaptureCursor
        };

        // Send command to service
        var response = await pipeClient.SendCommandAsync(request);

        // Handle response
        return HandleResponse(response, parsedCommand.Type);
    }

    private static int HandleResponse(Response response, CommandType commandType)
    {
        switch (response.Status)
        {
            case ResponseStatus.Success:
                Logger.Output(response.Message);
                
                if (commandType == CommandType.CheckStatus)
                {
                    Logger.Output($"Recording status: {(response.IsRecording ? "Active" : "Idle")}");
                }
                else if (commandType == CommandType.ListWindows)
                {
                    DisplayWindowsList(response);
                }
                else if (commandType == CommandType.ListMonitors)
                {
                    DisplayMonitorsList(response);
                }
                return 0;

            case ResponseStatus.AlreadyRecording:
                Console.Error.WriteLine("Error: A recording is already in progress");
                Console.Error.WriteLine("Use 'etwsnap stop <filepath>' to stop and save, or 'etwsnap cancel' to discard");
                return 1;

            case ResponseStatus.NotRecording:
                Console.Error.WriteLine("Error: No recording is currently active");
                return 1;

            case ResponseStatus.Error:
                Console.Error.WriteLine($"Error: {response.Message}");
                return 1;

            default:
                Console.Error.WriteLine($"Unknown response status: {response.Status}");
                return 1;
        }
    }

    private static void DisplayWindowsList(Response response)
    {
        if (!response.Data.TryGetValue("count", out var countStr) || !int.TryParse(countStr, out int count))
        {
            Logger.Output("No window data available");
            return;
        }

        if (count == 0)
        {
            Logger.Output("No capturable windows found");
            return;
        }

        Logger.OutputLine();
        Logger.Output("Available Windows:");
        Logger.Output("==================");

        for (int i = 0; i < count; i++)
        {
            var handle = response.Data.GetValueOrDefault($"window_{i}_handle", "N/A");
            var title = response.Data.GetValueOrDefault($"window_{i}_title", "N/A");
            var size = response.Data.GetValueOrDefault($"window_{i}_size", "N/A");

            Logger.Output($"  {title}");
            Logger.Output($"      Handle: {handle}");
            Logger.Output($"      Size:   {size}");
            Logger.OutputLine();
        }

        Logger.Output($"To record a specific window, use: etwsnap start --window <handle>");
    }

    private static void DisplayMonitorsList(Response response)
    {
        if (!response.Data.TryGetValue("count", out var countStr) || !int.TryParse(countStr, out int count))
        {
            Logger.Output("No monitor data available");
            return;
        }

        if (count == 0)
        {
            Logger.Output("No monitors found");
            return;
        }

        Logger.OutputLine();
        Logger.Output("Available Monitors:");
        Logger.Output("===================");

        for (int i = 0; i < count; i++)
        {
            var handle = response.Data.GetValueOrDefault($"monitor_{i}_handle", "N/A");
            var name = response.Data.GetValueOrDefault($"monitor_{i}_name", "N/A");
            var bounds = response.Data.GetValueOrDefault($"monitor_{i}_bounds", "N/A");
            var isPrimary = response.Data.GetValueOrDefault($"monitor_{i}_primary", "False") == "True";

            Logger.Output($"  {name}{(isPrimary ? " (PRIMARY)" : "")}");
            Logger.Output($"      Handle: {handle}");
            Logger.Output($"      Bounds: {bounds}");
            Logger.OutputLine();
        }

        Logger.Output($"To record a specific monitor, use: etwsnap start --monitor <handle>");
    }

    private static string GetWprOutputPath(string? screenshotPath)
    {
        // Determine output directory from the screenshot path
        string outputDirectory;
        if (string.IsNullOrEmpty(screenshotPath))
        {
            outputDirectory = Environment.CurrentDirectory;
        }
        else if (Directory.Exists(screenshotPath))
        {
            outputDirectory = screenshotPath;
        }
        else
        {
            outputDirectory = Path.GetDirectoryName(screenshotPath) ?? Environment.CurrentDirectory;
        }

        // Generate ETL filename with timestamp
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string etlFilename = $"etwsnap_trace_{timestamp}.etl";
        
        return Path.Combine(outputDirectory, etlFilename);
    }
}

