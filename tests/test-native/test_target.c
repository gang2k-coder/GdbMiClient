// Native test target for DebugBridge WinDbg E2E tests.
// Compiled with clang: clang -g -O0 -o test_target.exe test_target.c -fuse-ld=lld
// Exported symbols (add, multiply, factorial, g_counter) are visible to WinDbg.
// No CRT or Windows SDK dependencies — only kernel32.dll imports.

__declspec(dllimport) void __stdcall Sleep(unsigned long dwMilliseconds);
__declspec(dllimport) void __stdcall ExitProcess(unsigned int uExitCode);

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

// Loop body — called each iteration of the sleep loop.
// Provides a clean breakpoint target for go-capture-break workflow tests.
__declspec(dllexport) void loop_body(int i)
{
    g_counter++;
}

// Marker function called after the loop finishes.
// Serves as the "break" breakpoint target to stop and retrieve captures.
__declspec(dllexport) void after_loop(void)
{
    // intentional no-op — just a breakpoint target
}

void __stdcall mainCRTStartup(void)
{
    int sum = add(3, 4);
    int prod = multiply(5, 6);
    int fact = factorial(6);

    // Sleep loop — gives the debugger time to attach, set breakpoints,
    // and inspect state before the process exits.
    for (int i = 0; i < 5; i++)
    {
        Sleep(100);
        loop_body(i);
    }

    after_loop();
    ExitProcess(0);
}
