namespace ETWSnap;

/// <summary>
/// Constants for ETWSnap ETW provider and recording defaults
/// </summary>
public static class ETWSnapConstants
{
    /// <summary>
    /// ETW Provider Name
    /// </summary>
    public const string ProviderName = "ETWSnap-Service";
    
    /// <summary>
    /// ETW Provider GUID
    /// </summary>
    public const string ProviderGuid = "524507bc-3009-5e8d-c071-00a1c641849f";
    
    /// <summary>
    /// Default frames per second for screen recording
    /// </summary>
    public const int DefaultFPS = 30;
    
    /// <summary>
    /// Default maximum buffer size in megabytes
    /// </summary>
    public const long DefaultBufferSizeMB = 500;
    
    /// <summary>
    /// Default setting for capturing the cursor
    /// </summary>
    public const bool DefaultCaptureCursor = true;
}
