using System.Runtime.InteropServices;
using EtwSnap.Host.Capture;

namespace EtwSnap.UnitTests.Capture;

public sealed class NativeAbiTests
{
    [Fact]
    public void ManagedStructuresMatchNativeVersionThreeLayout()
    {
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.CreateOptions>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.FrameInfo>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.Stats>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.ArtifactReference>());
        Assert.Equal(144, Marshal.SizeOf<NativeMethods.ArtifactCommitted>());
    }
}