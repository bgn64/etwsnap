using EtwSnap.WpaPlugin.Models;
using EtwSnap.WpaPlugin.Tables;
using Microsoft.Performance.SDK;

namespace EtwSnap.WpaPlugin.Tests.Tables;

public sealed class ScreenshotTableCommandsTests
{
    [Fact]
    public void OpenInDefaultViewerUsesShellHandler()
    {
        using var image = TemporaryImage.Create();

        var created = ScreenshotTableCommands.TryCreateViewStartInfo(
            [CreateRecord(ScreenshotAvailability.Saved, image.Path)],
            [0],
            out var startInfo);

        Assert.True(created);
        Assert.Equal(image.Path, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public void RevealInExplorerSelectsImage()
    {
        using var image = TemporaryImage.Create();

        var created = ScreenshotTableCommands.TryCreateExplorerStartInfo(
            [CreateRecord(ScreenshotAvailability.Saved, image.Path)],
            [0],
            out var startInfo);

        Assert.True(created);
        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal([$"/select,{image.Path}"], startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(ScreenshotAvailability.MissingFile)]
    [InlineData(ScreenshotAvailability.NotPersisted)]
    [InlineData(ScreenshotAvailability.ArtifactUnavailable)]
    public void CommandsRejectUnavailableRows(ScreenshotAvailability availability)
    {
        using var image = TemporaryImage.Create();
        var rows = new[] { CreateRecord(availability, image.Path) };

        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [0], out _));
        Assert.False(ScreenshotTableCommands.TryCreateExplorerStartInfo(rows, [0], out _));
    }

    [Fact]
    public void CommandsRejectMissingFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"etwsnap-missing-{Guid.NewGuid():N}.png");
        var rows = new[] { CreateRecord(ScreenshotAvailability.Saved, missingPath) };

        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [0], out _));
        Assert.False(ScreenshotTableCommands.TryCreateExplorerStartInfo(rows, [0], out _));
    }

    [Fact]
    public void CommandsRequireExactlyOneValidSelection()
    {
        using var image = TemporaryImage.Create();
        var rows = new[] { CreateRecord(ScreenshotAvailability.Saved, image.Path) };

        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [], out _));
        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [0, 0], out _));
        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [-1], out _));
        Assert.False(ScreenshotTableCommands.TryCreateViewStartInfo(rows, [1], out _));
    }

    private static ScreenshotRecord CreateRecord(ScreenshotAvailability availability, string? imagePath) => new(
        Timestamp.Zero,
        TimestampDelta.Zero,
        Guid.NewGuid(),
        1,
        availability,
        imagePath,
        1,
        1,
        1,
        0,
        0);

    private sealed class TemporaryImage : IDisposable
    {
        private TemporaryImage(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryImage Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"etwsnap-command-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, [137, 80, 78, 71]);
            return new TemporaryImage(path);
        }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}