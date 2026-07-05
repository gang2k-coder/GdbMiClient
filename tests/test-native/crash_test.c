// Linux crash test program for LoadDump E2E test.
// Compile: gcc -g -O0 -o crash_test crash_test.c
// Generates a core dump on SIGSEGV (ulimit -c unlimited).

#include <stdio.h>
#include <stdlib.h>
#include <sys/resource.h>

void crash_me(int n)
{
    (void)n;
    int *null_ptr = NULL;
    *null_ptr = 42;  // SIGSEGV — deliberate crash
}

int main(void)
{
    // Enable core dumps
    struct rlimit rl = { RLIM_INFINITY, RLIM_INFINITY };
    setrlimit(RLIMIT_CORE, &rl);

    printf("About to crash...\n");
    fflush(stdout);
    crash_me(1);
    printf("Should not reach here\n");
    return 0;
}
