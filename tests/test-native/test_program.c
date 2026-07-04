// Native test program for DebugBridge WinDbg E2E tests.
// No CRT dependencies — uses kernel32.dll directly.
// Functions: add, multiply, factorial (recursive), global counter.

__declspec(dllexport) int g_counter = 0;

__declspec(dllexport) int add(int a, int b)
{
    g_counter++;
    return a + b;
}

__declspec(dllexport) int multiply(int a, int b)
{
    g_counter++;
    return a * b;
}

__declspec(dllexport) int factorial(int n)
{
    g_counter++;
    if (n <= 1)
        return 1;
    return n * factorial(n - 1);
}

__declspec(dllexport) void __stdcall SleepLoop(int ms_per_iter, int iterations)
{
    for (int i = 0; i < iterations; i++)
    {
        // Call kernel32 Sleep via dllimport
        extern void __stdcall Sleep(unsigned long);
        Sleep(ms_per_iter);
        g_counter++;
    }
}

void __stdcall mainCRTStartup(void)
{
    int x = add(3, 4);
    int y = multiply(5, 6);
    int z = factorial(6);
    SleepLoop(200, 10);  // ~2 seconds to give debugger time
    // ExitProcess(0) would need kernel32 import
    // Just return — not ideal but works for debugging
}
