using ETWSnap.IPC;

namespace ETWSnap.Client;

/// <summary>
/// Parsed command-line arguments
/// </summary>
public class ParsedCommand
{
    public CommandType Type { get; set; }
    public string? FilePath { get; set; }
    public string? OutputFilePath { get; set; }
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsClientSideOnly { get; set; } = false;
    public long WindowHandle { get; set; } = 0;
    public long MonitorHandle { get; set; } = 0;
}

/// <summary>
/// Interface for command-line parsing
/// </summary>
public interface ICommandParser
{
    ParsedCommand Parse(string[] args);
    void PrintUsage();
}

/// <summary>
/// Command-line parser for etwsnap utility
/// </summary>
public class CommandParser : ICommandParser
{
    public ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "No command specified"
            };
        }

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "start" => ParseStartCommand(args),
            "stop" => ParseStopCommand(args),
            "cancel" => ParseCancelCommand(args),
            "status" => ParseStatusCommand(args),
            "provider-info" => ParseProviderInfoCommand(args),
            "list-windows" => ParseListWindowsCommand(args),
            "list-monitors" => ParseListMonitorsCommand(args),
            "add-provider" => ParseAddProviderCommand(args),
            _ => new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = $"Unknown command: {command}"
            }
        };
    }

    private ParsedCommand ParseStartCommand(string[] args)
    {
        var result = new ParsedCommand
        {
            Type = CommandType.Start,
            IsValid = true
        };

        // Parse optional parameters
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--window" || arg == "-w")
            {
                if (i + 1 >= args.Length)
                {
                    return new ParsedCommand
                    {
                        IsValid = false,
                        ErrorMessage = $"{arg} requires a window handle value"
                    };
                }

                if (!TryParseHandle(args[i + 1], out long handle))
                {
                    return new ParsedCommand
                    {
                        IsValid = false,
                        ErrorMessage = $"Invalid window handle: {args[i + 1]}. Must be a valid handle (hex with 0x prefix or decimal)."
                    };
                }

                result.WindowHandle = handle;
                i++; // Skip next argument
            }
            else if (arg == "--monitor" || arg == "-m")
            {
                if (i + 1 >= args.Length)
                {
                    return new ParsedCommand
                    {
                        IsValid = false,
                        ErrorMessage = $"{arg} requires a monitor handle value"
                    };
                }

                if (!TryParseHandle(args[i + 1], out long handle))
                {
                    return new ParsedCommand
                    {
                        IsValid = false,
                        ErrorMessage = $"Invalid monitor handle: {args[i + 1]}. Must be a valid handle (hex with 0x prefix or decimal)."
                    };
                }

                result.MonitorHandle = handle;
                i++; // Skip next argument
            }
            else
            {
                return new ParsedCommand
                {
                    IsValid = false,
                    ErrorMessage = $"Unknown parameter: {arg}"
                };
            }
        }

        // Validate that both window and monitor are not specified
        if (result.WindowHandle != 0 && result.MonitorHandle != 0)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Cannot specify both --window and --monitor"
            };
        }

        return result;
    }



    private ParsedCommand ParseStopCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Stop command requires a file path argument"
            };
        }

        var filePath = args[1];
        
        // Basic validation
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "File path cannot be empty"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.Stop,
            FilePath = filePath,
            IsValid = true
        };
    }

    private ParsedCommand ParseCancelCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Cancel command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.Cancel,
            IsValid = true
        };
    }

    private ParsedCommand ParseStatusCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Status command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.CheckStatus,
            IsValid = true
        };
    }

    private ParsedCommand ParseProviderInfoCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Provider-info command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.CheckStatus, // Unused for client-side commands
            IsValid = true,
            IsClientSideOnly = true
        };
    }

    private ParsedCommand ParseListWindowsCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "List-windows command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.ListWindows,
            IsValid = true
        };
    }

    private ParsedCommand ParseListMonitorsCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "List-monitors command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.ListMonitors,
            IsValid = true
        };
    }

    private ParsedCommand ParseAddProviderCommand(string[] args)
    {
        if (args.Length < 3)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Add-provider command requires input and output WPRP file paths"
            };
        }

        var inputFilePath = args[1];
        var outputFilePath = args[2];
        
        // Basic validation for input file
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Input WPRP file path cannot be empty"
            };
        }

        // Basic validation for output file
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Output WPRP file path cannot be empty"
            };
        }

        // Check file extensions
        if (!inputFilePath.EndsWith(".wprp", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Input file must have .wprp extension"
            };
        }

        if (!outputFilePath.EndsWith(".wprp", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Output file must have .wprp extension"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.CheckStatus, // Unused for client-side commands
            FilePath = inputFilePath,
            OutputFilePath = outputFilePath,
            IsValid = true,
            IsClientSideOnly = true
        };
    }

    private bool TryParseHandle(string value, out long handle)
    {
        handle = 0;

        // Support both decimal and hexadecimal (0x prefix)
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out handle))
            {
                return true;
            }
        }
        else if (long.TryParse(value, out handle))
        {
            return true;
        }

        return false;
    }

    public void PrintUsage()
    {
        Console.WriteLine("ETWSnap - ETW Recording Utility");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  etwsnap start [options]          - Start a new recording session");
        Console.WriteLine("  etwsnap stop <filepath>          - Stop recording and save to file");
        Console.WriteLine("  etwsnap cancel                   - Cancel recording without saving");
        Console.WriteLine("  etwsnap status                   - Check recording status");
        Console.WriteLine("  etwsnap list-windows             - List all capturable windows");
        Console.WriteLine("  etwsnap list-monitors            - List all available monitors");
        Console.WriteLine("  etwsnap provider-info            - Display ETW provider information");
        Console.WriteLine("  etwsnap add-provider <input> <output> - Add ETWSnap provider to WPRP profile");
        Console.WriteLine();
        Console.WriteLine("Start Command Options:");
        Console.WriteLine("  --window <index>, -w <index>    - Capture a specific window (use list-windows to see indices)");
        Console.WriteLine("  --monitor <index>, -m <index>   - Capture a specific monitor (use list-monitors to see indices)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  etwsnap start                    - Start recording (captures primary monitor)");
        Console.WriteLine("  etwsnap start -m 0x20001         - Start recording monitor with handle 0x20001");
        Console.WriteLine("  etwsnap start -w 0x12345         - Start recording window with handle 0x12345");
        Console.WriteLine("  etwsnap list-windows             - List available windows");
        Console.WriteLine("  etwsnap list-monitors            - List available monitors");
        Console.WriteLine("  etwsnap stop recording.etl       - Stop and save recording");
        Console.WriteLine("  etwsnap cancel                   - Cancel without saving");
        Console.WriteLine("  etwsnap add-provider input.wprp output.wprp - Add ETWSnap provider to WPR profile");
    }
}
