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

        try
        {
            // Determine command name for routing
            string commandName = args[0].ToLowerInvariant();
            
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
        Console.WriteLine("ETWSnap ETW Provider Information:");
        Console.WriteLine();
        Console.WriteLine($"Provider Name: {ETWSnapConstants.ProviderName}");
        Console.WriteLine($"Provider GUID: {ETWSnapConstants.ProviderGuid}");
        Console.WriteLine();
        Console.WriteLine("Use this information when configuring ETW tracing tools like WPR or PerfView.");
        Console.WriteLine();
        Console.WriteLine("Example WPRP EventProvider definition:");
        Console.WriteLine($"  <EventProvider Id=\"{ETWSnapConstants.ProviderName}\" Name=\"{ETWSnapConstants.ProviderGuid}\">");
        Console.WriteLine("  </EventProvider>");
        return Task.FromResult(0);
    }

    private static Task<int> HandleAddProviderCommand(ParsedCommand parsedCommand)
    {
        Console.WriteLine($"Reading profile from: {parsedCommand.FilePath}");
        Console.WriteLine($"Writing modified profile to: {parsedCommand.OutputFilePath}");
        Console.WriteLine();
        
        bool success = WprpModifier.AddEtwSnapProviderToDefaultProfile(parsedCommand.FilePath!, parsedCommand.OutputFilePath!);
        
        return Task.FromResult(success ? 0 : 1);
    }

    private static async Task<int> HandleStartCommand(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        bool wprStarted = false;

        // If WPRP path is provided, start WPR tracing first
        if (!string.IsNullOrWhiteSpace(parsedCommand.WprpPath))
        {
            wprStarted = WprManager.Start(parsedCommand.WprpPath);
            if (!wprStarted)
            {
                Console.Error.WriteLine("Failed to start WPR tracing. Aborting start command.");
                return 1;
            }
        }

        // Send start command to service
        int result = await SendServiceCommand(pipeClient, parsedCommand);

        // If service start failed and we started WPR, cancel it
        if (result != 0 && wprStarted)
        {
            Console.WriteLine("Service start failed, cancelling WPR tracing...");
            WprManager.Cancel();
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
            Console.WriteLine();
            
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
            Console.WriteLine();
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

        Console.WriteLine("Service not running, starting...");
        
        if (!await serviceManager.StartServiceAsync())
        {
            return false;
        }

        Console.WriteLine("Service started successfully");
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
            IsUsingWpr = !string.IsNullOrWhiteSpace(parsedCommand.WprpPath)
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
                Console.WriteLine(response.Message);
                
                if (commandType == CommandType.CheckStatus)
                {
                    Console.WriteLine($"Recording status: {(response.IsRecording ? "Active" : "Idle")}");
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
            Console.WriteLine("No window data available");
            return;
        }

        if (count == 0)
        {
            Console.WriteLine("No capturable windows found");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Available Windows:");
        Console.WriteLine("==================");

        for (int i = 0; i < count; i++)
        {
            var handle = response.Data.GetValueOrDefault($"window_{i}_handle", "N/A");
            var title = response.Data.GetValueOrDefault($"window_{i}_title", "N/A");
            var size = response.Data.GetValueOrDefault($"window_{i}_size", "N/A");

            Console.WriteLine($"  {title}");
            Console.WriteLine($"      Handle: {handle}");
            Console.WriteLine($"      Size:   {size}");
            Console.WriteLine();
        }

        Console.WriteLine($"To record a specific window, use: etwsnap start --window <handle>");
    }

    private static void DisplayMonitorsList(Response response)
    {
        if (!response.Data.TryGetValue("count", out var countStr) || !int.TryParse(countStr, out int count))
        {
            Console.WriteLine("No monitor data available");
            return;
        }

        if (count == 0)
        {
            Console.WriteLine("No monitors found");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Available Monitors:");
        Console.WriteLine("===================");

        for (int i = 0; i < count; i++)
        {
            var handle = response.Data.GetValueOrDefault($"monitor_{i}_handle", "N/A");
            var name = response.Data.GetValueOrDefault($"monitor_{i}_name", "N/A");
            var bounds = response.Data.GetValueOrDefault($"monitor_{i}_bounds", "N/A");
            var isPrimary = response.Data.GetValueOrDefault($"monitor_{i}_primary", "False") == "True";

            Console.WriteLine($"  {name}{(isPrimary ? " (PRIMARY)" : "")}");
            Console.WriteLine($"      Handle: {handle}");
            Console.WriteLine($"      Bounds: {bounds}");
            Console.WriteLine();
        }

        Console.WriteLine($"To record a specific monitor, use: etwsnap start --monitor <handle>");
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

