using System.Runtime.InteropServices;

class Program
{
    // Import functions from the C++ DLL
    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Add(int a, int b);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void PrintMessage(string message);

    [DllImport("windows.graphics.capture.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Multiply(int a, int b, out int result);

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

        Console.WriteLine("\n=== All tests completed ===");
    }
}
