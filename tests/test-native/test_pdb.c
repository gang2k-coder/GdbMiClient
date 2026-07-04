// PDB test target for DebugBridge local-variable capture tests.
// Compile with MSVC: cl /Zi /Od /Fe:test_pdb.exe test_pdb.c
// Or clang-cl: clang-cl /Zi /Od /Fe:test_pdb.exe test_pdb.c
// Must be non-interactive — runs and exits without stdin.

__declspec(dllimport) void __stdcall ExitProcess(unsigned int uExitCode);

__declspec(dllexport) int g_counter = 0;

__declspec(dllexport) void after_points(void);

typedef struct {
    int x;
    int y;
} Point;

__declspec(dllexport) int compute_distance(Point* a, Point* b)
{
    int dx = a->x - b->x;
    int dy = a->y - b->y;
    int result = dx * dx + dy * dy;
    g_counter++;
    return result;
}

__declspec(dllexport) int process_points(int count)
{
    Point p1; p1.x = 3; p1.y = 4;
    Point p2; p2.x = 0; p2.y = 0;

    Point* a = &p1;       // simple pointer
    Point* b = &p2;       // simple pointer
    int sum = 0;
    int* ptr = &sum;      // int pointer

    for (int i = 0; i < count; i++)
    {
        int d = compute_distance(a, b);
        *ptr += d;
        p2.x++;
    }

    after_points();
    return *ptr;
}

__declspec(dllexport) void after_points(void)
{
    // intentional no-op — breakpoint target after the loop
}

void __stdcall mainCRTStartup(void)
{
    int result = process_points(3);
    g_counter = result;
    ExitProcess(0);
}
