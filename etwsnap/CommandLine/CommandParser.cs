using ETWSnap.IPC;

namespace ETWSnap.Client;

/// <summary>
/// Parsed command-line arguments
/// </summary>
public class ParsedCommand
{
    public CommandType Type { get; set; }
    public string? FilePath { get; set; }
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsClientSideOnly { get; set; } = false;
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
            _ => new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = $"Unknown command: {command}"
            }
        };
    }

    private ParsedCommand ParseStartCommand(string[] args)
    {
        if (args.Length > 1)
        {
            return new ParsedCommand
            {
                IsValid = false,
                ErrorMessage = "Start command does not accept additional arguments"
            };
        }

        return new ParsedCommand
        {
            Type = CommandType.Start,
            IsValid = true
        };
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

    public void PrintUsage()
    {
        Console.WriteLine("ETWSnap - ETW Recording Utility");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  etwsnap start                    - Start a new recording session");
        Console.WriteLine("  etwsnap stop <filepath>          - Stop recording and save to file");
        Console.WriteLine("  etwsnap cancel                   - Cancel recording without saving");
        Console.WriteLine("  etwsnap status                   - Check recording status");
        Console.WriteLine("  etwsnap provider-info            - Display ETW provider information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  etwsnap start");
        Console.WriteLine("  etwsnap stop C:\\recordings\\trace.etl");
        Console.WriteLine("  etwsnap cancel");
        Console.WriteLine("  etwsnap provider-info");
    }
}
