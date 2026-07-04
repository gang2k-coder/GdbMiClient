// Hanoi Tower test program for DebugBridge (Windows)
// Compiled with MSVC:
//   cl /Zi /Od /Fe:hanoi_win.exe hanoi_win.cpp /link /DEBUG:FULL /ENTRY:mainCRTStartup /SUBSYSTEM:CONSOLE
//
// Features:
//   - N discs (default 5) randomly distributed across 3 pegs (valid state)
//   - Recursive solution moving all discs to peg 3 in sorted order
//   - Prints each move to stdout
//   - Prints total steps on completion
//   - Exported symbols for WinDbg: hanoi_solve, move_one_disc, g_steps, g_pegs
//   - No CRT dependency — uses kernel32.dll only

extern "C" {
__declspec(dllimport) void* GetStdHandle(unsigned long nStdHandle);
__declspec(dllimport) int WriteFile(
    void* hFile,
    const void* lpBuffer,
    unsigned long nNumberOfBytesToWrite,
    unsigned long* lpNumberOfBytesWritten,
    void* lpOverlapped
);
__declspec(dllimport) char* GetCommandLineA(void);
__declspec(dllimport) void ExitProcess(unsigned int uExitCode);
}

#define STD_OUTPUT_HANDLE ((unsigned long)-11)
#define MAX_N 64

// ---- pegs ----
// pegs[p][i] = disc number at position i on peg p
// peg_tops[p] = index of top element, -1 if empty
// g_steps = total moves executed

__declspec(dllexport) int g_pegs[3][MAX_N];
__declspec(dllexport) int g_peg_tops[3];
__declspec(dllexport) int g_steps = 0;

static void* s_stdout;

// ---- output helpers (no CRT) ----

static int StrLen(const char* s)
{
    int n = 0;
    while (s[n]) n++;
    return n;
}

static void WriteStr(const char* s)
{
    unsigned long written;
    WriteFile(s_stdout, s, StrLen(s), &written, 0);
}

static void WriteInt(int n)
{
    char buf[12];
    int i = 0;
    if (n == 0)
    {
        buf[i++] = '0';
    }
    else
    {
        int neg = 0;
        if (n < 0) { neg = 1; n = -n; }
        while (n > 0)
        {
            buf[i++] = '0' + (n % 10);
            n /= 10;
        }
        if (neg) buf[i++] = '-';
        for (int j = 0; j < i / 2; j++)
        {
            char t = buf[j];
            buf[j] = buf[i - 1 - j];
            buf[i - 1 - j] = t;
        }
    }
    buf[i] = '\0';
    WriteStr(buf);
}

// ---- simple PRNG ----

static unsigned int s_rand = 12345;

static int RandInt(void)
{
    s_rand = s_rand * 1103515245 + 12345;
    return (int)(s_rand >> 16) & 0x7FFF;
}

// ---- peg operations ----

// Randomly distribute discs 1..n across all 3 pegs.
// Places discs largest-first to guarantee a valid Hanoi state
// (a larger disc is never placed on top of a smaller one).
static void InitPegsRandom(int n)
{
    g_peg_tops[0] = -1;
    g_peg_tops[1] = -1;
    g_peg_tops[2] = -1;

    for (int d = n; d >= 1; d--)
    {
        int p = RandInt() % 3;
        g_peg_tops[p]++;
        g_pegs[p][g_peg_tops[p]] = d;
    }
}

// Find which peg disc 'd' is on
static int FindDiscPeg(int d)
{
    for (int p = 0; p < 3; p++)
    {
        for (int i = 0; i <= g_peg_tops[p]; i++)
        {
            if (g_pegs[p][i] == d) return p;
        }
    }
    return -1;
}

// Move the top disc from peg 'from' to peg 'to'
__declspec(dllexport) void move_one_disc(int disc, int from, int to)
{
    g_peg_tops[from]--;
    g_peg_tops[to]++;
    g_pegs[to][g_peg_tops[to]] = disc;
    g_steps++;

    WriteStr("Move disc ");
    WriteInt(disc);
    WriteStr(" from peg ");
    WriteInt(from + 1); // 1-indexed display
    WriteStr(" to peg ");
    WriteInt(to + 1);
    WriteStr("\n");
}

// Recursively move all discs 1..n to the target peg.
// Works for any initial configuration by processing largest disc first.
__declspec(dllexport) void move_all(int n, int target)
{
    if (n == 0) return;

    int cur = FindDiscPeg(n);
    if (cur == target)
    {
        // Disc n is already in place — move smaller discs onto it
        move_all(n - 1, target);
    }
    else
    {
        // Move smaller discs out of the way, move disc n, then
        // move smaller discs back onto it
        int other = 3 - cur - target; // the third peg
        move_all(n - 1, other);
        move_one_disc(n, cur, target);
        move_all(n - 1, target);
    }
}

// ---- entry point ----

__declspec(dllexport) void hanoi_solve(int n)
{
    g_steps = 0;
    InitPegsRandom(n);

    WriteStr("Solving Tower of Hanoi for ");
    WriteInt(n);
    WriteStr(" discs (random initial placement across 3 pegs)\n");

    move_all(n, 2); // target = peg 2 (0-indexed) = peg 3 (1-indexed display)

    WriteStr("Done! Total steps: ");
    WriteInt(g_steps);
    WriteStr("\n");
}

// Parse an integer from a string (no CRT)
static int ParseInt(const char* s)
{
    int n = 0;
    while (*s >= '0' && *s <= '9')
    {
        n = n * 10 + (*s - '0');
        s++;
    }
    return n;
}

// Skip whitespace and optional quoted executable path
static const char* SkipToArg(const char* cmdline)
{
    // Handle quoted executable path
    if (*cmdline == '"')
    {
        cmdline++;
        while (*cmdline && *cmdline != '"') cmdline++;
        if (*cmdline == '"') cmdline++;
    }
    else
    {
        while (*cmdline && *cmdline != ' ' && *cmdline != '\t') cmdline++;
    }
    // Skip whitespace
    while (*cmdline == ' ' || *cmdline == '\t') cmdline++;
    return cmdline;
}

extern "C" void __stdcall mainCRTStartup(void)
{
    s_stdout = GetStdHandle(STD_OUTPUT_HANDLE);

    int N = 5; // default
    const char* args = SkipToArg(GetCommandLineA());
    if (*args)
    {
        N = ParseInt(args);
        if (N < 1) N = 1;
        if (N > MAX_N) N = MAX_N;
    }

    hanoi_solve(N);
    ExitProcess(0);
}
