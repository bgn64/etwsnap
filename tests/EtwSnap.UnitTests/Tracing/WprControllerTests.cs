using EtwSnap.Host.Tracing;
using System.Security.Cryptography;

namespace EtwSnap.UnitTests.Tracing;

public sealed class WprControllerTests
{
    [Fact]
    public void StartArgumentsComposeProfilesAndPlaceInstanceNameLast()
    {
        var arguments = WprController.CreateStartArguments(
            "EtwSnap_123",
            @"C:\Program Files\ETWSnap\EtwSnap.wprp",
            @"D:\Profiles\Performance.wprp!Performance.Verbose");

        Assert.Equal(
            new[]
            {
                "-start",
                @"C:\Program Files\ETWSnap\EtwSnap.wprp!EtwSnap.Verbose",
                "-start",
                @"D:\Profiles\Performance.wprp!Performance.Verbose",
                "-instancename",
                "EtwSnap_123",
            },
            arguments);
    }

    [Fact]
    public void ParseProfileSelectorValidatesNameAndDetailLevel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etwsnap-profile-{Guid.NewGuid():N}.wprp");
        try
        {
            File.WriteAllText(path, """
                <WindowsPerformanceRecorder>
                  <Profiles>
                    <Profile Name="Performance" DetailLevel="Verbose" />
                  </Profiles>
                </WindowsPerformanceRecorder>
                """);

            var result = WprController.ParseProfileSelector($"{path}!Performance.Verbose");
            Assert.Equal(path, result.Path);
            Assert.EndsWith("!Performance.Verbose", result.Selector);
            Assert.Throws<WprException>(() => WprController.ParseProfileSelector($"{path}!Missing.Verbose"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StageProfileCopiesExactBytesAndReturnsTheirHash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etwsnap-stage-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source.wprp");
        var destination = Path.Combine(root, "staged", "profile.wprp");
        var contents = "<WindowsPerformanceRecorder><Profiles /></WindowsPerformanceRecorder>"u8.ToArray();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(source, contents);

            var hash = await WprController.StageProfileAsync(source, destination, default);

            Assert.Equal(contents, await File.ReadAllBytesAsync(source));
            Assert.Equal(contents, await File.ReadAllBytesAsync(destination));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant(), hash);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}