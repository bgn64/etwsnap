using System.Buffers.Binary;
using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;

namespace EtwSnap.UnitTests.Protocol;

public sealed class MessageFramingTests
{
    [Fact]
    public async Task RoundTripPreservesEnvelopeAndPayload()
    {
        var request = WireMessage.CreateRequest(CommandKind.Stop, new StopCaptureRequest(@"D:\Captures"));
        await using var stream = new MemoryStream();

        await MessageFraming.WriteAsync(stream, request);
        stream.Position = 0;
        var result = await MessageFraming.ReadAsync(stream);

        Assert.Equal(request.ProtocolVersion, result.ProtocolVersion);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(CommandKind.Stop, result.Command);
        Assert.Equal(@"D:\Captures", result.ReadPayload<StopCaptureRequest>().OutputRoot);
    }

    [Fact]
    public async Task ProgressMessageRoundTrips()
    {
        var request = WireMessage.CreateRequest(CommandKind.Stop, new StopCaptureRequest(@"D:\Captures"));
        var progress = WireMessage.CreateProgress(request, 50, "Saving screenshots");
        await using var stream = new MemoryStream();

        await MessageFraming.WriteAsync(stream, progress);
        stream.Position = 0;
        var result = await MessageFraming.ReadAsync(stream);

        Assert.Equal(MessageKind.Progress, result.MessageKind);
        Assert.Equal(50, result.ProgressPercent);
        Assert.Equal("Saving screenshots", result.Message);
    }

    [Fact]
    public async Task EmbeddedStopTransportRoundTrips()
    {
        var request = WireMessage.CreateRequest(
            CommandKind.Stop,
            new StopCaptureRequest(@"D:\Captures", ArtifactTransport.Embedded));
        await using var stream = new MemoryStream();

        await MessageFraming.WriteAsync(stream, request);
        stream.Position = 0;
        var result = await MessageFraming.ReadAsync(stream);

        Assert.Equal(ArtifactTransport.Embedded, result.ReadPayload<StopCaptureRequest>().ArtifactTransport);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ProtocolConstants.MaximumMessageBytes + 1)]
    public async Task ReadRejectsInvalidBodyLength(int bodyLength)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bodyLength);
        await using var stream = new MemoryStream(prefix);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await MessageFraming.ReadAsync(stream));

        Assert.Contains("Invalid message length", exception.Message);
    }
}