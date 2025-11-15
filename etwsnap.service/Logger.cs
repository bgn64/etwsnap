namespace ETWSnap.Service;

/// <summary>
/// Global logger for etwsnap.service application that supports verbose mode filtering
/// </summary>
public static class Logger
{
    private static bool _verboseEnabled = false;

    /// <summary>
    /// Enable or disable verbose logging
    /// </summary>
    public static bool VerboseEnabled
    {
        get => _verboseEnabled;
        set => _verboseEnabled = value;
    }

    /// <summary>
    /// Log an informational message (only shown when verbose mode is enabled)
    /// </summary>
    public static void Info(string message)
    {
        if (_verboseEnabled)
        {
            Console.WriteLine(message);
        }
    }

    /// <summary>
    /// Log an informational message with formatting (only shown when verbose mode is enabled)
    /// </summary>
    public static void Info(string format, params object[] args)
    {
        if (_verboseEnabled)
        {
            Console.WriteLine(format, args);
        }
    }

    /// <summary>
    /// Log a message that should always be shown (for user-facing output)
    /// </summary>
    public static void Output(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// Log a message with formatting that should always be shown (for user-facing output)
    /// </summary>
    public static void Output(string format, params object[] args)
    {
        Console.WriteLine(format, args);
    }

    /// <summary>
    /// Log an error message (always shown)
    /// </summary>
    public static void Error(string message)
    {
        Console.Error.WriteLine(message);
    }

    /// <summary>
    /// Log an error message with formatting (always shown)
    /// </summary>
    public static void Error(string format, params object[] args)
    {
        Console.Error.WriteLine(format, args);
    }

    /// <summary>
    /// Log an empty line (only shown when verbose mode is enabled)
    /// </summary>
    public static void InfoLine()
    {
        if (_verboseEnabled)
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Log an empty line that should always be shown
    /// </summary>
    public static void OutputLine()
    {
        Console.WriteLine();
    }
}
