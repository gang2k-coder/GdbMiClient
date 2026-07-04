// Hanoi Tower test program for DebugBridge (Linux)
// Compiled with GCC: g++ -g -O0 -o hanoi_linux hanoi_linux.cpp
//         or Clang: clang++ -g -O0 -o hanoi_linux hanoi_linux.cpp
//
// Features:
//   - N discs (default 5) randomly distributed across 3 pegs (valid state)
//   - Recursive solution moving all discs to peg 3 in sorted order
//   - Prints each move to stdout
//   - Prints total steps on completion
//   - Non-interactive — runs to completion

#include <stdio.h>
#include <stdlib.h>
#include <time.h>

#define MAX_N 64

// ---- pegs ----
// pegs[p][i] = disc number at position i on peg p
// peg_tops[p] = index of top element, -1 if empty
// steps = total moves executed

int pegs[3][MAX_N];
int peg_tops[3];
int steps = 0;

// ---- peg operations ----

static int rand_int(void)
{
    return rand();
}

// Randomly distribute discs 1..n across all 3 pegs.
// Places discs largest-first to guarantee a valid Hanoi state
// (a larger disc is never placed on top of a smaller one).
void init_pegs_random(int n)
{
    peg_tops[0] = -1;
    peg_tops[1] = -1;
    peg_tops[2] = -1;

    for (int d = n; d >= 1; d--)
    {
        int p = rand_int() % 3;
        peg_tops[p]++;
        pegs[p][peg_tops[p]] = d;
    }
}

// Find which peg disc 'd' is on
int find_disc_peg(int d)
{
    for (int p = 0; p < 3; p++)
    {
        for (int i = 0; i <= peg_tops[p]; i++)
        {
            if (pegs[p][i] == d) return p;
        }
    }
    return -1;
}

void move_one_disc(int disc, int from, int to)
{
    peg_tops[from]--;
    peg_tops[to]++;
    pegs[to][peg_tops[to]] = disc;
    steps++;

    printf("Move disc %d from peg %d to peg %d\n", disc, from + 1, to + 1);
}

// Recursively move all discs 1..n to the target peg.
// Works for any initial configuration by processing largest disc first.
void move_all(int n, int target)
{
    if (n == 0) return;

    int cur = find_disc_peg(n);
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

void hanoi_solve(int n)
{
    steps = 0;
    init_pegs_random(n);

    printf("Solving Tower of Hanoi for %d discs (random initial placement across 3 pegs)\n", n);

    move_all(n, 2); // target = peg 2 (0-indexed) = peg 3 (1-indexed)

    printf("Done! Total steps: %d\n", steps);
}

// ---- main ----

int main(int argc, char* argv[])
{
    int N = 5; // default

    if (argc > 1)
    {
        N = atoi(argv[1]);
        if (N < 1) N = 1;
        if (N > MAX_N) N = MAX_N;
    }

    srand((unsigned int)time(NULL));
    hanoi_solve(N);
    return 0;
}
