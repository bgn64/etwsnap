namespace ETWSnap.Service.Recording;

/// <summary>
/// Represents a single captured frame from screen recording
/// </summary>
public class FrameData
{
    /// <summary>
    /// Width of the captured frame in pixels
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the captured frame in pixels
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Timestamp when the frame was captured (Unix milliseconds)
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// The actual pixel data (to be populated when we save frames to disk)
    /// For now, we'll just track metadata
    /// </summary>
    public byte[]? PixelData { get; set; }

    /// <summary>
    /// Estimated size in bytes (for buffer management)
    /// </summary>
    public long SizeInBytes => PixelData?.Length ?? (Width * Height * 4); // Assuming RGBA (4 bytes per pixel)

    /// <summary>
    /// Frame number in the recording sequence
    /// </summary>
    public int FrameNumber { get; set; }
}
