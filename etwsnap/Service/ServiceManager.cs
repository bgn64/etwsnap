using System.Diagnostics;

namespace ETWSnap.Client;

/// <summary>
/// Interface for managing the etwsnap service process
/// </summary>
public interface IServiceManager
{
    Task<bool> IsServiceRunningAsync(CancellationToken cancellationToken = default);
    Task<bool> StartServiceAsync(CancellationToken cancellationToken = default);
    Task<bool> StartServiceAsync(bool verboseMode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages the lifecycle of the etwsnap.service process
/// </summary>
public class ServiceManager : IServiceManager
{
    private const string ServiceProcessName = "etwsnap.service";
    private const int ServiceStartupWaitMs = 1000;
    private readonly string _serviceExecutablePath;

    public ServiceManager()
    {
        // Get the directory where the current executable is located
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        _serviceExecutablePath = Path.Combine(currentDir, "etwsnap.service.exe");
    }

    public ServiceManager(string serviceExecutablePath)
    {
        _serviceExecutablePath = serviceExecutablePath;
    }

    public Task<bool> IsServiceRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var processes = Process.GetProcessesByName(ServiceProcessName);
            return Task.FromResult(processes.Length > 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> StartServiceAsync(CancellationToken cancellationToken = default)
    {
        return StartServiceAsync(false, cancellationToken);
    }

    public async Task<bool> StartServiceAsync(bool verboseMode, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if already running
            if (await IsServiceRunningAsync(cancellationToken))
            {
                return true;
            }

            // Verify the service executable exists
            if (!File.Exists(_serviceExecutablePath))
            {
                Console.Error.WriteLine($"Service executable not found at: {_serviceExecutablePath}");
                return false;
            }

            // Start the service process
            var startInfo = new ProcessStartInfo
            {
                FileName = _serviceExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            // Pass verbose flag to service if enabled
            if (verboseMode)
            {
                startInfo.Arguments = "--verbose";
            }

            var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start service process");
                return false;
            }

            // Wait a bit for the service to initialize
            await Task.Delay(ServiceStartupWaitMs, cancellationToken);

            // Verify it's running
            return await IsServiceRunningAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting service: {ex.Message}");
            return false;
        }
    }
}
