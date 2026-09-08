using System.Runtime.InteropServices;
using EtwSnap.Host.Capture;

namespace EtwSnap.UnitTests.Capture;

public sealed class NativeAbiTests
{
    [Fact]
    public void ManagedStructuresMatchNativeVersionOneLayout()
    {
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.CreateOptions>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.FrameInfo>());
        Assert.Equal(56, Marshal.SizeOf<NativeMethods.Stats>());
    }
}