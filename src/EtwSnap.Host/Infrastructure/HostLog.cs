namespace EtwSnap.Host.Infrastructure;

internal static class HostLog
{
    public static void Info(string message) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} INF {message}");

    public static void Error(string message) => Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} ERR {message}");
}
