using System.Buffers.Binary;
using System.Text.Json;

namespace EtwSnap.Contracts.Protocol;

public static class MessageFraming
{
    public static async ValueTask WriteAsync(Stream stream, WireMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
        if (body.Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"Message length {body.Length} exceeds the {ProtocolConstants.MaximumMessageBytes}-byte limit.");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<WireMessage> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (bodyLength <= 0 || bodyLength > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"Invalid message length: {bodyLength}.");
        }

        var body = new byte[bodyLength];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<WireMessage>(body, ProtocolJson.Options)
            ?? throw new InvalidDataException("The message body is invalid.");
    }
}
