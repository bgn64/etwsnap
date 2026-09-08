namespace EtwSnap.Contracts;

public static class EtwSnapConstants
{
    public const string ProviderName = "ETWSnap-Service";
    public static readonly Guid ProviderId = new("524507bc-3009-5e8d-c071-00a1c641849f");

    public const int DefaultFramesPerSecond = 30;
    public const long DefaultBufferMegabytes = 500;
    public const bool DefaultCaptureCursor = true;
}
