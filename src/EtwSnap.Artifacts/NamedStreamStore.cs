using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace EtwSnap.Artifacts;

public sealed record NamedStreamInfo(string Name, long Size);

public sealed class NamedStreamStore
{
    public string GetStreamPath(string baseFilePath, string streamName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        if (streamName.IndexOfAny([':', '\\', '/']) >= 0)
        {
            throw new ArgumentException("The stream name contains an invalid character.", nameof(streamName));
        }
        return $"{Path.GetFullPath(baseFilePath)}:{streamName}";
    }

    public FileStream OpenRead(string baseFilePath, string streamName) =>
        new(GetStreamPath(baseFilePath, streamName), FileMode.Open, FileAccess.Read, FileShare.Read);

    public async Task WriteFromFileAsync(
        string baseFilePath,
        string streamName,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var created = false;
        try
        {
            await using var destination = new FileStream(
                GetStreamPath(baseFilePath, streamName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                try
                {
                    Delete(baseFilePath, streamName);
                }
                catch
                {
                }
            }
            throw;
        }
    }

    public bool Exists(string baseFilePath, string streamName)
    {
        try
        {
            using var stream = OpenRead(baseFilePath, streamName);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public void Delete(string baseFilePath, string streamName)
    {
        var streamPath = GetStreamPath(baseFilePath, streamName);
        if (!DeleteFile(streamPath) && Marshal.GetLastWin32Error() != ErrorFileNotFound)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to delete named stream '{streamName}'.");
        }
    }

    public IReadOnlyList<NamedStreamInfo> EnumerateEtwSnapStreams(string baseFilePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NTFS named streams require Windows.");
        }

        var result = new List<NamedStreamInfo>();
        var handle = FindFirstStreamW(Path.GetFullPath(baseFilePath), 0, out var data, 0);
        if (handle == InvalidHandleValue)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorHandleEof)
            {
                return result;
            }
            throw new Win32Exception(error, "Failed to enumerate named streams.");
        }

        try
        {
            do
            {
                if (TryNormalizeStreamName(data.StreamName, out var name) &&
                    name.StartsWith(EmbeddedArtifactConstants.StreamPrefix, StringComparison.Ordinal))
                {
                    result.Add(new NamedStreamInfo(name, data.StreamSize));
                }
            }
            while (FindNextStreamW(handle, out data));

            var finalError = Marshal.GetLastWin32Error();
            if (finalError != ErrorHandleEof)
            {
                throw new Win32Exception(finalError, "Failed while enumerating named streams.");
            }
        }
        finally
        {
            FindClose(handle);
        }

        return result.OrderBy(stream => stream.Name, StringComparer.Ordinal).ToArray();
    }

    public async Task PreflightAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Embedded artifacts require Windows and NTFS named streams.");
        }

        var canonicalDirectory = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(canonicalDirectory);
        RejectReparsePoint(canonicalDirectory);
        EnsureNamedStreamCapability(canonicalDirectory);
        var probePath = Path.Combine(canonicalDirectory, $".etwsnap-stream-probe-{Guid.NewGuid():N}.tmp");
        var probeStreamName = $"EtwSnap.Probe.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(probePath, [0x45, 0x54, 0x57], cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(GetStreamPath(probePath, probeStreamName), [0x53, 0x4E, 0x41, 0x50], cancellationToken).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(GetStreamPath(probePath, probeStreamName), cancellationToken).ConfigureAwait(false);
            if (!bytes.AsSpan().SequenceEqual(new byte[] { 0x53, 0x4E, 0x41, 0x50 }))
            {
                throw new IOException("Named stream preflight returned unexpected data.");
            }
            Delete(probePath, probeStreamName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new IOException($"The output location does not support writable NTFS named streams: {canonicalDirectory}", exception);
        }
        finally
        {
            File.Delete(probePath);
        }
    }

    public static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Reparse points are not supported for embedded artifact mutations: {path}");
        }
    }

    private static bool TryNormalizeStreamName(string nativeName, out string name)
    {
        name = string.Empty;
        const string suffix = ":$DATA";
        if (nativeName.Length <= suffix.Length + 1 ||
            nativeName[0] != ':' ||
            !nativeName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        name = nativeName[1..^suffix.Length];
        return name.Length > 0;
    }

    private static void EnsureNamedStreamCapability(string path)
    {
        var volumePath = new StringBuilder(261);
        if (!GetVolumePathNameW(path, volumePath, volumePath.Capacity))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resolve the output volume.");
        }
        if (!GetVolumeInformationW(
            volumePath.ToString(),
            null,
            0,
            out _,
            out _,
            out var fileSystemFlags,
            null,
            0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query the output volume.");
        }
        if ((fileSystemFlags & FileNamedStreams) == 0)
        {
            throw new IOException($"The output volume does not advertise named-stream support: {volumePath}");
        }
    }

    private const int ErrorFileNotFound = 2;
    private const int ErrorHandleEof = 38;
    private const uint FileNamedStreams = 0x00040000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string StreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(
        string fileName,
        int infoLevel,
        out Win32FindStreamData findStreamData,
        int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr findStream, out Win32FindStreamData findStreamData);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFile(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}