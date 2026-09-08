namespace EtwSnap.Host.Tracing;

internal static class WprPaths
{
    public static string SupplementalProfile => Path.Combine(AppContext.BaseDirectory, "profiles", "EtwSnap.wprp");
}
