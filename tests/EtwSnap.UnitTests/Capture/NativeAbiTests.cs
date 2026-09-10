using System.Runtime.InteropServices;
using EtwSnap.Host.Capture;

namespace EtwSnap.UnitTests.Capture;

public sealed class NativeAbiTests
{
    [Fact]
    public void ManagedStructuresMatchNativeVersionTwoLayout()
    {
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.CreateOptions>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.FrameInfo>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.Stats>());
        Assert.Equal(64, Marshal.SizeOf<NativeMethods.ArtifactReference>());
        Assert.Equal(136, Marshal.SizeOf<NativeMethods.ArtifactCommitted>());
    }
}