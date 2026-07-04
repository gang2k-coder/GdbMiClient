using System.Runtime.CompilerServices;

namespace TestTarget;

public class Program
{
    public static int Counter;

    public static void Main(string[] args)
    {
        Console.WriteLine("DebugBridge Test Target started.");
        Console.WriteLine($"PID: {Environment.ProcessId}");

        int x = Add(3, 4);
        Console.WriteLine($"add(3,4) = {x}");

        int y = Multiply(5, 6);
        Console.WriteLine($"multiply(5,6) = {y}");

        int z = Factorial(6);
        Console.WriteLine($"factorial(6) = {z}");

        Console.WriteLine($"Counter = {Counter}");

        Console.WriteLine("Entering work loop...");
        for (int i = 0; i < 30; i++)
        {
            DoWork(i);
            Thread.Sleep(200);
        }

        Console.WriteLine("Done.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Add(int a, int b) { Counter++; return a + b; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Multiply(int a, int b) { Counter++; return a * b; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Factorial(int n)
    {
        Counter++;
        if (n <= 1) return 1;
        return n * Factorial(n - 1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DoWork(int iteration)
    {
        Counter++;
        _ = Math.Sqrt(iteration * 100.0);
    }
}
