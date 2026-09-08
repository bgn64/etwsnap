using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtwSnap.Contracts.Protocol;

public enum MessageKind
{
    Request,
    Progress,
    Response,
}

public enum CommandKind
{
    Ping,
    Start,
    Stop,
    Cancel,
    Status,
    ListWindows,
    ListMonitors,
}

public sealed record WireMessage
{
    public required int ProtocolVersion { get; init; }
    public required Guid RequestId { get; init; }
    public required MessageKind MessageKind { get; init; }
    public required CommandKind Command { get; init; }
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public int? ProgressPercent { get; init; }
    public JsonElement Payload { get; init; }

    public T ReadPayload<T>()
    {
        if (Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new InvalidDataException($"The {Command} message does not contain a payload.");
        }

        return Payload.Deserialize<T>(ProtocolJson.Options)
            ?? throw new InvalidDataException($"The {Command} payload is invalid.");
    }

    public static WireMessage CreateRequest<T>(CommandKind command, T payload) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        RequestId = Guid.NewGuid(),
        MessageKind = MessageKind.Request,
        Command = command,
        Payload = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
    };

    public static WireMessage CreateResponse<T>(WireMessage request, bool success, T payload, string? message = null, string? errorCode = null) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        RequestId = request.RequestId,
        MessageKind = MessageKind.Response,
        Command = request.Command,
        Success = success,
        ErrorCode = errorCode,
        Message = message,
        Payload = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
    };

    public static WireMessage CreateProgress(WireMessage request, int percent, string message) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        RequestId = request.RequestId,
        MessageKind = MessageKind.Progress,
        Command = request.Command,
        Success = true,
        Message = message,
        ProgressPercent = Math.Clamp(percent, 0, 100),
        Payload = JsonSerializer.SerializeToElement(new { }, ProtocolJson.Options),
    };
}

public static class ProtocolJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
