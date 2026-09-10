using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Commands;
using EtwSnap.Host.Infrastructure;
using EtwSnap.Host.IPC;
using EtwSnap.Host.Recovery;
using EtwSnap.Host.Sessions;
using EtwSnap.Host.Targets;
using EtwSnap.Host.Tracing;

namespace EtwSnap.Host;

internal static class Program
{
    public static async Task<int> Main()
    {
        using var instance = HostInstanceLock.TryAcquire();
        if (instance is null)
        {
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        var targets = new TargetEnumerator();
        var wpr = new WprController(WprPaths.SupplementalProfile);
        var recovery = new RecoveryStore();
        await recovery.RecoverAbandonedAsync(wpr, shutdown.Token).ConfigureAwait(false);
        var coordinator = new CaptureSessionCoordinator(
            () => new NativeCaptureFactory(),
            wpr,
            new SessionArtifactWriter(),
            NativeArtifactEventEmitter.Instance,
            recovery,
            targets);
        var dispatcher = new CommandDispatcher(coordinator, targets);
        await using var server = new PipeServer(UserScopeNames.PipeName(CurrentUser.Sid), dispatcher);
        var idleShutdown = MonitorIdleAsync(coordinator, shutdown);

        try
        {
            HostLog.Info("ETWSnap host ready.");
            await server.RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            HostLog.Error(exception.ToString());
            return 1;
        }
        finally
        {
            await coordinator.ShutdownAsync().ConfigureAwait(false);
            await shutdown.CancelAsync().ConfigureAwait(false);
            await idleShutdown.ConfigureAwait(false);
        }
    }

    private static async Task MonitorIdleAsync(CaptureSessionCoordinator coordinator, CancellationTokenSource shutdown)
    {
        var idleSince = DateTimeOffset.UtcNow;
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), shutdown.Token).ConfigureAwait(false);
                if (coordinator.GetStatus().State == Contracts.Models.CaptureSessionState.Idle)
                {
                    if (DateTimeOffset.UtcNow - idleSince >= TimeSpan.FromMinutes(5))
                    {
                        await shutdown.CancelAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    idleSince = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }
}
