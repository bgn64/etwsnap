using System.Runtime.InteropServices;

class Program
{
    // Callback delegate for frame events
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FrameArrivedCallback(int width, int height, long timestamp, IntPtr userContext);

    // Import functions from the C++ DLL
    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Add(int a, int b);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void PrintMessage(string message);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Multiply(int a, int b, out int result);

    // Screen Capture API
    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Capture_Create(IntPtr windowHandle, int frameIntervalMs);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Capture_Start(IntPtr captureHandle);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Capture_Stop(IntPtr captureHandle);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Capture_Destroy(IntPtr captureHandle);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Capture_SetFrameCallback(IntPtr captureHandle, FrameArrivedCallback callback, IntPtr userContext);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Capture_SetCursorEnabled(IntPtr captureHandle, bool enabled);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Capture_IsCursorEnabled(IntPtr captureHandle);

    // Windows API for getting window handle
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    static void Main(string[] args)
    {
        Console.WriteLine("=== Testing C++ DLL Interop ===\n");

        // Test 1: Simple function call
        int sum = Add(5, 3);
        Console.WriteLine($"Add(5, 3) = {sum}");

        // Test 2: String passing
        PrintMessage("Hello from C#!");

        // Test 3: Output parameter
        if (Multiply(7, 6, out int product))
        {
            Console.WriteLine($"Multiply(7, 6) = {product}");
        }
        else
        {
            Console.WriteLine("Multiply failed!");
        }

        Console.WriteLine("\n=== Screen Capture Test ===\n");

        // Get the current window handle (this console window)
        IntPtr windowHandle = GetForegroundWindow();
        
        if (windowHandle == IntPtr.Zero)
        {
            Console.WriteLine("Failed to get window handle!");
            return;
        }

        var windowTitle = new System.Text.StringBuilder(256);
        GetWindowText(windowHandle, windowTitle, 256);
        Console.WriteLine($"Capturing window: {windowTitle}");

        // Create capture manager (capture at 5 FPS)
        IntPtr captureHandle = Capture_Create(windowHandle, 200);
        
        if (captureHandle == IntPtr.Zero)
        {
            Console.WriteLine("Failed to create capture!");
            return;
        }

        Console.WriteLine("Capture created successfully!");

        // Set up frame callback
        int frameCount = 0;
        FrameArrivedCallback callback = (width, height, timestamp, userContext) =>
        {
            frameCount++;
            var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime();
            Console.WriteLine($"Frame {frameCount}: {width}x{height} at {dateTime:HH:mm:ss.fff}");
        };

        Capture_SetFrameCallback(captureHandle, callback, IntPtr.Zero);
        Capture_SetCursorEnabled(captureHandle, true);

        Console.WriteLine($"Cursor capture enabled: {Capture_IsCursorEnabled(captureHandle)}");

        // Start capturing
        if (Capture_Start(captureHandle))
        {
            Console.WriteLine("Capture started! Press any key to stop...\n");
            Console.ReadKey(true);
        }
        else
        {
            Console.WriteLine("Failed to start capture!");
        }

        // Stop and cleanup
        Capture_Stop(captureHandle);
        Capture_Destroy(captureHandle);

        Console.WriteLine($"\n=== Capture stopped. Total frames: {frameCount} ===");
    }
}

