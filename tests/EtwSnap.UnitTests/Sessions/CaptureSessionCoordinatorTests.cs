using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Recovery;
using EtwSnap.Host.Sessions;
using EtwSnap.Host.Targets;
using EtwSnap.Host.Tracing;

namespace EtwSnap.UnitTests.Sessions;

public sealed class CaptureSessionCoordinatorTests
{
    [Fact]
    public async Task ProfileWithoutTraceIsRejectedBeforeNativeCreation()
    {
        var fixture = new CoordinatorFixture();
        var request = CreateRequest() with { Profile = @"C:\profile.wprp!Test.Verbose" };

        var exception = await Assert.ThrowsAsync<SessionException>(() => fixture.Coordinator.StartAsync(request, default));

        Assert.Equal(ErrorCodes.InvalidProfile, exception.ErrorCode);
        Assert.Equal(0, fixture.Factory.CreateCalls);
    }

    [Fact]
    public async Task FailedDestinationReservationLeavesCaptureRunningForRetry()
    {
        var fixture = new CoordinatorFixture { ReservationError = new IOException("read-only destination") };
        await fixture.Coordinator.StartAsync(CreateRequest(), default);
        var stopRequest = WireMessage.CreateRequest(CommandKind.Stop, new StopCaptureRequest(@"Z:\Denied"));

        var exception = await Assert.ThrowsAsync<SessionException>(() => fixture.Coordinator.StopAsync(
            stopRequest.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            stopRequest,
            default));

        Assert.Equal(ErrorCodes.OutputFailed, exception.ErrorCode);
        Assert.Equal(CaptureSessionState.Capturing, fixture.Coordinator.GetStatus().State);
        Assert.Equal(0, fixture.Capture.StopCalls);
        Assert.False(fixture.Capture.Disposed);

        await fixture.Coordinator.CancelAsync(default);
    }

    [Fact]
    public async Task CancelStopsAndDisposesNativeSession()
    {
        var fixture = new CoordinatorFixture();
        var started = await fixture.Coordinator.StartAsync(CreateRequest(), default);

        var result = await fixture.Coordinator.CancelAsync(default);

        Assert.Equal(started.SessionId, result.SessionId);
        Assert.Equal(1, fixture.Capture.StopCalls);
        Assert.True(fixture.Capture.Disposed);
        Assert.True(fixture.Factory.Disposed);
        Assert.Equal(CaptureSessionState.Idle, fixture.Coordinator.GetStatus().State);
        Assert.Contains(fixture.Recovery.Statuses, status => status == "Cancelled");
    }

    [Fact]
    public async Task SuccessfulStopPersistsAndReturnsToIdle()
    {
        var fixture = new CoordinatorFixture();
        var started = await fixture.Coordinator.StartAsync(CreateRequest(), default);
        var request = WireMessage.CreateRequest(CommandKind.Stop, new StopCaptureRequest(@"D:\Captures"));

        var result = await fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default);

        Assert.Equal(started.SessionId, result.SessionId);
        Assert.Equal(3, result.ExportedFrames);
        Assert.Equal(1, fixture.Capture.StopCalls);
        Assert.True(fixture.Capture.Disposed);
        Assert.Equal(CaptureSessionState.Idle, fixture.Coordinator.GetStatus().State);
    }

    private static StartCaptureRequest CreateRequest() => new(
        false,
        null,
        new CaptureTarget(CaptureTargetKind.PrimaryMonitor),
        30,
        500,
        true);

    private sealed class CoordinatorFixture
    {
        public FakeCaptureSession Capture { get; } = new();
        public FakeCaptureFactory Factory { get; }
        public FakeRecoveryStore Recovery { get; } = new();
        public Exception? ReservationError { get; set; }

        public CoordinatorFixture()
        {
            Factory = new FakeCaptureFactory(Capture);
            Coordinator = new CaptureSessionCoordinator(
                () => Factory,
                new FakeWprController(),
                new FakeArtifactWriter(() => ReservationError),
                Recovery,
                new ValidTargetValidator());
        }

        public CaptureSessionCoordinator Coordinator { get; }
    }

    private sealed class FakeCaptureFactory(FakeCaptureSession capture) : INativeCaptureFactory
    {
        public int CreateCalls { get; private set; }
        public bool Disposed { get; private set; }

        public INativeCaptureSession Create(Guid sessionId, StartCaptureRequest request)
        {
            ++CreateCalls;
            return capture;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeCaptureSession : INativeCaptureSession
    {
        public int StopCalls { get; private set; }
        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public void Stop() => ++StopCalls;

        public NativeCaptureStats GetStats() => new(4, 4, 3, 1, 0, 0);

        public ulong GetFrameCount() => 3;

        public NativeFrameInfo GetFrameInfo(ulong index) => new(index + 1, 100 + (long)index, 200 + (long)index, 1, 1, 1, 4);

        public void CopyFrameBgra(ulong index, byte[] destination, uint stride)
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeArtifactWriter(Func<Exception?> reservationError) : IArtifactWriter
    {
        public ArtifactReservation Reserve(string outputRoot, Guid sessionId, DateTimeOffset startedAtUtc)
        {
            var error = reservationError();
            if (error is not null)
            {
                throw error;
            }
            return new ArtifactReservation(
                @"D:\Captures\session",
                @"D:\Captures\session\frames",
                @"D:\Captures\session\manifest.json",
                @"D:\Captures\session\trace.etl",
                @"D:\Captures\session\.reserved",
                null);
        }

        public Task<ArtifactWriteResult> WriteAsync(
            ArtifactReservation reservation,
            SessionMetadata session,
            INativeCaptureSession capture,
            NativeCaptureStats stats,
            string? tracePath,
            IReadOnlyList<string> warnings,
            CancellationToken cancellationToken,
            Func<int, string, ValueTask> progress) => Task.FromResult(
                new ArtifactWriteResult(reservation.DirectoryPath, reservation.ManifestPath, 3, 0, []));
    }

    private sealed class FakeRecoveryStore : IRecoveryStore
    {
        public List<string> Statuses { get; } = [];

        public Task BeginAsync(Guid sessionId, DateTimeOffset startedAtUtc, StartCaptureRequest request, CancellationToken cancellationToken)
        {
            Statuses.Add("Starting");
            return Task.CompletedTask;
        }

        public Task MarkAsync(Guid sessionId, string status, string? outputDirectory, string? error, CancellationToken cancellationToken)
        {
            Statuses.Add(status);
            return Task.CompletedTask;
        }
    }

    private sealed class ValidTargetValidator : ITargetValidator
    {
        public bool IsValid(CaptureTarget target) => true;
    }

    private sealed class FakeWprController : IWprController
    {
        public Task<WprSession> StartAsync(Guid sessionId, string? userProfileSelector, CancellationToken cancellationToken) =>
            Task.FromResult(new WprSession("test", [], "hash", null, null, null, "staging"));

        public Task StopAsync(WprSession session, string outputPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelAsync(WprSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelInstanceAsync(string instanceName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}