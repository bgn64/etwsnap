using System.Diagnostics;
using ETWSnap;

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
            Logger.Error($"WPR: WPRP file not found: {wprpPath}");
            return false;
        }

        Logger.Info($"Starting WPR tracing with profile: {wprpPath}");

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
                Logger.Error($"WPR: Failed to start tracing (exit code {process.ExitCode})");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Error($"WPR Error: {error}");
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.Error($"WPR Output: {output}");
                }
                return false;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Logger.Info($"WPR: {output}");
            }

            Logger.Info("WPR tracing started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"WPR: Exception while starting tracing: {ex.Message}");
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
            Logger.Error("WPR: Output path cannot be empty");
            return false;
        }

        Logger.Info($"Stopping WPR tracing and saving to: {outputPath}");

        try
        {
            // Don't redirect output - let wpr.exe write directly to console
            // This allows the user to see the progress bar and status messages
            var startInfo = new ProcessStartInfo
            {
                FileName = "wpr.exe",
                Arguments = $"-stop \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Error("WPR: Failed to start wpr.exe process");
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Logger.Error($"WPR: Failed to stop tracing (exit code {process.ExitCode})");
                return false;
            }

            Logger.Info($"WPR trace saved to: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"WPR: Exception while stopping tracing: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cancels WPR tracing without saving
    /// </summary>
    /// <returns>True if WPR cancelled successfully, false otherwise</returns>
    public static bool Cancel()
    {
        Logger.Info("Cancelling WPR tracing...");

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
                Logger.Error("WPR: Failed to start wpr.exe process");
                return false;
            }

            process.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                Logger.Error($"WPR: Failed to cancel tracing (exit code {process.ExitCode})");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Error($"WPR Error: {error}");
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.Error($"WPR Output: {output}");
                }
                return false;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Logger.Info($"WPR: {output}");
            }

            Logger.Info("WPR tracing cancelled successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"WPR: Exception while cancelling tracing: {ex.Message}");
            return false;
        }
    }
}
