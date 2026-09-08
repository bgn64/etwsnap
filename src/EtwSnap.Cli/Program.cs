namespace EtwSnap.Cli;

internal static class Program
{
    public static Task<int> Main(string[] args) => new CliApplication().InvokeAsync(args);
}
