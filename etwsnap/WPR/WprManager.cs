using System.Diagnostics;

namespace ETWSnap.WPR;

/// <summary>
/// Manages WPR (Windows Performance Recorder) command execution
/// </summary>
public static class WprManager
{
    /// <summary>
    /// Starts WPR tracing with the specified WPRP profile
    /// </summary>
    /// <param name="wprpPath">Path to the WPRP profile file</param>
    /// <returns>True if WPR started successfully, false otherwise</returns>
    public static bool Start(string wprpPath)
    {
        if (string.IsNullOrWhiteSpace(wprpPath))
        {
            Console.Error.WriteLine("WPR: WPRP file path cannot be empty");
            return false;
        }

        if (!File.Exists(wprpPath))
        {
            Console.Error.WriteLine($"WPR: WPRP file not found: {wprpPath}");
            return false;
        }

        Console.WriteLine($"Starting WPR tracing with profile: {wprpPath}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wpr.exe",
                Arguments = $"-start \"{wprpPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("WPR: Failed to start wpr.exe process");
                return false;
            }

            process.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"WPR: Failed to start tracing (exit code {process.ExitCode})");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.Error.WriteLine($"WPR Error: {error}");
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine($"WPR Output: {output}");
                }
                return false;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"WPR: {output}");
            }

            Console.WriteLine("WPR tracing started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WPR: Exception while starting tracing: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stops WPR tracing and saves the trace to the specified file
    /// </summary>
    /// <param name="outputPath">Path where the ETL file should be saved</param>
    /// <returns>True if WPR stopped successfully, false otherwise</returns>
    public static bool Stop(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("WPR: Output path cannot be empty");
            return false;
        }

        Console.WriteLine($"Stopping WPR tracing and saving to: {outputPath}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wpr.exe",
                Arguments = $"-stop \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("WPR: Failed to start wpr.exe process");
                return false;
            }

            process.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"WPR: Failed to stop tracing (exit code {process.ExitCode})");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.Error.WriteLine($"WPR Error: {error}");
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine($"WPR Output: {output}");
                }
                return false;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"WPR: {output}");
            }

            Console.WriteLine($"WPR trace saved to: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WPR: Exception while stopping tracing: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cancels WPR tracing without saving
    /// </summary>
    /// <returns>True if WPR cancelled successfully, false otherwise</returns>
    public static bool Cancel()
    {
        Console.WriteLine("Cancelling WPR tracing...");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wpr.exe",
                Arguments = "-cancel",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("WPR: Failed to start wpr.exe process");
                return false;
            }

            process.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"WPR: Failed to cancel tracing (exit code {process.ExitCode})");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.Error.WriteLine($"WPR Error: {error}");
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine($"WPR Output: {output}");
                }
                return false;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"WPR: {output}");
            }

            Console.WriteLine("WPR tracing cancelled successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WPR: Exception while cancelling tracing: {ex.Message}");
            return false;
        }
    }
}
