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
        var started = await fixture.Coordinator.StartAsync(CreateRequest() with { Trace = true }, default);
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
        Assert.Equal(["Reserve", "ArtifactReference", "CaptureStop", "WprStop", "Write", "ArtifactCommitted"], fixture.StopOperations);
        Assert.Equal(started.SessionId, fixture.ArtifactEvents.Reference?.SessionId);
        Assert.Equal("sessions/" + started.SessionId.ToString("N") + "/manifest.json", fixture.ArtifactEvents.Reference?.PortableManifestRelativePath);
        Assert.Equal("hash", fixture.ArtifactEvents.Committed?.ManifestSha256);
    }

    [Fact]
    public async Task ArtifactEventFailureDoesNotFailStop()
    {
        var fixture = new CoordinatorFixture();
        fixture.ArtifactEvents.Error = new InvalidOperationException("provider unavailable");
        await fixture.Coordinator.StartAsync(CreateRequest(), default);
        var request = WireMessage.CreateRequest(CommandKind.Stop, new StopCaptureRequest(@"D:\Captures"));

        var result = await fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default);

        Assert.Equal(3, result.ExportedFrames);
        Assert.Equal(CaptureSessionState.Idle, fixture.Coordinator.GetStatus().State);
    }

    [Fact]
    public async Task EmbeddedStopWithoutTraceIsRejectedBeforeCaptureStops()
    {
        var fixture = new CoordinatorFixture();
        await fixture.Coordinator.StartAsync(CreateRequest(), default);
        var request = WireMessage.CreateRequest(
            CommandKind.Stop,
            new StopCaptureRequest(@"D:\Captures", ArtifactTransport.Embedded));

        var exception = await Assert.ThrowsAsync<SessionException>(() => fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default));

        Assert.Equal(ErrorCodes.InvalidRequest, exception.ErrorCode);
        Assert.Equal(0, fixture.Capture.StopCalls);
        Assert.Equal(CaptureSessionState.Capturing, fixture.Coordinator.GetStatus().State);
        await fixture.Coordinator.CancelAsync(default);
    }

    [Fact]
    public async Task EmbeddedStopPublishesStandaloneEtl()
    {
        var fixture = new CoordinatorFixture();
        await fixture.Coordinator.StartAsync(CreateRequest() with { Trace = true }, default);
        var request = WireMessage.CreateRequest(
            CommandKind.Stop,
            new StopCaptureRequest(@"D:\Captures", ArtifactTransport.Embedded));

        var result = await fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default);

        Assert.Equal(ArtifactTransport.Embedded, result.RequestedArtifactTransport);
        Assert.Equal(ArtifactTransport.Embedded, result.ActualArtifactTransport);
        Assert.Equal(@"D:\Captures\session.etl", result.ArtifactPath);
        Assert.Null(result.OutputDirectory);
        Assert.Equal(
            ["EmbeddedReserve", "ArtifactReference", "CaptureStop", "WprStop", "Write", "EmbeddedPublish", "ArtifactCommitted"],
            fixture.StopOperations);
    }

    [Fact]
    public async Task EmbeddedReservationFailureFallsBackBeforeCaptureStops()
    {
        var fixture = new CoordinatorFixture();
        fixture.EmbeddedPublisher.ReservationError = new IOException("streams unsupported");
        await fixture.Coordinator.StartAsync(CreateRequest() with { Trace = true }, default);
        var request = WireMessage.CreateRequest(
            CommandKind.Stop,
            new StopCaptureRequest(@"D:\Captures", ArtifactTransport.Embedded));

        var result = await fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default);

        Assert.Equal(ArtifactTransport.Folder, result.ActualArtifactTransport);
        Assert.Contains("streams unsupported", result.ArtifactWarning);
        Assert.Equal(["EmbeddedReserve", "Reserve", "ArtifactReference", "CaptureStop", "WprStop", "Write", "ArtifactCommitted"], fixture.StopOperations);
    }

    [Fact]
    public async Task EmbeddedPublicationFailureReturnsFolderAndWarning()
    {
        var fixture = new CoordinatorFixture();
        fixture.EmbeddedPublisher.Publication = new ArtifactPublication(
            ArtifactTransport.Folder,
            @"D:\Captures\session",
            @"D:\Captures\session\manifest.json",
            @"D:\Captures\session\trace.etl",
            @"D:\Captures\session",
            "Embedded artifact publication failed; saved a normal session folder instead. disk full");
        await fixture.Coordinator.StartAsync(CreateRequest() with { Trace = true }, default);
        var request = WireMessage.CreateRequest(
            CommandKind.Stop,
            new StopCaptureRequest(@"D:\Captures", ArtifactTransport.Embedded));

        var result = await fixture.Coordinator.StopAsync(
            request.ReadPayload<StopCaptureRequest>(),
            _ => ValueTask.CompletedTask,
            request,
            default);

        Assert.Equal(ArtifactTransport.Folder, result.ActualArtifactTransport);
        Assert.Equal(@"D:\Captures\session", result.OutputDirectory);
        Assert.Contains("disk full", result.ArtifactWarning);
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
        public List<string> StopOperations { get; } = [];
        public FakeCaptureSession Capture { get; }
        public FakeCaptureFactory Factory { get; }
        public FakeArtifactEventEmitter ArtifactEvents { get; }
        public FakeEmbeddedArtifactPublisher EmbeddedPublisher { get; }
        public FakeRecoveryStore Recovery { get; } = new();
        public Exception? ReservationError { get; set; }

        public CoordinatorFixture()
        {
            Capture = new FakeCaptureSession(StopOperations);
            Factory = new FakeCaptureFactory(Capture);
            ArtifactEvents = new FakeArtifactEventEmitter(StopOperations);
            EmbeddedPublisher = new FakeEmbeddedArtifactPublisher(StopOperations);
            Coordinator = new CaptureSessionCoordinator(
                () => Factory,
                new FakeWprController(StopOperations),
                new FakeArtifactWriter(() => ReservationError, StopOperations),
                EmbeddedPublisher,
                ArtifactEvents,
                Recovery,
                new ValidTargetValidator());
        }

        public CaptureSessionCoordinator Coordinator { get; }
    }

    private sealed class FakeEmbeddedArtifactPublisher(List<string> stopOperations) : IEmbeddedArtifactPublisher
    {
        public Exception? ReservationError { get; set; }
        public ArtifactPublication Publication { get; set; } = new(
            ArtifactTransport.Embedded,
            null,
            null,
            @"D:\Captures\session.etl",
            @"D:\Captures\session.etl",
            null);

        public Task<EmbeddedArtifactReservation> ReserveAsync(
            string outputRoot,
            Guid sessionId,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken)
        {
            stopOperations.Add("EmbeddedReserve");
            if (ReservationError is not null)
            {
                throw ReservationError;
            }
            var staging = new ArtifactReservation(
                @"D:\Captures\.etwsnap-staging\session",
                @"D:\Captures\.etwsnap-staging\session\frames",
                @"D:\Captures\.etwsnap-staging\session\manifest.json",
                @"D:\Captures\.etwsnap-staging\session\trace.etl",
                @"D:\Captures\.etwsnap-staging\session\.reserved",
                null,
                true);
            return Task.FromResult(new EmbeddedArtifactReservation(
                staging,
                @"D:\Captures\session.etl",
                @"D:\Captures\session"));
        }

        public Task<ArtifactPublication> PublishAsync(
            EmbeddedArtifactReservation reservation,
            SessionMetadata session,
            string? fallbackReason,
            CancellationToken cancellationToken)
        {
            stopOperations.Add("EmbeddedPublish");
            return Task.FromResult(Publication);
        }
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

    private sealed class FakeCaptureSession(List<string> stopOperations) : INativeCaptureSession
    {
        public int StopCalls { get; private set; }
        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public void Stop()
        {
            stopOperations.Add("CaptureStop");
            ++StopCalls;
        }

        public NativeCaptureStats GetStats() => new(4, 4, 3, 1, 0, 0);

        public ulong GetFrameCount() => 3;

        public NativeFrameInfo GetFrameInfo(ulong index) => new(index + 1, 100 + (long)index, 200 + (long)index, 1, 1, 1, 4);

        public void CopyFrameBgra(ulong index, byte[] destination, uint stride)
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeArtifactWriter(Func<Exception?> reservationError, List<string> stopOperations) : IArtifactWriter
    {
        public ArtifactReservation Reserve(string outputRoot, Guid sessionId, DateTimeOffset startedAtUtc)
        {
            var error = reservationError();
            if (error is not null)
            {
                throw error;
            }
            stopOperations.Add("Reserve");
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
            Func<int, string, ValueTask> progress)
        {
            stopOperations.Add("Write");
            return Task.FromResult(new ArtifactWriteResult(reservation.DirectoryPath, reservation.ManifestPath, 3, 0, [], "hash", "Complete"));
        }
    }

    private sealed class FakeArtifactEventEmitter(List<string> stopOperations) : IArtifactEventEmitter
    {
        public ArtifactReferenceEvent? Reference { get; private set; }
        public ArtifactCommittedEvent? Committed { get; private set; }
        public Exception? Error { get; set; }

        public void EmitReference(ArtifactReferenceEvent artifact)
        {
            stopOperations.Add("ArtifactReference");
            if (Error is not null)
            {
                throw Error;
            }
            Reference = artifact;
        }

        public void EmitCommitted(ArtifactCommittedEvent artifact)
        {
            stopOperations.Add("ArtifactCommitted");
            if (Error is not null)
            {
                throw Error;
            }
            Committed = artifact;
        }
    }

    private sealed class FakeRecoveryStore : IRecoveryStore
    {
        public List<string> Statuses { get; } = [];
        public RecoveryArtifactState? Artifacts { get; private set; }

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

        public Task MarkArtifactsAsync(Guid sessionId, RecoveryArtifactState artifacts, CancellationToken cancellationToken)
        {
            Artifacts = artifacts;
            return Task.CompletedTask;
        }
    }

    private sealed class ValidTargetValidator : ITargetValidator
    {
        public bool IsValid(CaptureTarget target) => true;
    }

    private sealed class FakeWprController(List<string> stopOperations) : IWprController
    {
        public Task<WprSession> StartAsync(Guid sessionId, string? userProfileSelector, CancellationToken cancellationToken) =>
            Task.FromResult(new WprSession("test", [], "hash", null, null, null, "staging"));

        public Task StopAsync(WprSession session, string outputPath, CancellationToken cancellationToken)
        {
            stopOperations.Add("WprStop");
            return Task.CompletedTask;
        }

        public Task CancelAsync(WprSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelInstanceAsync(string instanceName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}