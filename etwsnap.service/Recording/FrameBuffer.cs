namespace ETWSnap.Service.Recording;

/// <summary>
/// A circular buffer for storing captured frames with a maximum size limit
/// </summary>
public class FrameBuffer
{
    private readonly LinkedList<FrameData> _frames = new();
    private readonly object _lockObj = new();
    private readonly long _maxSizeInBytes;
    private long _currentSizeInBytes = 0;

    /// <summary>
    /// Default buffer size: 500 MB
    /// </summary>
    public const long DefaultMaxSizeInBytes = 500L * 1024 * 1024;

    public FrameBuffer(long maxSizeInBytes = DefaultMaxSizeInBytes)
    {
        _maxSizeInBytes = maxSizeInBytes;
    }

    /// <summary>
    /// Current number of frames in the buffer
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lockObj)
            {
                return _frames.Count;
            }
        }
    }

    /// <summary>
    /// Current size of the buffer in bytes
    /// </summary>
    public long CurrentSizeInBytes
    {
        get
        {
            lock (_lockObj)
            {
                return _currentSizeInBytes;
            }
        }
    }

    /// <summary>
    /// Maximum size of the buffer in bytes
    /// </summary>
    public long MaxSizeInBytes => _maxSizeInBytes;

    /// <summary>
    /// Adds a frame to the buffer, removing oldest frames if necessary to stay within size limit
    /// </summary>
    public void AddFrame(FrameData frame)
    {
        lock (_lockObj)
        {
            // Remove oldest frames until we have room for the new frame
            while (_frames.Count > 0 && _currentSizeInBytes + frame.SizeInBytes > _maxSizeInBytes)
            {
                var oldestFrame = _frames.First!.Value;
                _frames.RemoveFirst();
                _currentSizeInBytes -= oldestFrame.SizeInBytes;
            }

            // Add the new frame
            _frames.AddLast(frame);
            _currentSizeInBytes += frame.SizeInBytes;
        }
    }

    /// <summary>
    /// Gets all frames in chronological order (oldest to newest)
    /// </summary>
    public List<FrameData> GetAllFrames()
    {
        lock (_lockObj)
        {
            return new List<FrameData>(_frames);
        }
    }

    /// <summary>
    /// Clears all frames from the buffer
    /// </summary>
    public void Clear()
    {
        lock (_lockObj)
        {
            _frames.Clear();
            _currentSizeInBytes = 0;
        }
    }

    /// <summary>
    /// Gets buffer statistics
    /// </summary>
    public BufferStats GetStats()
    {
        lock (_lockObj)
        {
            return new BufferStats
            {
                FrameCount = _frames.Count,
                CurrentSizeInBytes = _currentSizeInBytes,
                MaxSizeInBytes = _maxSizeInBytes,
                UtilizationPercent = _maxSizeInBytes > 0 ? (_currentSizeInBytes * 100.0 / _maxSizeInBytes) : 0,
                OldestFrameTimestamp = _frames.First?.Value.Timestamp,
                NewestFrameTimestamp = _frames.Last?.Value.Timestamp
            };
        }
    }
}

/// <summary>
/// Statistics about the frame buffer
/// </summary>
public class BufferStats
{
    public int FrameCount { get; set; }
    public long CurrentSizeInBytes { get; set; }
    public long MaxSizeInBytes { get; set; }
    public double UtilizationPercent { get; set; }
    public long? OldestFrameTimestamp { get; set; }
    public long? NewestFrameTimestamp { get; set; }
}
