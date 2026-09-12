using EtwSnap.Artifacts;

namespace EtwSnap.UnitTests.Artifacts;

public sealed class FileReplacementSetTests
{
    [Fact]
    public void DisposeRestoresOldTargetAndNewTemporaryFile()
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "capture.etl");
        var temporary = Path.Combine(fixture.Root, "new.etl.tmp");
        File.WriteAllText(target, "old");
        File.WriteAllText(temporary, "new");

        using (var replacement = FileReplacementSet.Create([target]))
        {
            replacement.Publish(temporary, target);
        }

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal("new", File.ReadAllText(temporary));
    }

    [Fact]
    public void CommitKeepsNewTargetAndRemovesTemporaryFile()
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "capture.etl");
        var temporary = Path.Combine(fixture.Root, "new.etl.tmp");
        File.WriteAllText(target, "old");
        File.WriteAllText(temporary, "new");

        using (var replacement = FileReplacementSet.Create([target]))
        {
            replacement.Publish(temporary, target);
            replacement.Commit();
        }

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(temporary));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"etwsnap-replacement-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}