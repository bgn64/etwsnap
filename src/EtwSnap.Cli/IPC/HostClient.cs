using System.Diagnostics;
using System.IO.Pipes;
using EtwSnap.Cli.Infrastructure;
using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;

namespace EtwSnap.Cli.IPC;

internal sealed class HostClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private readonly string _pipeName = UserScopeNames.PipeName(CurrentUser.Sid);

    public async Task<WireMessage> SendAsync<TRequest>(
        CommandKind command,
        TRequest payload,
        Action<WireMessage>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureHostAsync(cancellationToken).ConfigureAwait(false);
        return await SendCoreAsync(WireMessage.CreateRequest(command, payload), onProgress, ConnectTimeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureHostAsync(CancellationToken cancellationToken)
    {
        if (await TryPingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var hostPath = Path.Combine(AppContext.BaseDirectory, "etwsnap.host.exe");
        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException("The ETWSnap host executable is missing.", hostPath);
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("Failed to start the ETWSnap host.");

        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"The ETWSnap host exited during startup with code {process.ExitCode}.");
            }

            if (await TryPingAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The ETWSnap host did not become ready in time.");
    }

    private async Task<bool> TryPingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendCoreAsync(
                WireMessage.CreateRequest(CommandKind.Ping, new EmptyRequest()),
                null,
                TimeSpan.FromMilliseconds(250),
                cancellationToken).ConfigureAwait(false);
            return response.Success && response.ReadPayload<PingResult>().ProtocolVersion == ProtocolConstants.CurrentVersion;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<WireMessage> SendCoreAsync(
        WireMessage request,
        Action<WireMessage>? onProgress,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCancellation.CancelAfter(connectTimeout);
        try
        {
            await pipe.ConnectAsync(connectCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out connecting to the ETWSnap host.");
        }

        await MessageFraming.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var response = await MessageFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (response.RequestId != request.RequestId)
            {
                throw new InvalidDataException("The host returned a response for a different request.");
            }

            if (response.MessageKind == MessageKind.Progress)
            {
                onProgress?.Invoke(response);
                continue;
            }

            if (response.MessageKind != MessageKind.Response)
            {
                throw new InvalidDataException($"Unexpected message kind: {response.MessageKind}.");
            }

            return response;
        }
    }
}
