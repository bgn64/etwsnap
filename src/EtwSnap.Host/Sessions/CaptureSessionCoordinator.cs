using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.Artifacts;
using EtwSnap.Host.Capture;
using EtwSnap.Host.Infrastructure;
using EtwSnap.Host.Recovery;
using EtwSnap.Host.Targets;
using EtwSnap.Host.Tracing;

namespace EtwSnap.Host.Sessions;

internal sealed class CaptureSessionCoordinator(
    Func<INativeCaptureFactory> captureFactory,
    IWprController wpr,
    IArtifactWriter artifacts,
    IEmbeddedArtifactPublisher embeddedArtifacts,
    IArtifactEventEmitter artifactEvents,
    IRecoveryStore recovery,
    ITargetValidator targets)
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CaptureSessionState _state = CaptureSessionState.Idle;
    private ActiveSession? _active;
    private string? _lastError;

    public SessionStatus GetStatus()
    {
        ActiveSession? active;
        CaptureSessionState state;
        string? lastError;
        lock (_stateGate)
        {
            active = _active;
            state = _state;
            lastError = _lastError;
        }

        var stats = new NativeCaptureStats(0, 0, 0, 0, 0, 0);
        if (active is not null && state == CaptureSessionState.Capturing)
        {
            try
            {
                lock (active.NativeGate)
                {
                    stats = active.Capture.GetStats();
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }
        }

        return new SessionStatus(
            state,
            active?.Metadata.SessionId,
            active?.Metadata.StartedAtUtc,
            active?.Metadata.Wpr is not null,
            checked((long)stats.AcceptedFrames),
            checked((long)stats.RetainedFrames),
            checked((long)stats.EvictedFrames),
            lastError);
    }

    public async Task<StartCaptureResult> StartAsync(StartCaptureRequest request, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStartRequest(request);
            lock (_stateGate)
            {
                if (_state != CaptureSessionState.Idle)
                {
                    throw new SessionException(ErrorCodes.AlreadyRecording, "A recording is already active.");
                }
                _state = CaptureSessionState.Starting;
                _lastError = null;
            }

            var sessionId = Guid.NewGuid();
            var startedAtUtc = DateTimeOffset.UtcNow;
            WprSession? wprSession = null;
            INativeCaptureFactory? factory = null;
            INativeCaptureSession? capture = null;
            try
            {
                await recovery.BeginAsync(sessionId, startedAtUtc, request, cancellationToken).ConfigureAwait(false);
                if (request.Trace)
                {
                    wprSession = await wpr.StartAsync(sessionId, request.Profile, cancellationToken).ConfigureAwait(false);
                }

                factory = captureFactory();
                capture = factory.Create(sessionId, request);
                capture.Start();

                var metadata = new SessionMetadata(sessionId, startedAtUtc, request, wprSession);
                lock (_stateGate)
                {
                    _active = new ActiveSession(metadata, factory, capture);
                    _state = CaptureSessionState.Capturing;
                }
                await recovery.MarkAsync(sessionId, "Capturing", null, null, cancellationToken).ConfigureAwait(false);
                return new StartCaptureResult(sessionId, startedAtUtc, request.Trace);
            }
            catch (Exception exception)
            {
                capture?.Dispose();
                factory?.Dispose();
                if (wprSession is not null)
                {
                    try
                    {
                        await wpr.CancelAsync(wprSession, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                else if (request.Trace)
                {
                    try
                    {
                        await wpr.CancelInstanceAsync($"EtwSnap_{sessionId:N}", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                await TryMarkRecoveryAsync(sessionId, "Failed", null, exception.Message).ConfigureAwait(false);
                SetIdle(exception.Message);
                throw MapException(exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<StopCaptureResult> StopAsync(
        StopCaptureRequest request,
        Func<WireMessage, ValueTask> progress,
        WireMessage sourceRequest,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = GetActiveOrThrow();
            if (!Enum.IsDefined(request.ArtifactTransport))
            {
                throw new SessionException(ErrorCodes.InvalidRequest, "The requested artifact transport is not supported.");
            }
            if (request.ArtifactTransport == ArtifactTransport.Embedded && active.Metadata.Wpr is null)
            {
                throw new SessionException(ErrorCodes.InvalidRequest, "--embed-artifacts requires a session started with --trace.");
            }

            var warnings = new List<string>();
            EmbeddedArtifactReservation? embeddedReservation = null;
            ArtifactReservation reservation;
            try
            {
                if (request.ArtifactTransport == ArtifactTransport.Embedded)
                {
                    try
                    {
                        embeddedReservation = await embeddedArtifacts.ReserveAsync(
                            request.OutputRoot,
                            active.Metadata.SessionId,
                            active.Metadata.StartedAtUtc,
                            cancellationToken).ConfigureAwait(false);
                        reservation = embeddedReservation.Staging;
                    }
                    catch (Exception exception)
                    {
                        var warning = $"Embedded artifacts are unavailable; saving a normal session folder instead. {exception.Message}";
                        warnings.Add(warning);
                        reservation = artifacts.Reserve(request.OutputRoot, active.Metadata.SessionId, active.Metadata.StartedAtUtc);
                    }
                }
                else
                {
                    reservation = artifacts.Reserve(request.OutputRoot, active.Metadata.SessionId, active.Metadata.StartedAtUtc);
                }
            }
            catch (Exception exception)
            {
                throw new SessionException(ErrorCodes.OutputFailed, $"The output destination was not accepted; recording is still active. {exception.Message}", exception);
            }

            await TryMarkRecoveryArtifactsAsync(
                active.Metadata.SessionId,
                new RecoveryArtifactState(
                    request.ArtifactTransport,
                    embeddedReservation is null ? ArtifactTransport.Folder : null,
                    embeddedReservation?.Staging.DirectoryPath,
                    embeddedReservation?.FinalEtlPath,
                    embeddedReservation is null
                        ? null
                        : EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(active.Metadata.SessionId))).ConfigureAwait(false);

            var artifactReference = ArtifactEvents.CreateReference(
                active.Metadata.SessionId,
                embeddedReservation?.FallbackDirectoryPath ?? reservation.DirectoryPath);
            TryEmitArtifactEvent(
                () => artifactEvents.EmitReference(artifactReference),
                "ArtifactReference",
                warnings);

            SetState(CaptureSessionState.StoppingCapture);
            string? tracePath = null;
            string? embeddedFallbackReason = null;
            var stage = "stopping native capture";

            try
            {
                await TryMarkRecoveryAsync(active.Metadata.SessionId, "Stopping", reservation.DirectoryPath, null).ConfigureAwait(false);
                NativeCaptureStats stats;
                lock (active.NativeGate)
                {
                    active.Capture.Stop();
                    stats = active.Capture.GetStats();
                }

                if (active.Metadata.Wpr is not null)
                {
                    stage = "stopping WPR";
                    SetState(CaptureSessionState.StoppingTrace);
                    try
                    {
                        await wpr.StopAsync(active.Metadata.Wpr, reservation.TracePath, cancellationToken).ConfigureAwait(false);
                        tracePath = reservation.TracePath;
                    }
                    catch (Exception exception)
                    {
                        warnings.Add($"Trace stop failed: {exception.Message}");
                        embeddedFallbackReason = $"Trace stop failed: {exception.Message}";
                        try
                        {
                            await wpr.CancelAsync(active.Metadata.Wpr, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception cancelException)
                        {
                            warnings.Add($"Trace cleanup failed: {cancelException.Message}");
                        }
                    }
                }

                stage = "persisting session artifacts";
                SetState(CaptureSessionState.Persisting);
                await TryMarkRecoveryAsync(active.Metadata.SessionId, "Persisting", reservation.DirectoryPath, null).ConfigureAwait(false);
                var result = await artifacts.WriteAsync(
                    reservation,
                    active.Metadata,
                    active.Capture,
                    stats,
                    tracePath,
                    warnings,
                    cancellationToken,
                    (percent, message) => TryReportProgressAsync(progress, WireMessage.CreateProgress(sourceRequest, percent, message))).ConfigureAwait(false);

                ArtifactPublication publication;
                if (embeddedReservation is not null)
                {
                    stage = "publishing embedded artifacts";
                    publication = await embeddedArtifacts.PublishAsync(
                        embeddedReservation,
                        active.Metadata,
                        embeddedFallbackReason,
                        cancellationToken).ConfigureAwait(false);
                    if (publication.Warning is not null)
                    {
                        warnings.Add(publication.Warning);
                    }
                }
                else
                {
                    publication = new ArtifactPublication(
                        ArtifactTransport.Folder,
                        result.OutputDirectory,
                        result.ManifestPath,
                        tracePath,
                        result.OutputDirectory,
                        warnings.FirstOrDefault(message => message.StartsWith("Embedded artifacts are unavailable", StringComparison.Ordinal)));
                }

                await TryMarkRecoveryArtifactsAsync(
                    active.Metadata.SessionId,
                    new RecoveryArtifactState(
                        request.ArtifactTransport,
                        publication.Transport,
                        embeddedReservation?.Staging.DirectoryPath,
                        publication.Transport == ArtifactTransport.Embedded ? publication.TracePath : null,
                        publication.Transport == ArtifactTransport.Embedded
                            ? EtwSnap.Artifacts.EmbeddedArtifactConstants.GetStreamName(active.Metadata.SessionId)
                            : null)).ConfigureAwait(false);

                TryEmitArtifactEvent(
                    () => artifactEvents.EmitCommitted(ArtifactEvents.CreateCommitted(artifactReference, result, stats)),
                    "ArtifactCommitted");

                active.Dispose();
                await TryMarkRecoveryAsync(
                    active.Metadata.SessionId,
                    result.FailedFrames == 0 && warnings.Count == 0 ? "Complete" : "Partial",
                    publication.ArtifactPath,
                    result.Errors.Count == 0 ? null : string.Join(Environment.NewLine, result.Errors)).ConfigureAwait(false);
                SetIdle(null);

                return new StopCaptureResult(
                    active.Metadata.SessionId,
                    publication.OutputDirectory,
                    publication.ManifestPath,
                    publication.TracePath,
                    result.ExportedFrames,
                    checked((long)stats.EvictedFrames),
                    request.ArtifactTransport,
                    publication.Transport,
                    publication.ArtifactPath,
                    publication.Warning);
            }
            catch (Exception exception)
            {
                var stagedException = new InvalidOperationException($"Failed while {stage}: {exception.Message}", exception);
                await CleanupFailedStopAsync(active, reservation, stagedException).ConfigureAwait(false);
                throw new SessionException(ErrorCodes.OutputFailed, stagedException.Message, stagedException);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static void TryEmitArtifactEvent(Action emit, string eventName, ICollection<string>? warnings = null)
    {
        try
        {
            emit();
        }
        catch (Exception exception)
        {
            var message = $"Failed to emit {eventName}: {exception.Message}";
            warnings?.Add(message);
            HostLog.Error(message);
        }
    }

    public async Task<CancelCaptureResult> CancelAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = GetActiveOrThrow();
            SetState(CaptureSessionState.Cancelling);
            var errors = new List<string>();
            try
            {
                lock (active.NativeGate)
                {
                    active.Capture.Stop();
                }
            }
            catch (Exception exception)
            {
                errors.Add($"Capture cleanup failed: {exception.Message}");
            }

            if (active.Metadata.Wpr is not null)
            {
                try
                {
                    await wpr.CancelAsync(active.Metadata.Wpr, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add($"Trace cleanup failed: {exception.Message}");
                }
            }

            var error = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
            try
            {
                active.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add($"Native disposal failed: {exception.Message}");
                error = string.Join(Environment.NewLine, errors);
            }

            SetIdle(error);
            await TryMarkRecoveryAsync(active.Metadata.SessionId, "Cancelled", null, error).ConfigureAwait(false);

            if (error is not null)
            {
                throw new SessionException(ErrorCodes.InternalError, $"The session was cancelled with cleanup errors: {error}");
            }
            return new CancelCaptureResult(active.Metadata.SessionId);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            if (GetStatus().SessionId is not null)
            {
                await CancelAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    private void EnsureStartRequest(StartCaptureRequest request)
    {
        if (request.Profile is not null && !request.Trace)
        {
            throw new SessionException(ErrorCodes.InvalidProfile, "--profile requires tracing.");
        }
        if (request.FramesPerSecond is < 1 or > 120 || request.BufferMegabytes <= 0)
        {
            throw new SessionException(ErrorCodes.InvalidRequest, "Capture FPS or buffer size is invalid.");
        }
        if (!targets.IsValid(request.Target))
        {
            throw new SessionException(ErrorCodes.InvalidTarget, "The requested capture target is no longer valid.");
        }
    }

    private ActiveSession GetActiveOrThrow()
    {
        lock (_stateGate)
        {
            if (_state != CaptureSessionState.Capturing || _active is null)
            {
                throw new SessionException(ErrorCodes.NotRecording, "No recording is active.");
            }
            return _active;
        }
    }

    private async Task CleanupFailedStopAsync(ActiveSession active, ArtifactReservation reservation, Exception exception)
    {
        if (active.Metadata.Wpr is not null)
        {
            try
            {
                await wpr.CancelAsync(active.Metadata.Wpr, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
        active.Dispose();
        await TryMarkRecoveryAsync(active.Metadata.SessionId, "Failed", reservation.DirectoryPath, exception.Message).ConfigureAwait(false);
        SetIdle(exception.Message);
    }

    private async Task TryMarkRecoveryAsync(Guid sessionId, string status, string? outputDirectory, string? error)
    {
        try
        {
            await recovery.MarkAsync(sessionId, status, outputDirectory, error, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryMarkRecoveryArtifactsAsync(Guid sessionId, RecoveryArtifactState artifactsState)
    {
        try
        {
            await recovery.MarkArtifactsAsync(sessionId, artifactsState, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async ValueTask TryReportProgressAsync(Func<WireMessage, ValueTask> progress, WireMessage message)
    {
        try
        {
            await progress(message).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SetState(CaptureSessionState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    private void SetIdle(string? lastError)
    {
        lock (_stateGate)
        {
            _active = null;
            _state = CaptureSessionState.Idle;
            _lastError = lastError;
        }
    }

    private static SessionException MapException(Exception exception, string? fallbackCode = null) => exception switch
    {
        SessionException sessionException => sessionException,
        WprException => new SessionException(ErrorCodes.TraceFailed, exception.Message, exception),
        NativeCaptureException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException =>
            new SessionException(ErrorCodes.CaptureFailed, exception.Message, exception),
        _ => new SessionException(fallbackCode ?? ErrorCodes.InternalError, exception.Message, exception),
    };

    private sealed class ActiveSession(
        SessionMetadata metadata,
        INativeCaptureFactory factory,
        INativeCaptureSession capture) : IDisposable
    {
        public object NativeGate { get; } = new();
        public SessionMetadata Metadata { get; } = metadata;
        public INativeCaptureSession Capture { get; } = capture;

        public void Dispose()
        {
            Capture.Dispose();
            factory.Dispose();
        }
    }
}

internal sealed class SessionException(string errorCode, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
