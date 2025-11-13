using System.Drawing;
using System.Drawing.Imaging;

namespace ETWSnap.Service.Recording;

/// <summary>
/// Supported image formats for saving frames
/// </summary>
public enum FrameImageFormat
{
    /// <summary>
    /// PNG format (lossless, larger file size)
    /// </summary>
    PNG,
    
    /// <summary>
    /// JPEG format (lossy, smaller file size)
    /// </summary>
    JPEG,
    
    /// <summary>
    /// BMP format (uncompressed, largest file size)
    /// </summary>
    BMP
}

/// <summary>
/// Options for saving frames
/// </summary>
public class FrameSaveOptions
{
    /// <summary>
    /// Directory where frames should be saved
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;
    
    /// <summary>
    /// Base filename for frames (frame number will be appended)
    /// </summary>
    public string BaseFilename { get; set; } = "frame";
    
    /// <summary>
    /// Image format to use for saving frames
    /// </summary>
    public FrameImageFormat Format { get; set; } = FrameImageFormat.PNG;
    
    /// <summary>
    /// JPEG quality (1-100), only used for JPEG format
    /// </summary>
    public long JpegQuality { get; set; } = 90;
}

/// <summary>
/// Handles saving captured frames to disk as individual image files
/// </summary>
public class FrameSaver
{
    /// <summary>
    /// Saves a list of frames to disk
    /// </summary>
    /// <param name="frames">The frames to save</param>
    /// <param name="options">Options controlling how frames are saved</param>
    /// <returns>Number of frames successfully saved</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    public static int SaveFrames(List<FrameData> frames, FrameSaveOptions options)
    {
        if (frames == null || frames.Count == 0)
        {
            Console.WriteLine("[FrameSaver] No frames to save");
            return 0;
        }

        if (string.IsNullOrEmpty(options.OutputDirectory))
        {
            Console.WriteLine("[FrameSaver] Output directory not specified");
            return 0;
        }

        // Create output directory if it doesn't exist
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FrameSaver] Failed to create output directory: {ex.Message}");
            return 0;
        }

        // Determine image format
        var imageFormat = GetImageFormat(options.Format);
        var extension = GetFileExtension(options.Format);

        Console.WriteLine($"[FrameSaver] Saving {frames.Count} frames to: {options.OutputDirectory}");
        Console.WriteLine($"[FrameSaver] Format: {options.Format}, Base filename: {options.BaseFilename}");

        int savedCount = 0;
        int totalFrames = frames.Count;
        int lastProgressPercent = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            
            try
            {
                // Skip frames without pixel data
                if (frame.PixelData == null || frame.PixelData.Length == 0)
                {
                    Console.WriteLine($"[FrameSaver] Skipping frame {frame.FrameNumber} - no pixel data");
                    continue;
                }

                // Generate filename
                string filename = Path.Combine(
                    options.OutputDirectory,
                    $"{options.BaseFilename}_{frame.FrameNumber:D6}{extension}"
                );

                // Save the frame
                SaveFrame(frame, filename, imageFormat, options);
                savedCount++;

                // Show progress every 10%
                int progressPercent = (savedCount * 100) / totalFrames;
                if (progressPercent >= lastProgressPercent + 10)
                {
                    Console.WriteLine($"[FrameSaver] Progress: {progressPercent}% ({savedCount}/{totalFrames} frames)");
                    lastProgressPercent = progressPercent;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FrameSaver] Failed to save frame {frame.FrameNumber}: {ex.Message}");
            }
        }

        Console.WriteLine($"[FrameSaver] Saved {savedCount} of {totalFrames} frames");
        return savedCount;
    }

    /// <summary>
    /// Saves a single frame to disk
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static void SaveFrame(FrameData frame, string filename, ImageFormat format, FrameSaveOptions options)
    {
        // Create bitmap from frame data
        // The pixel data is in BGRA format (4 bytes per pixel)
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        
        // Lock the bitmap for direct pixel access
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb
        );

        try
        {
            // Copy pixel data to bitmap
            if (frame.PixelData != null)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    frame.PixelData,
                    0,
                    bitmapData.Scan0,
                    frame.PixelData.Length
                );
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        // Save to file
        if (format == ImageFormat.Jpeg)
        {
            // Save with JPEG quality setting
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, options.JpegQuality);
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            
            if (jpegEncoder != null)
            {
                bitmap.Save(filename, jpegEncoder, encoderParams);
            }
            else
            {
                bitmap.Save(filename, format);
            }
        }
        else
        {
            bitmap.Save(filename, format);
        }
    }

    /// <summary>
    /// Gets the System.Drawing.Imaging.ImageFormat for a FrameImageFormat
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static ImageFormat GetImageFormat(FrameImageFormat format)
    {
        return format switch
        {
            FrameImageFormat.PNG => ImageFormat.Png,
            FrameImageFormat.JPEG => ImageFormat.Jpeg,
            FrameImageFormat.BMP => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };
    }

    /// <summary>
    /// Gets the file extension for a FrameImageFormat
    /// </summary>
    private static string GetFileExtension(FrameImageFormat format)
    {
        return format switch
        {
            FrameImageFormat.PNG => ".png",
            FrameImageFormat.JPEG => ".jpg",
            FrameImageFormat.BMP => ".bmp",
            _ => ".png"
        };
    }

    /// <summary>
    /// Gets the image encoder for a specific format
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }
}
