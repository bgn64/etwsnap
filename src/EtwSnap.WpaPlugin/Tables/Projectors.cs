using EtwSnap.WpaPlugin.Models;
using Microsoft.Performance.SDK;

namespace EtwSnap.WpaPlugin.Tables;

internal static class Projectors
{
    public static Timestamp ScreenshotStart(ScreenshotRecord row) => row.StartTime;
    public static TimestampDelta ScreenshotDuration(ScreenshotRecord row) => row.Duration;
    public static string ScreenshotSessionId(ScreenshotRecord row) => row.SessionId.ToString("D");
    public static ulong FrameNumber(ScreenshotRecord row) => row.FrameNumber;
    public static string Availability(ScreenshotRecord row) => row.Availability.ToString();
    public static string ImagePath(ScreenshotRecord row) => row.ImagePath ?? string.Empty;
    public static uint Width(ScreenshotRecord row) => row.Width;
    public static uint Height(ScreenshotRecord row) => row.Height;
    public static uint PixelFormat(ScreenshotRecord row) => row.PixelFormat;
    public static long PresentationTime(ScreenshotRecord row) => row.PresentationTime100ns;
    public static long CallbackQpc(ScreenshotRecord row) => row.CallbackQpc;

    public static Timestamp SessionStart(SessionRecord row) => row.StartTime;
    public static TimestampDelta SessionDuration(SessionRecord row) => row.Duration;
    public static string SessionId(SessionRecord row) => row.SessionId.ToString("D");
    public static uint? FramesPerSecond(SessionRecord row) => row.FramesPerSecond;
    public static ulong? BufferBytes(SessionRecord row) => row.BufferBytes;
    public static uint? TargetKind(SessionRecord row) => row.TargetKind;
    public static ulong? TargetHandle(SessionRecord row) => row.TargetHandle;
    public static ulong? AcceptedFrames(SessionRecord row) => row.AcceptedFrames;
    public static ulong? RetainedFrames(SessionRecord row) => row.RetainedFrames;
    public static ulong? EvictedFrames(SessionRecord row) => row.EvictedFrames;
    public static ulong? DroppedFrames(SessionRecord row) => row.DroppedFrames;
    public static ulong? ErrorCount(SessionRecord row) => row.ErrorCount;
    public static ulong? ExportedFrames(SessionRecord row) => row.ExportedFrames;
    public static ulong? FailedFrames(SessionRecord row) => row.FailedFrames;
    public static string ArtifactState(SessionRecord row) => row.ArtifactState.ToString();
    public static string ManifestPath(SessionRecord row) => row.ManifestPath ?? string.Empty;
    public static string ArtifactDetail(SessionRecord row) => row.ArtifactDetail ?? string.Empty;
}