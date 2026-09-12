using EtwSnap.WpaPlugin.Artifacts;
using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Parsing;
using Microsoft.Performance.SDK;

namespace EtwSnap.WpaPlugin.Tests.Models;

public sealed class EtwSnapDataSetTests
{
    [Fact]
    public void InterleavedSessionsKeepIndependentFrameTimelines()
    {
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var events = new EtwSnapEvent[]
        {
            Started(firstSession, 0),
            Started(secondSession, 50),
            Frame(firstSession, 1, 100),
            Frame(secondSession, 1, 200),
            Frame(firstSession, 2, 300),
            Stopped(secondSession, 400),
            Stopped(firstSession, 500),
        };

        var dataSet = EtwSnapDataSet.Build(events);

        Assert.Equal(2, dataSet.Sessions.Count);
        var firstFrames = dataSet.Screenshots.Where(frame => frame.SessionId == firstSession).ToArray();
        var secondFrame = Assert.Single(dataSet.Screenshots, frame => frame.SessionId == secondSession);
        Assert.Equal([200L, 200L], firstFrames.Select(frame => frame.Duration.ToNanoseconds));
        Assert.Equal(200, secondFrame.Duration.ToNanoseconds);
        Assert.All(dataSet.Sessions, session => Assert.Equal(ArtifactResolutionState.NotFound, session.ArtifactState));
    }

    [Fact]
    public void DuplicateEtwFrameKeysDoNotCrashArtifactResolution()
    {
        var sessionId = Guid.NewGuid();
        var frame = Frame(sessionId, 7, 100);

        var dataSet = EtwSnapDataSet.Build([frame, frame with { Timestamp = Timestamp.FromNanoseconds(200) }]);

        Assert.Equal(2, dataSet.Screenshots.Count);
        Assert.All(dataSet.Screenshots, screenshot => Assert.Equal(sessionId, screenshot.SessionId));
    }

    [Fact]
    public void ArtifactCommitDoesNotExtendIncompleteCaptureDuration()
    {
        var sessionId = Guid.NewGuid();
        var frame = Frame(sessionId, 1, 100);
        var commit = new ArtifactCommittedEvent(
            Timestamp.FromNanoseconds(500),
            sessionId,
            2,
            @"D:\missing.etwsnap.zip",
            "missing.etwsnap.zip",
            2,
            2,
            0,
            0,
            new string('0', 64),
            new string('1', 64),
            "Complete",
            1,
            1,
            0,
            0,
            0,
            1,
            0);

        var session = Assert.Single(EtwSnapDataSet.Build([frame, commit]).Sessions);

        Assert.Equal(100, session.StartTime.ToNanoseconds);
        Assert.Equal(0, session.Duration.ToNanoseconds);
    }

    [Fact]
    public void SameSessionIdInDifferentEtlsRemainsSourceScoped()
    {
        var sessionId = Guid.NewGuid();
        var first = Frame(sessionId, 1, 100) with { SourcePath = @"D:\first.etl" };
        var second = Frame(sessionId, 2, 200) with { SourcePath = @"D:\second.etl" };

        var dataSet = EtwSnapDataSet.Build([first, second]);

        Assert.Equal(2, dataSet.Sessions.Count);
        Assert.Equal(2, dataSet.Screenshots.Count);
    }

    private static RecordingStartedEvent Started(Guid sessionId, long nanoseconds) => new(
        Timestamp.FromNanoseconds(nanoseconds), sessionId, 10_000_000, 30, 1024, 0, 0);

    private static FrameCapturedEvent Frame(Guid sessionId, ulong frameNumber, long nanoseconds) => new(
        Timestamp.FromNanoseconds(nanoseconds), sessionId, frameNumber, 10, 20, 2, 2, 1);

    private static RecordingStoppedEvent Stopped(Guid sessionId, long nanoseconds) => new(
        Timestamp.FromNanoseconds(nanoseconds), sessionId, 2, 2, 0, 0, 0);
}