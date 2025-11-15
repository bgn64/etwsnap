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
            // Handle client-side only commands
            if (parsedCommand.IsClientSideOnly)
            {
                return ExecuteClientSideCommand(args[0].ToLowerInvariant(), parsedCommand);
            }

            // Ensure service is running for service-based commands
            if (!await EnsureServiceRunningAsync(serviceManager))
            {
                Console.Error.WriteLine("Failed to start or connect to service");
                return 1;
            }

            // Execute command
            using var pipeClient = new NamedPipeClient();
            var exitCode = await ExecuteCommandAsync(pipeClient, parsedCommand);
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteClientSideCommand(string command, ParsedCommand parsedCommand)
    {
        switch (command)
        {
            case "provider-info":
                return ExecuteProviderInfo();
            
            case "add-provider":
                return ExecuteAddProvider(parsedCommand.FilePath!, parsedCommand.OutputFilePath!);
            
            default:
                Console.Error.WriteLine($"Unknown client-side command: {command}");
                return 1;
        }
    }

    private static int ExecuteProviderInfo()
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
        return 0;
    }

    private static int ExecuteAddProvider(string inputFilePath, string outputFilePath)
    {
        Console.WriteLine($"Reading profile from: {inputFilePath}");
        Console.WriteLine($"Writing modified profile to: {outputFilePath}");
        Console.WriteLine();
        
        bool success = WprpModifier.AddEtwSnapProviderToDefaultProfile(inputFilePath, outputFilePath);
        
        return success ? 0 : 1;
    }

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

    private static async Task<int> ExecuteCommandAsync(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        // Create request based on parsed command
        var request = new Request
        {
            Command = parsedCommand.Type,
            FilePath = parsedCommand.FilePath,
            WindowHandle = parsedCommand.WindowHandle,
            MonitorHandle = parsedCommand.MonitorHandle
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
}

