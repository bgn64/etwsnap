namespace EtwSnap.Contracts.Models;

public static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedProtocol = "unsupported_protocol";
    public const string AlreadyRecording = "already_recording";
    public const string NotRecording = "not_recording";
    public const string InvalidTarget = "invalid_target";
    public const string InvalidProfile = "invalid_profile";
    public const string CaptureFailed = "capture_failed";
    public const string TraceFailed = "trace_failed";
    public const string OutputFailed = "output_failed";
    public const string InternalError = "internal_error";
}
